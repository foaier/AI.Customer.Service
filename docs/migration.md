# Java 智能客服迁移说明

迁移来源：`D:\WorkCode\2_Backend\byt-cloud-agent`，目标：`D:\MyCode\AI.Customer.Service`。原始规范以 `specs/013-human-customer-service/` 顶层正式文档为依据，包含 2026-11-11 的取消沉默调查和直接手动接单规则。原仓库仅执行读取和状态检查，不改代码、不改库。

## 分层与领域边界

保留原七层拆分，Java 模块 `byt-cloud-agent-{common,api,domain,infrastructure,core,service,web}` 对应 .NET 同名职责项目。Spring 装配转为 ASP.NET Core DI；MyBatis-Plus 仓储转为 EF Core LINQ 和实体映射；Java Worker 转为 `BackgroundService`。AI Agent 执行层使用 **Microsoft Agent Framework 1.28.0**，由 `ChatClientAgent` 负责文本生成、流式响应和自动工具循环，分类也通过框架运行。

依赖方向：Domain → Common；Api → Domain/Common；Infrastructure → Domain/Common；Core → Domain/Common；Service → Api/Domain/Core/Common；Web 负责最外层装配。业务层只调用领域 Repository，不引用 DbContext 或数据库实体配置。

| 原业务边界                                 | .NET 实现                                                      | 唯一写入 owner         |
| ------------------------------------------ | -------------------------------------------------------------- | ---------------------- |
| Conversation、generation、轮次、消息和水位 | `ConversationRuntime`、`MessagingService`、会话 Repository     | Conversation           |
| 接待申请、建议卡、认领、首响、结束、取消   | `HandoffService`、`ReceptionService`、`ReceptionRules`         | CustomerService        |
| 四类业务组、账号引用、助手路由             | `ConfigurationService`、配置 Repository                        | CustomerService        |
| 组织在线展示及改派容量                     | `AvailabilityService`、`ReceptionService`                      | CustomerService        |
| 等待、邮箱、留言、最终回复                 | `WaitingService`、`FollowupService`                            | CustomerService        |
| AI 结束候选及满意度                        | `SurveyService`、`SurveyContentPolicy`、`SurveyRecoveryWorker` | Conversation           |
| AI Agent、工具调用及语义分类               | `AgentFrameworkChatModel`、`AgentProposalTools`                | 仅计算候选，不直接写库 |
| 待投递任务及租约                           | `CommandJournal`、`ReliableTaskProcessor`                      | CustomerService        |
| 账号/组织启用、外部人员组                  | `IAccountDirectory`、`UserCenterAdapter`                       | 外部用户中心，本地只读 |
| 可信助手组织/大会归属                      | `IAssistantRegistry`、`ConfiguredAssistantRegistry`            | 服务端助手注册表       |

两个本地业务 owner 共享数据库和短事务，但通过明确端口调用。组织写入门闩使用 ORM 原子更新取得写锁，将跨会话容量、撤权、回复权、回执和待投递事实放在同一事务内。网络分类与用户中心核验在事务外；事务内再次核验本地权限和期望版本。没有通用 SQL 执行入口或按数据库拼接业务查询。

## 保留的关键规则

- 同一个 conversation贯穿 AI、人工、调查；每个新 AI 轮次和回复权切换推进 epoch。旧正文、建议、未决调查和故障任务都受到来源轮次围栏保护。
- 人工状态为 `REQUESTED → CLAIMED → ACTIVE → RESOLVED`，`REQUESTED → CANCELLED`。首条客服消息才进入 ACTIVE。原访客仅能取消未认领请求，原认领者才能结束。
- 手动接单只要求登录、外部组织启用、本地组权限和容量；不要求上线。改派目标需在线租约有效、同组、容量充足；成功后旧客服立即失去正文与写权限。
- EXHIBITOR / VISITOR / MEDIA / OTHER 是主题分类，不能据此推导访客身份或权限；分类置信度低于 0.75、结果非法或依赖失败时归 OTHER。
- `proposeHandoff` 只产生建议。访客明确原话或按钮才直接申请；模型 `requestHandoff` 不能将否定、引用和假设升级为用户授权。确认卡同键回执不可覆写。
- 等待按当前会话访问补算，T+5 分钟提醒、T+10 分钟繁忙、闭班优先；无全库等待扫描。原周期冻结日历，已接单不因下班被取消。
- 选择 EMAIL 原子取消在线申请并恢复 AI，创建无伪造访客正文的 EMAIL 工单；FORM 为访客主动留言，原组客服认领后才可读正文和回复。
- 邮件正文和待发任务同事务保存；网关明确 `accepted=true` 才记 SENT，SENT 不代表送达或已读。最终回复显式结案，不允许从客户端指定收件人。
- 满意度只依据完整成功历史、真实业务问题和明确用户结束证据；普通“谢谢”、模型高置信候选和任意长度沉默都不创建调查。曾申请人工的会话始终排除纯 AI 调查。一会话最多一份调查。
- `proposeResolution(confidence)` 达到 0.85 才保存模型候选。候选固定真实轮次，不复制正文，同轮同参数幂等、异参数拒绝覆写；候选本身不能替代用户结束证据。
- 不满意先进入独立 PENDING 选择，只有访客 OPTED_IN 才申请人工。新轮次后旧未决选择失效；历史已提交回执仍可恢复。

