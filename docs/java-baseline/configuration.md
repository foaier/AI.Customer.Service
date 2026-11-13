# 客服与AI调查配置契约

2026-11-10生效。AI智伴和人工接待在本应用内，部署必须包含本期全部迁移，不再用独立功能开关分阶段拼接流程。历史文档中的enabled/api-enabled说明以本文为准。

## 内置流程

接待、消息/回执/WebSocket、等待、最终回复与结案、在线/改派、系统故障任务、调查API、调查Worker、JSON/Prometheus指标随应用运行。建议卡在查询和确认时判断到期。等待期限在当前会话的HTTP查询、访客操作或实时订阅读取时补算，删除全局等待扫描；无人访问时不主动提醒或关闭申请。已登记通知由 `CustomerServiceNotificationWorker` 每5秒恢复投递，全部通知通道关闭时直接返回，不查数据库。AI文本生成继续使用原流式协议。删除以下配置及对应环境变量读取：

- reception.enabled、waiting.enabled/api-enabled、followup-response.enabled/api-enabled。
- availability.enabled、system-handoff.enabled、metrics.enabled。
- handoff-proposal.api-enabled/expiration-enabled。
- ai-resolution.survey.enabled/api-enabled、ai-resolution.idle-deadline.enabled。
- account-directory.mode及formal-agent-directory的两个enabled开关。

无需设置任何上述值为true。认证、营业时间、当前generation、状态机、版本与容量仍按各自规则核验。2026-11-11 起，已登录且关联接待组的客服可在容量内直接手动接单，不要求先上线或发送心跳；新客服默认OFFLINE，在线租约只用于展示和接收同事改派，旧页面心跳不能覆盖下线选择。未配路由仍拒绝客服准入。

## 保留参数

下表为部署级缺省，访客HTTP和模型不能覆盖。2026-11-11起，正式营业时间可通过`/api/customer-service/configuration/calendar?topOrgId=...`按组织持久设置，优先于部署日历，只影响新等待周期；完整前端字段见[接口文档](frontend-api.md)。下文的本机测试入口只临时覆盖测试实例日历，不修改正式持久设置。

| 配置（customer-service前缀）        | 默认值、单位与范围                      | 业务含义及缺省行为                                                                                                                                              |
| ----------------------------------- | --------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| availability.capacity               | 5个并发接待，整数1—100                  | 同租户/组织客服在所有助手/组下的并发上限；不自动上线                                                                                                            |
| calendar.version                    | cn-office-v1，非空且≤64字符             | 冻结期限的日历版本，变更规则须升级版本                                                                                                                          |
| calendar.zone                       | Asia/Shanghai，合法IANA时区             | 服务端解释营业日期及起止时间                                                                                                                                    |
| calendar.opens/closes               | 10:00/17:00，本地24小时制且开始早于结束 | 缺省工作日营业区间                                                                                                                                              |
| calendar.overrides                  | 空Map；ISO日期→布尔                     | 日期覆盖优先于周一至周五默认规则                                                                                                                                |
| handoff-proposal.proposal-ttl       | 10分钟；1分钟—24小时                    | 待确认AI建议卡有效期；查询时过滤过期卡，确认时拒绝创建接待周期并记录过期状态，不影响访客明确直接申请；无人点击的历史行可保留PENDING，须结合expires_at判断有效性 |
| notifications.enabled               | false，布尔                             | 唯一外部邮件/内部提醒发送开关；关闭不领取、不耗尝试次数，等待/建单/回复仍正常保存                                                                               |
| notifications.endpoint              | 空；HTTP(S)受控地址                     | 空地址禁止投递；连接2秒、请求5秒；网关须持久幂等支持eventKey并回执accepted                                                                                      |
| notifications.authorization         | 空；服务端认证头                        | 仅用于出站请求，不保存到业务任务、日志或响应                                                                                                                    |
| notifications.wecom-webhook         | 空；官方企微HTTPS群机器人发送地址       | 非空时INTERNAL优先发企微；仅notifications.enabled=true生效；邮件及最终回复仍需endpoint，企微回执必须为整数errcode=0                                             |
| handoff-proposal.outbox-enabled     | false，布尔                             | 可选向外部Rabbit消费者转发事件；本项目接待、聊天、WebSocket与调查不依赖它                                                                                       |
| handoff-proposal.outbox-batch-size  | 100条；1—500                            | 外部转发每轮领取上限                                                                                                                                            |
| handoff-proposal.outbox-interval-ms | 500毫秒；100—60000                      | 外部转发轮询间隔；未开启转发时不运行                                                                                                                            |

