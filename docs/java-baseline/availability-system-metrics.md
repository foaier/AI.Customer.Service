# 在线接待、改派、系统故障和对账

## 归属和状态

用户中心唯一主写账号、租户和组织启用；本项目Customer Service上下文维护业务组、成员关联、组织级运行状态、认领、改派审计、可靠系统任务和接待事件。Conversation持有回复权、消息、调查，跨上下文只通过领域/应用端口访问。

`cs_agent_availability`以tenant/topOrg/agent唯一。2026-11-11 起，手动接单只要求有效登录、组权限及剩余容量，默认OFFLINE、AWAY或心跳过期均不阻止本人主动接单。首次接单或读取缺少运行行时幂等建立默认OFFLINE/version=0行后持锁，避免不同会话首次同时接单超额；不会自动上线、续租或推进代次。容量默认5，合法1—100，覆盖同组织所有助手/组，客户端不能覆盖身份、租约和容量。锁序generation→episode→目标客服运行行，并发新单依运行行串行化后实时计数。运行表仍是容量锁和在线展示的必需结构。

明确选择ONLINE/AWAY/OFFLINE会递增服务端version；页面每15秒以该version调用独立heartbeat，只有同代次、仍在线且未过期才可续约，不推进version。服务端45秒过期；AWAY/OFFLINE/失联仅停止接收同事改派，已有接待和本人手动接单仍可继续。另一标签页改变状态后旧页心跳返回409并停止续约；过期或版本0需明确选择在线才恢复展示/接收改派。页面恢复不自动上线。

改派只有当前认领客服可执行，目标必须经实时用户中心核验、同组织同服务器路由组、ONLINE且有容量。保留原episode/HUMAN，将CLAIMED或ACTIVE转为CLAIMED等待接手客服首响；`cs_handoff_claim`保存当前受理人，`cs_reception_transfer`不可变保存原/目标/操作者/请求键/版本/trace/时间。改派事件同事务入Outbox；失败整体回滚。同键同参重放，异参拒绝。旧客服即使持旧Snapshot也无法读消息、AI历史、发消息、提交回执或结束；WebSocket每次投影重新读取当前认领，页面清除旧正文。响应丢失锁目标并复用原请求键/版本确认，不能自动改派给另一人。

## 可信系统策略

AutoMode最终失败（含完成自动恢复后仍失败）从调用开始时冻结的可信归属、原访客、turn、epoch、trace登记`cs_system_handoff`；安全护栏异常使用固定安全原因。用户文本/模型Tool/HTTP均无系统来源创建权限。没有完整登录/轮次/trace/epoch的请求不创建系统申请；主动取消/线程中断不当作系统故障。

同完整范围/轮次/epoch/原因的SHA256为任务键。PENDING每5秒最多20条，消费锁序generation→任务，短事务内创建SYSTEM_POLICY周期、命令回执、Outbox、HUMAN与COMPLETED引用；不执行网络调用，崩溃整体回滚，其他节点可恢复。新消息或已有人工取代epoch时只推进SUPERSEDED。失败预算五次，退避每次5秒乘次数；DEAD需正式客服重试，保留首次来源、trace及人工重试主体/时间/累计次数。登记本身的数据库故障会返回错误，不声称已经转接。访客页面轮询持久owner确认系统申请，错误文案本身不构成已转人工。

## HTTP

均在`/api/customer-service/agents`，正式Bearer认证，每次实时组授权，响应no-store。

| 方法     | 路径                                                                    | 内容                                                                               |
| -------- | ----------------------------------------------------------------------- | ---------------------------------------------------------------------------------- |
| GET/POST | `/availability?topOrgId=&assistantConfigId=`                            | GET不改变在线选择，缺行时初始化默认离线锁行；POST仅`{status: ONLINE/AWAY/OFFLINE}` |
| POST     | `/availability/heartbeat?topOrgId=&assistantConfigId=`                  | 仅`{version: 正数}`；不创建状态，不允许过期或旧代次复活                            |
| GET      | `/transfer-candidates?topOrgId=&assistantConfigId=&afterCursor=`        | 同组候选accountId/displayName分页，无联系方式                                      |
| POST     | `/conversations/{id}/transfer?topOrgId=`                                | requestId、expectedVersion、targetAccountId                                        |
| GET      | `/system-handoffs?topOrgId=&assistantConfigId=`                         | 最多100条本助手死信最小引用                                                        |
| POST     | `/conversations/{id}/system-handoff/retry?topOrgId=&assistantConfigId=` | 空JSON；仅恢复已有DEAD预算，无创建入口                                             |
| GET      | `/metrics?topOrgId=&assistantConfigId=`                                 | 最近24小时历史及当前积压聚合                                                       |
| GET      | `/metrics/prometheus?topOrgId=&assistantConfigId=`                      | 同一聚合的Prometheus文本，仍须正式身份                                             |

## 指标与部署

历史按申请requested_at最近24小时统计来源/原因/状态；接单、首响、解决提供秒数总和与样本数。首响由客服实际消息成功保存时同事务写`handoff.first_response`，改派后再次首响不重复最初耗时。缺旧首响不计样本、不回填零秒。调查仅通过Conversation owner只读端口按opened_at窗口统计OPEN/SATISFIED/DISSATISFIED/EXPIRED，未知或未回答不算满意。

当前积压独立于历史窗口：超过五分钟待接单、通知/最终回复/系统任务/Outbox死信、超五分钟系统任务及Outbox。JSON和Prometheus无正文、邮箱、Token、会话/账号标签；所有导出是gauge，窗口数量不能冒充单调counter。采集端按固定可信组织/助手配置目标并附目标标签，使用secret文件提供正式账号凭据，不将Token写入仓库。告警样例见`data/monitoring/customer-service.rules.yml`。样例不是已部署/已送达告警证据。

2026-11-11 起，统一使用目标方言的`data/sql/<dialect>/ai_20261111_customer_service/all_in_one.sql`，其中 availability 来源段先于 availability_lease，system 来源段晚于 survey_runtime；仅执行尚未部署的完整来源段，详见[数据库入口](database-20261111.md)。不得重跑基线或已部署增量。历史记录：开发MySQL5.7已完成在线、租约和系统三份迁移并通过回滚探针，快照在本机.codex下；本次仅整理入口。在线、系统任务和指标随应用运行，不再分别配置enabled。容量配置`customer-service.availability.capacity`默认5、范围1—100。下线或暂离不自动结束已有接待。外部事件转发关闭时Outbox仅保留本地事实，不统计为对外投递积压。正式部署告警、Kingbase和多节点仍须目标环境验证。

## 本轮证据与实际限制

2026-11-10收尾：Java定向84+87=171项、Node17项通过；生产reactor package成功并重启本机8081。隔离外部身份/用户中心、真实Tomcat/H2/JDBC/SSE/Worker：在线/改派11项、系统恢复/指标6项、原业务邮件闭环9项，均通过且页面异常0。45秒失联和跨标签页真实定时心跳已验证；十分钟idle已在真实admin/模型/MySQL场景实际等待603秒通过，不重复推断满意。

正式用户中心接口复查返回503，不能视为组授权通过；之后开发MySQL连接被拒绝且Apollo配置拉取失败，业务配置页面暂不可用。此为目标环境联调缺口，未修改认证或伪造成功。全仓testCompile仍有既有TencentAvatarProperties/TencentStsProperties及DTO缺类阻断。真实客服双账号、真实邮件投递、Kingbase、多节点及正式告警部署保持未验证。