## 协议和可靠恢复

AI 文本采用 SSE，人工实时只提供 `/api/customer-service/realtime/ws`。WebSocket 首帧携带 Authorization 和固定订阅范围；URL 禁止令牌和参数，未知字段拒绝。首帧 5 秒、进程最多 64 连接、每秒读取持久事实、15 秒心跳、60 秒复核外部账号；本地成员和认领权限随快照检查。

人工消息使用单会话递增 seq、稳定 clientMsgId 和正文摘要；回执为单调 delivered/read 水位。客服改派和访客重连按持久事实恢复。HTTP 请求拒绝未知身份字段，tenant/account 从 JWT 解析，组织/大会由持久会话和可信助手推导。

Worker 每 5 秒有界恢复可投递任务；租约 30 秒，最多 5 次，失败退避 5/10/20/40 秒后 DEAD。禁用通道不领取、不耗尝试；EMAIL/INTERNAL 死信可由原组客服恢复，最终回复和系统故障各有独立重试入口。重试保留 eventKey，外部网关必须持久幂等。可选官方企微群机器人仅承诺其回执，官方 webhook 本身不提供按 eventKey 严格去重，重试可能出现重复提醒。

调查 Worker 每 10 秒恢复未评估成功轮次，使用持久评估游标；它不判断用户沉默，不扫描等待期限。跨异步调用显式传递 sessionId、turnId、traceId、tenantId、topOrgId、bventId。

## 本次独立化的适配边界

独立项目使用 Microsoft Agent Framework 的官方 OpenAI provider 和部署维护的可信助手注册表，保留客服所需的最小文本会话能力。MAF 实际执行注册的 `AIFunction` 并将工具结果交回模型继续生成，原手写模型 HTTP/SSE 解析器已移除。每轮独立 AgentSession 从同范围的真实成功历史构建，工具仅保存本轮候选，业务层成功围栏后再提交；失败、取消或旧代次不会直接提交建议。[框架实现说明](agent-framework.md) 包含运行路径及验证依据。

Java 整个平台的知识库管理、RAG 检索、地图、文件上传、语音、第三方模型专用 SDK、全平台助手管理和 Consul 等不属于这次客服剥离；需要知识检索时应在模型端口前加入正式知识领域 Adapter，不能把当前纯模型回答描述为已接入原知识库。

原项目的本机假身份、模拟通知和旧测试控制台不进入生产入口；现在通过独立测试宿主验证。保留业务静态页面并删除无对应服务的旧调试链接。原 `/ai_chat` 上下文由部署代理决定，新项目默认运行根路径。

数据库为独立新结构，并非原 Java 表结构的直接替换：枚举在数据库存数值、在 API 使用字符串，路由配置作为同 owner 的 JSON 聚合保存。Kingbase 通过 Npgsql 使用 PostgreSQL 兼容模式，尚需目标版本实库验收；MySQL 要求 8.0.16+ 才能强制 CHECK。SQLite 用于本机及自动回归，不将其并发性能结论外推到多节点。

当前导出为分页 JSON，与 Java 正式接口一致，每页最多 100 个会话，人工消息每会话最多 10000 条并给截断标记；AI 轮次最多 10000 条，不提供 Excel。指标按完整租户/组织/助手聚合最近 24 小时的来源、原因、状态、接单/首响/解决耗时，当前积压独立于历史窗口；JSON 和 Prometheus 共用一致性快照，全部声明 gauge，不使用有截断的任务样本冒充全量计数。

改派的原客服、目标、操作者、请求键、前后版本、trace 和时间保存在不可变 EVENT 载荷及登记时间中；即使外发关闭仍保留本地审计。可靠任务保存累计尝试次数、人工恢复次数及最近操作者/时间，人工恢复只重置当前预算，不覆盖首次来源。

## 来源审计

`docs/source-inventory.json` 保存 Java 文件路径、SHA-256 和来源提交。执行 `node scripts/source-inventory.mjs` 可重新只读生成。`docs/java-baseline` 为正式规范快照，`data/sql/reference` 为原库参考 DDL；两者保留原始格式，不作为新代码格式化或自动执行对象。Java 源仓库最终状态应与开始时一样保持干净。
