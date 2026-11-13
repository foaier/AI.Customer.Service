# 等待、调查及人工实时消息增量

本项目是接单、接待、等待、通知任务及人工消息的写入 owner；内部系统唯一维护账号、组和成员。Conversation owner 维护回复权、调查及其事件。开发阶段不兼容旧外部接待历史，人工历史 Adapter 以本项目客服周期为完整覆盖；本文描述已实现契约，不代表生产或目标数据库验收通过。

客服最终回复与结案是后续独立增量，状态、API、可靠发送及两类业务入口见 [followup-response.md](followup-response.md)。

## 部署及配置

2026-11-11 起，统一使用 `data/sql/<dialect>/ai_20261111_customer_service/all_in_one.sql`。其中 followup、survey_runtime、followup_response 三个完整来源段依次承载等待/可靠通知、调查起点/来源、最终回复/结案，均晚于基线和 reception。整份文件支持本批既有开发库重跑和补齐已知增量结构，执行前提和兼容范围详见[数据库入口](database-20261111.md)。历史记录：2026-11-09 已在 MySQL 5.7 开发库执行相关增量并保存变更前结构快照；本次代码简化未执行共享业务库DDL或删除业务记录。

MySQL 5.7 不执行 CHECK，脚本提供 INSERT/UPDATE 触发器兜底；整份文件的版本要求及验证范围以数据库入口为准。Kingbase 使用 PostgreSQL 兼容方言的 CHECK。DDL 回滚先停止应用、导出审计与待投递任务，再按 owner 审核方案处理，不能删除真实申请或评价。

等待API、调查API和调查Worker随应用运行，无独立功能开关。2026-11-11 起删除全局等待扫描，当前会话通过HTTP查询、访客操作或实时订阅按需推进；无人访问时不主动推进。通知Worker只投递已登记任务，每5秒恢复已启用通道，全部通道关闭时不访问数据库；调查Worker仍每10秒运行。

| 配置                                           | 缺省与作用                                       |
| ---------------------------------------------- | ------------------------------------------------ |
| `CUSTOMER_SERVICE_NOTIFICATIONS_ENABLED`       | false；通知发送开关，必须同时配置受控 endpoint   |
| `CUSTOMER_SERVICE_NOTIFICATIONS_ENDPOINT`      | 空；可信 INTERNAL/EMAIL 网关地址，不接收访客 URL |
| `CUSTOMER_SERVICE_NOTIFICATIONS_AUTHORIZATION` | 空；仅服务端请求头，不入事件/日志/访客投影       |

`customer-service.calendar` 默认 `Asia/Shanghai`、工作日 10:00–17:00，支持日期覆盖；配置格式、范围和覆盖优先级见 `application.yml`。首次推进时按服务端申请时间和当前日历冻结 UTC 截止时间及日历版本，不接受客户端时间，已冻结期限不随配置改变。通知 Worker 每批最多 10 条、租约 30 秒、网络请求最多 5 秒、自动投递最多 5 次，失败进入 DEAD 后由已授权客服重试。节点重启依靠持久状态及租约恢复。

## 等待 HTTP

沿用真实会话关系授权；不接受自报 tenant、org、principal、时间或客服组。

| 方法与路径（前缀 `/api/customer-service`）                                       | 行为                                                                                                    |
| -------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| `GET /conversations/{id}/waiting`                                                | 返回 `state,version,closesAt,nextOpensAt,ticketId,deliveries,contactCollected`；不公开邮箱              |
| `POST /conversations/{id}/waiting`                                               | `{expectedVersion,action,email}`；CONTINUE 继续等待，CONTACT 仅保存邮箱，EMAIL 取消在线等待并建跟进工单 |
| `GET /agents/followups?topOrgId=...&assistantConfigId=...`                       | 实时核验路由组成员，最多返回 100 条 NEED_EMAIL/FOLLOW_UP 摘要；不返回邮箱或正文                         |
| `GET /agents/conversations/{id}/waiting?topOrgId=...&episodeId=...`              | 按原周期读取状态；不指定 episodeId 时读取最新周期                                                       |
| `POST /agents/conversations/{id}/notifications/retry?topOrgId=...&episodeId=...` | 实时核验组成员后恢复所选周期 DEAD 通知；SENT 不重置                                                     |

按需推进时，达到T+5登记唯一内部提醒，达到T+10进入BUSY；继续等待为CONTINUING。首次读取已超过十分钟时直接补算BUSY，只登记一次提醒；首次读取已闭班时直接收敛为NEED_EMAIL，不补发过期提醒。闭班或非营业申请在推进时取消未接单周期并恢复AI。无人访问时不保证截止时刻主动通知或关闭，重新访问时恢复；客服先接单成功则保留接待。有效邮箱才建唯一工单及EMAIL Outbox；无邮箱不自动建工单。已接单周期不被闭班或访客邮件选择取消。版本冲突返回409，恢复后再选择。