外部转发关闭时，事务内事件仍保留为审计及耗时事实，outbox_dead/outbox_over_5m返回0，避免将未启用的外发通道误报为故障。等待、系统任务和回复自身的失败/积压继续按数据库统计。

## 测试Profile与依赖

customer-service-test注册本机可视化测试配置API与独立通知探针沙箱；未加入默认active profiles。没有该Profile也会注册全部内置流程。接待组、成员关联及助手四类路由统一从设置页持久维护，不再读取部署级reception.routes；开发阶段不回填旧外部组配置。

用户中心唯一拥有账号、租户及组织启用关系，本项目客服上下文拥有四类业务组、成员关联、助手路由和申请快照。UserCenterAccountDirectoryAdapter保留只读账号/历史目录查询，不再作为正式业务分组主写方。本项目还拥有接单、接待、等待和工单回复；同项目不意味着可以取消限界上下文及事务端口。

路由设置由当前登录账号操作，服务端实时核验该账号属于认证租户且在请求指定组织启用；不配置部署级账号白名单或根据用户名推断角色。设置页先读取该组织用户中心现有分组，再读取所选组成员；用户中心仍是账号/分组owner，本项目只保存成员账号引用及展示名快照。目录响应只包含组ID、组名、账号ID和展示名，不返回邮箱、手机号或账号状态；保存配置时再次核验每位成员的租户和组织启用关系。该设置权限不自动授予接单资格，接单仍由本项目业务组成员关系和接待状态决定。

## 持久分组及实时连接

2026-11-11 起，对应数据库的全部迭代 SQL 统一使用`data/sql/{mysql|kingbase}/ai_20261111_customer_service/all_in_one.sql`，执行范围及缺失来源段的核对方式见[数据库入口](database-20261111.md)。完成路由结构后，打开`/ai_chat/customer-service/debug.html`选择组织和助手，进入“客服分组与助手路由”，核验并关联已有账号，为展商、观众、媒体、其他四类咨询选择接待组，再保存。首次只读显示四个稳定默认组，不自动授予接单资格；配置按组织版本整体CAS提交。修改映射只影响新申请，旧组仍可处理历史已分配申请；撤销成员关联则即时停止其权限。

上下文分类使用统一意图模型，读取最近对话及当前真实输入，识别咨询主题而非用户身份。置信度低于0.75、非法输出、模型失败或上下文缺失降为OTHER；设置页预览显示识别来源及已保存目标组。模型不能决定实际组ID或授予成员资格。

人工状态、队列、等待、调查与消息统一使用同源WebSocket；两个旧人工SSE接口和独立资源池已删除，AI生成继续沿用原流式协议。HTTPS入口自动选择WSS，接单、发送和回执仍为幂等HTTP命令。2026-11-11 起直接复用登录JWT；Bearer仅通过认证首帧传输，不进入URL或数据库。连接有效期沿用JWT，到期关闭，取消固定60秒重连认证。每节点64连接、4个扫描线程，未认证连接5秒关闭；每秒只查本地成员、认领与持久事实，仅推送变化及15秒心跳。用户中心账号/组织在首帧核验，之后每连接固定60秒复核一次，失败关闭；账号禁用生效窗口为最多一个复核周期加调度及请求耗时，本地成员撤销和改派在下一次扫描生效。此周期为服务端固定值，无额外配置或全局身份缓存；HTTP命令仍逐次核验账号与组权限。浏览器35秒无帧主动重连，退避1—30秒，携带最后已处理seq补齐。在线状态心跳只服务展示和接收改派，不限制手动接单；实现结论见[当日简化记录](design-simplification-review-20261111.md)。

