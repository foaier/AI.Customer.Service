# 2026-11-11 客服设计简化记录

本次按“客服复用登录状态即可使用”的方向，检查人工实时通道、授权调用链、接单前置条件、调度任务及本轮SQL。实时连接直接复用登录JWT；用户确认将检索出的三项全部简化，以下记录最终实现。

## 三项简化结果

| 原问题                            | 最终行为                                                                                                                                                                                                     | 验证                                                                                                                                    |
| --------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------- |
| 人工消息同时保留 SSE 和 WebSocket | 删除两个旧 `/events` 人工 SSE 路由、`CustomerServiceReceptionStream` 及其独立64连接/4线程资源池。人工实时统一为现有WebSocket；AI生成原流式协议继续使用                                                       | 两个旧路由真实MVC返回404且无异步/业务读取；WebSocket登录、游标、撤权和JWT边界回归                                                       |
| 每秒扫描都重新请求用户中心        | 首帧核验登录JWT及用户中心账号/组织，每秒快照仅查询本地成员、当前认领和持久事实；每连接固定60秒复核账号，失败关闭，不强制重连，不引入全局身份缓存。两个连接的稳定远程身份查询由约每秒4次降至约每分钟4次       | 连续扫描不重复调用用户中心；本地撤销拒绝下一次读取；可控时钟验证59秒不刷新、60秒只刷新一次、下次失败先关闭且不再读快照；JWT到期优先关闭 |
| 手动接单还依赖单独上线和持续心跳  | 手动接单改用 `requireClaimCapacity`，登录且有组权限即可在容量内认领，默认OFFLINE、AWAY、心跳过期均不额外阻止。首次操作幂等建立默认离线锁行后串行计数，不自动上线或延长租约；在线状态只用于展示及接收同事改派 | 真实HTTP/JDBC验证首次零运行行直接接单，三种状态均可接单，无锁行的跨会话并发只能占用最后一个容量；前端直接接单不隐式发上线请求           |

用户中心禁用生效窗口为最多一个60秒复核周期加调度及请求耗时，不宣称即时撤权；HTTP命令仍逐次核验用户中心，本地成员撤销/改派在下一次扫描生效。改派目标仍须明确在线且有容量，避免把接待被动交给已离开页面的客服。

相关实现：

- [精简后的HTTP接口](../../byt-cloud-agent-web/src/main/java/com/byt/cloud/agent/web/controller/CustomerServiceReceptionController.java)、[WebSocket适配器](../../byt-cloud-agent-web/src/main/java/com/byt/cloud/agent/web/customerservice/CustomerServiceRealtimeWebSocketHandler.java)。
- [实时查询](../../byt-cloud-agent-service/src/main/java/com/byt/cloud/agent/service/business/customerservice/CustomerServiceRealtimeQuery.java)、[客服授权](../../byt-cloud-agent-service/src/main/java/com/byt/cloud/agent/service/business/customerservice/CustomerServiceAgentAccessService.java)、[用户中心核验](../../byt-cloud-agent-service/src/main/java/com/byt/cloud/agent/service/business/customerservice/CustomerServiceAccountIdentity.java)。
- [接单编排](../../byt-cloud-agent-service/src/main/java/com/byt/cloud/agent/service/business/customerservice/CustomerServiceReceptionService.java)、[在线状态编排](../../byt-cloud-agent-service/src/main/java/com/byt/cloud/agent/service/business/customerservice/CustomerServiceAvailabilityService.java)、[在线与容量规则](../../byt-cloud-agent-domain/src/main/java/com/byt/cloud/agent/domain/customerservice/model/CustomerServiceAvailability.java)。

## 当前仍有的调度任务

以下为源码中的注册规则，实际运行取决于应用是否部署此版本、Spring 调度是否启用及对应配置；不是对当前运行进程的观测。

