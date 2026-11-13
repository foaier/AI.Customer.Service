# 接待、消息与 Tool 执行契约

2026-11-09 用户明确指定本项目唯一主写接单、接待状态和聊天消息；2026-11-10进一步确认本项目维护业务分组及成员关联，用户中心继续唯一主写账号、租户和组织关系。本文替代旧方案中“外部系统拥有 claim/消息/业务组”的假设。不恢复旧cs_session协议，最新路由及实时契约见[routing-realtime.md](routing-realtime.md)。

## 执行范围与验证

当前分支连续实现，风险 STRICT。既有独立联调页的 UI 检查与真实浏览器验收继续开启。先补接待与消息 owner、迁移、授权及 HTTP/恢复协议，再启用 AI Tool 与卡片；测试覆盖并发接单、消息/命令幂等、状态及跨组织拒绝、消息回执、取消/结束恢复 AI、Tool 的用户授权校验和页面确认卡。

## owner 与可信边界

- Customer Service owner：既有 episode 作为接待聚合，原子决定 REQUESTED → CLAIMED → ACTIVE → RESOLVED，以及未接单的 CANCELLED。认领事实不可由客户端任意修改；请求使用 expectedVersion 和稳定 requestId。
- Conversation owner：人工模式访客/客服消息正文、递增序列、消息幂等和接收/已读水位；只引用已有 conversation 和 episode，不创建第二套会话。AI 原有消息继续由 ai_conversation_detail 持有，通过只读历史 API 提供接待前上下文。
- 用户中心：认证账号资料、租户和组织启用状态；每次客服读写重新核验，不复制账号主表。登录accountId必须与用户中心Info.accountId一致，再核验本地业务组关联；禁止用昵称、邮箱或userId猜测映射。
- 路由：客服owner持久维护四类业务组/成员及assistantConfigId分类映射；申请时冻结咨询类别、目标组和配置版本，模型只识别类别。未配置不可接单；请求参数不能替换可信组织及会话绑定。组织成员状态必须是ENABLED，映射改变保留旧单入口，移除成员立即撤权。
- 数据库同时以 tenant/topOrg/conversation 复合约束隔离；事务先锁 Conversation generation，再锁 episode，网络目录查询在事务前完成。

## 消息和恢复

HTTP 写入返回持久成功，不能等同对端收到。消息按 conversation 单调 seq，clientMsgId 同键同内容返回原消息，异内容冲突。接收/已读水位只能单调推进，READ 不得超过 DELIVERED 或已存在最大序列。双方按 afterSeq 拉取持久消息，重连可补齐；状态恢复始终读取数据库。事件/消息存储本身作为可重放投递日志，不依赖单进程内存通知。已有跨系统事件继续使用 Outbox。

## AI 转人工

- `proposeHandoff(reasonCode, confidence)`：AI 根据上下文建议时，只保存待确认 proposal，提交后通过 SSE `card` 发送稳定 proposalId；前端可恢复、确认或拒绝。确认前不得建 episode 或停止 AI。
- `requestHandoff()`：模型不能传入身份/组/授权；服务端必须再次检查本轮原始用户输入是否明确要求人工。校验通过才使用 VISITOR_DIRECT_REQUEST / USER_REQUESTED 直接申请，不再询问。
- 引用、否定、假设、询问功能或不完整上下文不能当作用户授权；无法确定时仅允许建议卡。
- 卡片推送是提交后的及时通知，持久 proposal 查询负责漏推/断线恢复；未获得真实接单事实时只能显示“申请已记录/等待接单”。

## 2026-11-09 恢复中断任务后的验证

本轮范围为接单、双方消息、回执、取消/结束，以及两个转人工 Tool 与前端确认卡。用户允许模拟客服身份，并允许 admin 验证真实聊天；模拟仅在测试源码和独立 H2 中替换外部身份来源，正式服务仍实时核验用户中心账号、组织与组成员，不增加 admin 绕过或本地成员主表。

| 验收项                                      | 实际证据                                                                                                                                                          | 结果与边界                                            |
| ------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------- |
| 客服认领、首响、双向消息、回执、结束恢复 AI | `CustomerServiceReceptionFlowTest` 使用真实 Controller、AccessService、应用服务与 JDBC；用户中心及认证上下文为模拟                                                | HTTP/JDBC 验证通过，不代表真实双客服账号浏览器验收    |
| 竞争、幂等、事务和隔离                      | `CustomerServiceReceptionJdbcTest` 覆盖双接单、取消竞争、并发 seq、Outbox 故障回滚及跨组织 FK；HTTP 覆盖同组未认领拒绝、撤权、未知字段和非法回执                  | 隔离 H2 验证通过，Kingbase 与多节点仍待验证           |
| 用户明确申请                                | 已登录 admin 在真实模型和开发库发送“请直接转人工”，创建 `VISITOR_DIRECT_REQUEST`，无二次确认卡                                                                    | 真实浏览器通过                                        |
| 模型主动建议与用户确认                      | 真实模型调用 `proposeHandoff`，收到 SSE 确认卡；确认前 episode 为空，确认后来源为 `AI_RECOMMENDATION_CONFIRMED`                                                   | 真实浏览器通过                                        |
| 取消恢复 AI                                 | 同一新建测试会话取消后为 `CANCELLED`，随后真实模型成功回答                                                                                                        | 真实浏览器通过                                        |
| 消息响应丢失后的重试                        | 页面拦截仅丢弃服务端保存后的首次响应，重试复用 clientMsgId，数据库读回唯一一条正文                                                                                | 真实浏览器和前端回归通过                              |
| 切换会话时旧轮询迟到                        | 可控网络先悬挂旧查询，新建会话后释放响应，确认不覆盖新状态、不请求新会话人工消息                                                                                  | 前端回归通过                                          |
| 参数拒绝                                    | 缺少 kind 的回执返回 400；接待 DTO 在 Spring Boot 忽略未知字段的默认配置下仍显式拒绝伪造 actor/group/sender；Controller 局部异常处理避免全局旧处理包装为 HTTP 200 | 包含生产 GlobalExceptionHandler 的 HTTP/JDBC 回归通过 |

本机服务为 `127.0.0.1:8081`，`dev,customer-service-test`，Consul 注册关闭。已有 MySQL 5.7 reception 增量沿用上轮执行结果，本轮未再次迁移或清理数据库。开发库新建的测试会话与申请保留，其中成功验收的两条等待申请已通过正常取消接口结束。

恢复证据位于 `.codex/qa/customer-service-resume-20261109/`；完整接口见 [reception-api.md](reception-api.md)。生产打包时显式跳过测试编译，目标测试另行 javac/JUnit 执行；全仓 `test-compile` 仍被原有 Tencent DTO/Properties 缺失阻断，未修改 POM 测试过滤或弱化断言。

最终结果：Java 定向 56/56、Node 10/10、真实浏览器 7 项通过且页面异常为零；最终包再次启动后复验，非法字段及缺回执类型均为 HTTP 400。完整接待 HTTP/JDBC 测试包含生产全局异常处理器。最终直接申请会话 `c2d99644-8d02-4edb-9f7b-ea83ee8e7aad` 和建议确认会话 `f14305a9-5513-490e-b695-6a43df2fb6ed` 已由正常取消接口结束。

人工消息目前每 2 秒按持久 seq 轮询，尚无独立人工 SSE/WS 订阅；真实客服登录、Kingbase、多节点/推送、工作日历/T+5/T+10/邮件和满意度闭环继续保留为后续验收项。不能将本轮接待接口验证表述为整个 013 需求已完成。