本轮本地浏览器验证了真实WS传输及断线补齐，身份、会话关系和语义模型为明确替身；TLS终止/WSS升级、真实模型分类及目标数据库迁移需要在对应环境验收。仅浏览器协议选择测试不能证明TLS部署已通过。完整契约见[routing-realtime.md](routing-realtime.md)。

历史目录联调Controller和X-CS-Mock-\*管理授权仅在customer-service-directory-test Profile下注册。该Profile不随customer-service-test启用，不替代正式接单权限，不应在部署环境激活。

启动新版应用前先执行当日合并 SQL；合并文件支持已有开发库整份重复执行并补齐缺失结构，生产环境仍需按变更窗口核对。外部通知与 Rabbit 转发保持缺省关闭。

## 营业日历与企微通知可视化联调

入口：`/test/customer-service-configuration.html`，应用带`/ai_chat`上下文时使用`/ai_chat/test/customer-service-configuration.html`；现有AI对话与转人工联调台提供链接。沿用用户本轮“可视化测试配置”范围，采用临时配置，重启恢复；正式管理台持久化及多节点分发不在本轮范围。

数据owner与生命周期：正式组织日历由客服上下文持久维护，未保存时使用部署缺省；临时测试日历是当前进程的不可变覆盖快照，不建表、不写正式日历、不复制用户中心数据。实例只用于本机联调，临时覆盖影响此实例所有新初始化等待单，不按租户单独持久化。现有等待单的UTC期限与日历版本由客服owner唯一持久化，日历改变后不重算。通知探针只记录固定测试组织/会话和最多100条安全模拟记录、100个进程内受理键，重启清空；它不消费或确认业务Outbox。

页面能力：营业时区、开闭门时间、节假日/调休日期覆盖、指定带时区时间的草稿预览、T+5/T+10及下班/下一班次解释、版本化应用、刷新恢复、并发冲突和恢复部署默认；企微模板预览、成功/拒绝模拟、同一事件最多五次手工探针与幂等受理。周历默认周一至周五，当前不新增任意周历编辑。日期覆盖最多1000项，区间支持HH:mm或HH:mm:ss且必须同日开门早于闭门；重叠日期拒绝。

API根路径：`/api/customer-service/test-configuration`。

| 操作                    | 输入/结果及副作用                                                                                                                                  |
| ----------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| GET根路径               | 返回revision、calendar、notifications安全记录；无部署端点或密钥                                                                                    |
| POST /preview           | `{calendar,at}`，at是UTC/带偏移的Instant；只算草稿，不保存、不改时钟                                                                               |
| PUT根路径               | `{revision,calendar,notifications:{active,outcome}}`；outcome仅SUCCESS/REJECT；全部校验后原子应用，改营业规则必须更新日历版本；陈旧revision返回409 |
| POST /reset             | `{revision}`；恢复部署日历、停用探针并推进revision；保留本轮记录                                                                                   |
| POST /notification-test | `{eventKey,attempt}`；key必须probe:前缀、最长100字符；attempt为1—5；不创建工单，不发真实消息                                                       |

仅已认证、本机直接回环连接且携带`X-CS-Test-Configuration: local-workbench`的请求可用；拒绝代理转发头、非同源Origin、匿名和非回环来源。HTTP成功及拒绝均no-store；未知字段即使全局Jackson宽松也返回400。此接口不是组织管理授权替代品，不得在生产Profile激活。

真实企微使用环境变量`CUSTOMER_SERVICE_NOTIFICATIONS_WECOM_WEBHOOK`，仅接受`https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=...`官方地址，不跟随重定向。密钥不在可视化测试页输入、回显、日志或业务任务中保存。部署总开关仍默认关闭；只配企微时Worker只领取INTERNAL，EMAIL/EMAIL_REPLY保持PENDING且不消耗次数。正式流程继续使用现有持久租约、五次重试、DEAD和人工恢复，发送在事务外。企微本身不提供eventKey去重，网络超时后的至少一次重投可能重复群提醒，页面沙箱幂等不能证明企微具备此能力；SENT仅代表企微/网关受理。

本轮验证没有访问真实企微群、发送真实邮件或修改开发数据库，不能将回环HTTP协议测试与本地模拟记录视为真实企微送达验收。
