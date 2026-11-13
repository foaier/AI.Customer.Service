# 本地接待、消息与回执接口（2026-11-10）

本项目主写接待状态和消息；用户中心主写账号、组、成员。正式接待路径统一以 `/api/customer-service` 开始，不在路径中区分 V1/V2，也不注册旧版本前缀的兼容别名；请求带当前登录 `Authorization: Bearer ...`。不要用目录测试的 `X-CS-Mock-*` 头授权接单。

等待选择、通知重试、调查恢复/决策/回执及访客/客服 SSE 的新增契约与部署步骤见 [survey-waiting-realtime.md](survey-waiting-realtime.md)。

在线/容量、带代次心跳、同组改派、可信系统任务和JSON/Prometheus指标见 [availability-system-metrics.md](availability-system-metrics.md)；原工单最终回复/受理后结案见 [followup-response.md](followup-response.md)。

## 接待 API

| 方法与路径                                          | 语义 / 参数                                                                                |
| --------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| `GET /agents/queue?topOrgId&assistantConfigId`      | 即时核验登录账号、租户、组织 ENABLED 及服务器路由组成员，返回待接单和本人接待，最多 100 条 |
| `GET /agents/conversations/{id}/reception?topOrgId` | 客服恢复接待摘要                                                                           |
| `POST /agents/conversations/{id}/claim?topOrgId`    | 原子认领 REQUESTED；body 为 `requestId, expectedVersion`                                   |
| `POST /agents/conversations/{id}/resolve?topOrgId`  | 当前认领者结束 CLAIMED/ACTIVE，与恢复 AI 同事务；相同 body 重试返回首次回执                |
| `GET /agents/conversations/{id}/history?topOrgId`   | 仅正式认领客服可读 AI 历史最小文本投影，不返回 request DTO / 内部系统消息                  |
| `GET /conversations/{id}/reception`                 | 原申请访客恢复当前接待                                                                     |
| `POST /conversations/{id}/reception/cancel`         | 原申请者取消 REQUESTED，与接单竞争；body 为 `requestId, expectedVersion`                   |

状态为 `REQUESTED → CLAIMED → ACTIVE → RESOLVED`，以及 `REQUESTED → CANCELLED`。CLAIMED 是原子接单事实；第一条客服消息才是 ACTIVE。取消/结束不是 AI 解决证据，不直接发问卷。

组织只作为待校验选择条件，不能覆盖服务器持久归属。消息正文只对原申请者或当前真实认领客服开放。同组其他客服无权读正文。每次客服HTTP操作都会重新读取用户中心资格，撤权后即拒绝。手动接单只需登录、组权限及容量，不要求先上线或心跳；改派目标仍须明确在线。

## 人工消息与水位

访客使用 `/conversations/{id}`；客服使用 `/agents/conversations/{id}` 并附 `topOrgId`。

| 方法与后缀                           | 请求 / 响应                                                                                                                      |
| ------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------- |
| `POST /messages`                     | `{"clientMsgId":"重试复用键","content":"1–4000 字符纯文本"}`；返回服务器 messageId、seq、episodeId、角色、发送者、正文和首次时间 |
| `GET /messages?afterSeq=0&limit=100` | 返回 `messages, receipt, peerReceipt`；本人和对方的回执均为 deliveredSeq/readSeq，未接单对方为零                                 |
| `POST /receipts`                     | `{"kind":"DELIVERED或READ","seq":1}`；READ 同时证明 DELIVERED，旧水位不回退，未来水位拒绝                                        |

seq 在同 conversation 单调递增，数据库复合键保护租户/组织/会话。消息发送成功只代表持久；对方读取后显式回执才代表收到/已读。人工实时统一为同源WebSocket，断线携最后游标重连，不再维持两秒HTTP轮询；两个旧`GET /conversations/{id}/events`、`GET /agents/conversations/{id}/events`人工SSE接口已删除并返回404。登录JWT通过首帧传输；每秒读取检查本地成员/认领，用户中心账号/组织每60秒复核一次，失败关闭，详见[routing-realtime.md](routing-realtime.md)。AI文本生成的原SSE协议保留。稳定clientMsgId同参重放返回同一消息，异参冲突；接待结束后的首次成功消息重试仍可返回原回执，不能新增消息。

统一响应：`{"code":"OK","data":...}`，`Cache-Control: no-store`。无权 403，非法参数 400，版本/状态/异参冲突 409，下游故障 503；不回显 SQL、凭据或内部异常。

## AI Tool 与确认卡

- `proposeHandoff(reasonCode, confidence)`：仅允许 `UNRESOLVED_AFTER_RETRY/USER_DISSATISFIED/REQUIRES_HUMAN_JUDGMENT`，置信度须达到既有策略阈值；身份与 session/turn/trace/assistant/bvent 由运行时绑定。持久 proposal/Outbox 后发 `event:card`，payload 的 status 为 `handoff.proposed`，data 含 proposalId、version、PENDING、userFacingReason、expiresAt。
- `requestHandoff()`：无模型参数；复核原始用户话语与正式登录上下文。用户明确要求则确定性调用，不依赖模型是否记得调用，也不再确认。通知 status 为 `handoff.requested`。
- 已有 pending API 恢复卡片；decision API 的 CONFIRM 创建唯一申请，DISMISS 只保存拒绝。拒绝前后保持 AI；确认后 HUMAN fencing 拒绝迟到 AI。
- 来源分别为 `VISITOR_DIRECT_REQUEST/USER_REQUESTED` 与 `AI_RECOMMENDATION_CONFIRMED/实际模型原因码`。问卷不满意后的明确opt-in使用`SURVEY_DISSATISFIED_OPT_IN/USER_DISSATISFIED`；可信服务端最终故障为`SYSTEM_POLICY/SYSTEM_FAILURE或SAFETY_OR_URGENT`，模型与HTTP不能创建系统来源。

## 配置与迁移

接待接口随应用启用，无独立enabled开关。`customer-service.reception.routes` 是 assistantConfigId → groupId 的服务器配置，默认空且拒绝客服操作。用户确认的联调 route：`41fd45d2-bf35-4924-b9fc-634d6cbc72d4 → fe6864e1-26be-4bc6-a4b7-a5ddfbef4977`，验收 accountId 为 `749bc8c6-5af7-46bd-9871-8230275e6531`；accountId 不作为可配置绕过授权项。

2026-11-11 起，基线及接待增量统一位于 `data/sql/{mysql|kingbase}/ai_20261111_customer_service/all_in_one.sql`。首次安装按整份顺序执行；已运行过基线的库，仅按来源标记提取尚未执行的接待及后续完整增量，见[数据库入口](database-20261111.md)。禁止重跑已部署来源段或清空申请。

本次实测开发库 MySQL 5.7.35；MySQL 脚本条件执行 8.x 的 CHECK 变更，并用 10 个 INSERT/UPDATE 触发器补齐 5.7 的状态、消息及水位值域校验。主键、唯一键、非空与复合 FK 仍在数据库强制。Kingbase 使用 CHECK，尚无真实目标库验收。