工作台“保存邮箱并继续等待”使用 CONTACT，不创建工单或取消接待；相同邮箱重放不会重复写入。客服选择跟进列表项后固定 episodeId，同会话新增接待也不会把原工单的状态查询或重试落到新周期。查询在应用与持久层都核验租户、组织、会话、助手归属。

通知网关接收 `eventKey,kind,scope,episodeId,ticketId,traceId`；仅 EMAIL 携带邮箱。`Idempotency-Key` 与 eventKey 一致，下游必须持久去重。成功回执须为 2xx JSON `{"accepted":true,"eventKey":"同一个事件键"}`；空 2xx、异键、拒绝或网络错误都不能标记 SENT。SENT 仅表示网关受理，不表示送达或已读。发送与确认故障逐条隔离，过期租约可接管。

## 调查 HTTP

前缀 `/api/customer-service/conversations/{conversation}/survey`；所有路径先通过现有 Conversation 授权 Adapter 得到完整可信范围。

| 方法与后缀                        | 行为                                                                              |
| --------------------------------- | --------------------------------------------------------------------------------- |
| `GET`                             | `{data:{survey:null或最小卡片}}`；不创建调查、不确认展示，OPEN 超期收敛为 EXPIRED |
| `POST /{surveyId}/receipt`        | 页面渲染后显式确认 offered；重复回执幂等                                          |
| `POST /{surveyId}/answer`         | `{expectedVersion,answer}`；SATISFIED 或 DISSATISFIED                             |
| `POST /{surveyId}/handoff-choice` | `{expectedVersion,choice}`；OPTED_IN 或 DECLINED，仅不满意后可选                  |

最小卡片仅含 `surveyId,status,handoffChoice,version,expiresAt`。满意显示致谢；不满意仅记录评价，然后再次询问人工；拒绝只记评价，同意才在共享事务中创建客服周期、转移 HUMAN 并消费调查事件。来源固定为 `SURVEY_DISSATISFIED_OPT_IN`，原因 `USER_DISSATISFIED`，保留 surveyId、真实 turnId、触发主体及 traceId。一调查最多一周期，重复同参安全重放；Outbox 或 owner 转移失败全部回滚。未知字段/非法 JSON 为 400、越权 403、状态冲突 409、依赖不可用 503；响应 no-store。

## 人工时间线

2026-11-11 起人工实时统一到同源WebSocket。旧访客 `GET /conversations/{id}/events` 和客服 `GET /agents/conversations/{id}/events` 两个SSE接口及独立资源池已删除，返回404。登录JWT通过认证首帧传输，具体字段和订阅归属见[routing-realtime.md](routing-realtime.md)。AI生成原流式协议继续使用。

WebSocket快照的timeline与消息查询Page一致，含messages、本人receipt、peerReceipt，成功发送后推进连接内持久seq游标。每秒读取本地成员与当前认领，单页100；用户中心账号/组织每连接60秒复核，失败关闭，JWT到期独立关闭；禁用生效窗口包括调度及请求耗时。本机最多64连接、4个调度线程，停机/授权失败/发送失败释放配额，读取不自动推进READ。

工作台使用WebSocket，断线后按持久seq重连，取消重复人工SSE与每两秒HTTP轮询。消息按seq去重并显式回执，后台标签只发DELIVERED，前台发READ。切会话、卸载终止旧连接，迟到调查/消息/客服历史不覆盖新会话。手动接单不要求上线或在线心跳；在线状态仅用于展示和接收改派。

## 验证与剩余发布条件

最新证据见 `.codex/qa/customer-service-completion-20261109/`：真实 admin、模型和 MySQL 5.7 调查闭环 5 项通过；隔离 H2、模拟身份/目录、真实 Tomcat Controller/事务/SSE 的双浏览器接待 9 项通过。后者关闭两秒轮询补偿验证 SSE 消息到达，不模拟接单、消息、等待或跟进 API 响应。两类浏览器页面异常均为 0，390px 截图无横向溢出。定向 JDBC、MVC、Worker 和本机 HTTP 网关测试分别保留在 followup/reception 证据目录；Kingbase 和多节点未运行。

发布环境仍需真实通知网关、Kingbase/多节点故障演练和真实客服账号验收。开发阶段以本项目唯一接待 owner 提供完整历史。知识纠错通知已按用户决定移出本期，由人工依据满意度更新知识库。