| 任务                                          | 默认间隔         | 用途与启动条件                                                             |
| --------------------------------------------- | ---------------- | -------------------------------------------------------------------------- |
| `CustomerServiceNotificationWorker`           | 5 秒             | 只恢复已登记通知的可靠投递；所有通知通道关闭时不查数据库                   |
| `CustomerServiceFollowupResponseWorker`       | 5 秒             | 最终邮件回复的租约、重试与恢复；通知端口不支持 EMAIL_REPLY 时直接返回      |
| `CustomerServiceSystemHandoffService.runOnce` | 5 秒             | 恢复可信系统故障的待转人工任务                                             |
| `AiSurveyRuntimeWorker`                       | 10 秒            | 扫描调查资格并恢复调查创建/过期                                            |
| `CustomerServiceHandoffOutboxWorker`          | 500 毫秒，可配置 | 可选 Rabbit 对外事件转发；仅 `outbox-enabled=true` 注册，默认关闭          |
| 人工 WebSocket                                | 每连接约 1 秒    | 有连接才读取本地权限/持久事实；每60秒复核用户中心账号，旧人工SSE调度已删除 |

全仓另发现 `JiMengQueueManager.cleanupStaleTasks` 每 30 秒调用空方法；它属于图片生成模块，可作为后续低优先级清理项，本次未修改。

## 有明确用途的结构

- 组成员、租户/组织、会话归属及当前认领关系防止越组读取和写入，应继续复用现有权限校验。
- generation 围栏处理迟到 AI 回复，接待状态机及幂等回执处理重复命令和并发接单；这些有独立业务用途。
- 等待状态在访问时恢复；通知、系统故障和最终回复任务需要进程重启后的恢复，持久任务、有限重试及人工恢复有明确用途。
- Rabbit 仅为可选外部转发，本项目接单和聊天不必开启它。`ai_resolution_survey_outbox` 也存在实际消费确认：`AiResolutionSurveyRepositoryImpl` 写入 PENDING，`ConversationSurveyRuntimeRepositoryImpl.consumed` 更新 DELIVERED；不能判定为无人读取的废表。

## SQL 整理边界

本轮全部客服及关联AI流程的9份来源脚本，按依赖顺序分别汇总为MySQL、Kingbase各一份；原分散入口移除，来源标记保留。移除沉默调查后的当前两种方言各包含28张表，支持本批既有开发库整份重跑和补已知缺失结构。部署、已部分执行的处理方式及验证限制见[20261111数据库入口](database-20261111.md)。

历史SQL整理回归149项通过。本次三项简化后，32个Java测试类共167项通过，前端工作台15项通过；包含新增及调整的接口移除、账号刷新、成员撤销、无上线接单、并发容量和改派限制测试。生产reactor `clean package`通过，构建显式跳过测试，测试由独立javac/JUnit执行，未将跳过测试的构建计为测试通过。

已更新现行配置、接待API、在线规则和实时协议；没有新增表、字段或迁移。共享业务库未执行DDL或清理记录，现有服务未重启；部署须同步更新服务端和前端资源。H2和外部身份替身不代表真实MySQL/Kingbase、用户中心或TLS/WSS部署验收。

2026-11-11 后续：用户明确取消沉默后触发满意度调查，已移除空闲期限创建、500毫秒扫描及generation推进时的取消SQL。仅用户明确结束或确认没有后续问题时，才进入完整问答调查资格判断。

同日等待简化：删除每5秒遍历接待周期的SQL、候选扫描端口及专用扫描测试。当前会话的HTTP查询、访客操作和实时订阅复用原有状态机，按需补算提醒、忙碌及闭班；无人访问时不主动推进，客服先接单成功则保留接待。原Worker收敛并更名为通知投递Worker，通知全关闭时不访问数据库，保留启用通道的租约、有限重试和逐条故障隔离。此变更不补齐缺失表：开发库缺少`cs_handoff_route`仍需按数据库入口补齐，正常接单和路由设置依赖该结构。

等待简化验证：干净生产构建通过；73个相关Java测试类共329项通过，包含首次访问跨十分钟、首次访问已闭班、关闭通知零数据库访问、投递及结算故障隔离、接单与闭班竞争。261个Java文件和62个其他迭代文件格式检查通过，打包产物只含新的通知Worker，无旧等待扫描Worker。未启动或重启业务服务，未执行共享库迁移；H2和通知替身不代表真实通知网关或Kingbase验收。
