# 历史实施记录

以下保留各阶段的当时结论，可能已被后续 owner 和范围决定替代。当前状态只以 [tasks.md](tasks.md) 为准，不从历史的 disabled/未实现说明推断现状。

2026-11-10 API 路径收敛：客服 HTTP 接口统一使用 `/api/customer-service` 下的无版本号路径，WebSocket 使用 `/api/customer-service/realtime/ws`；不注册 `/api/customer-service/v1` 或 `/api/customer-service/v2` 兼容别名。下文带版本号的路由仅记录当时阶段，不是当前入口；当前契约见 [reception-api.md](reception-api.md) 与 [handoff-api.md](handoff-api.md)。

# 013 展会 AI 客服实施任务

2026-11-09 接待 owner 决定已确认：本项目负责接单/接待/消息，内部系统负责账号/组/成员。当前能力与验收优先见 [reception.md](reception.md) 及 [reception-api.md](reception-api.md)；下文早期“待外部 claim/message 契约”已由本地 owner 替代。

本清单是 [实施计划](plan.md) 的执行状态。复选框只有在对应退出证据存在后才可勾选；Fake/H2 结果不能勾选真实身份、内部客服系统、实时接待或生产验收项。

2026-11-09 最新收尾：MySQL 5.7 开发库已部署等待/调查增量，本机 profile 已启用等待和调查；共享配置默认关闭，真实通知发送仍关闭。真实 admin/模型/MySQL 的自动调查、满意、不满意明确转人工、闭班与邮件工单闭环 5 项通过。模拟双身份、真实 Tomcat/H2/JDBC/SSE 浏览器接待 9 项通过，页面异常均为 0；不将其标为真实客服账号验收。客服后续跟进列表/原周期重试、CONTACT 独立采集邮箱、新 generation 调查失效及来源枚举恢复已验证。知识纠错通知从本期移除。新鲜证据见 `.codex/qa/customer-service-completion-20261109/`，契约见 [survey-waiting-realtime.md](survey-waiting-realtime.md)。

## 阶段 0：事实与依赖契约

- [ ] 取得 TAPD 1157652247001073148、1157652247001073149 可读故事及原型，记录状态、按钮、角色和异常态。
- [ ] 与内部客服系统确认账号/组/成员查询、登录主体映射、在线状态、容量、claim、客服消息、撤权事件、通知和 SLA 的接口与 owner。
- [ ] 决定内部系统不可用、无成员、成员离职/撤权、认领竞争、通知失败时的业务结果。
- [ ] 确认 SOP 冲突项：在线申请邮箱、T+5 接收方、人工 5/10 分钟关闭口径、调查卡 24 小时推荐过期策略。
- [ ] 冻结 owner 矩阵、身份信任来源、业务日历和组织/assistant/source 到外部组的可信映射。

阶段出口：账号、组、在线、容量、claim、消息的每类事实均只有唯一主写 owner；未经确认不建本地重复表。

## 阶段 1：新契约与清理旧方向

- [ ] 将命令、事件、错误码、来源/原因枚举和事件版本整理为唯一 v2 契约；不保留旧 v1 双协议。
- [ ] 明确 Conversation、HandoffProposal、HandoffEpisode、Satisfaction、Ticket 各自状态机与跨上下文端口。
- [ ] 定义 `propose_handoff`、`propose_resolution` 结构化 Tool schema、超时/低置信/非法输出降级。
- [ ] 定义 Conversation owner 的 generation epoch、AI 回复 fencing 和人工消息写入端口。
- [ ] 为内部客服 Adapter 建立契约测试和失败分类；确认外部 claim 幂等和回调安全。
- [ ] 删除/替换规范中“不排队即拒绝”、本地客服组/账号 CRUD 及把人工休眠当 AI 结束的要求。
- [ ] 基于真实发布情况确定 v1 清退和 v2 唯一上线方式；无业务数据则重建 schema，有数据先做 owner 批准的备份/导出。

阶段出口：无两个 owner 共同写一个业务事实；模型输出不能指定身份、组、权限或状态；关键状态转移有一致契约测试。

## 阶段 2：Conversation 与 handoff 纵切

- [ ] 为真实 AI conversation 提供稳定客服应用端口，验证会话创建、认证归属和来源/assistant 路由绑定。
- [ ] 实现持久化 HandoffProposal 与确认/拒绝/过期幂等命令，Outbox 成功后才推送卡片。
- [ ] 实现用户直接申请、确认 AI 建议、主动取消三种入口；明确申请不得二次确认。
- [ ] 实现 HandoffEpisode 的触发来源、reasonCodes、操作者、策略版本和 trace 引用。
- [x] 同一事务或可靠协调步骤推进 AI generation epoch，拒绝迟到 AI 输出。
- [ ] 用正式登录和隔离目标数据库验证；禁止使用 X-CS-Mock 身份作为验收证据。

阶段出口：真实 conversation 上建议卡可恢复；直接申请与 AI 建议行为有别；重复命令/回调无重复状态或事件。

## 阶段 3：内部客服 owner 与实时接待

- [ ] 实现内部客服系统 directory/identity adapter，不复制账号、组或成员主数据。
- [ ] 对每个外部主体执行真实登录 principal 映射和组织/组授权复核。
- [ ] 按已确认 owner 接入 available/claim/revoke 能力；通过并发认领测试证明只有一个有效接手人。
- [ ] 实现客服工作台待处理/接入/处理中/后续跟进/结束视图及原型要求的操作状态。
- [ ] 实现同一 conversation 的访客/客服纯文本消息、真实授权、WS/SSE 推送和 HTTP 水位恢复。
- [ ] 验证接手后 AI 保持静默；客服结束后 AI 恢复按 Conversation owner 规则推进。
- [ ] 覆盖撤权、移组、离线、外部系统超时、重复回调和旧连接发消息。

阶段出口：真实客服系统用户完成认领；双方消息实时到达；断线补齐正确；无双答、重复认领或跨组越权。

## 阶段 4：工作时间等待与邮件跟进

- [ ] 接入服务端权威工作日历、节假日和下一办公时间查询。
- [x] 实现 T+5 内部提醒和可靠投递状态；H2/本机网关契约通过，真实接收方待接入。
- [x] 实现 T+10 访客忙碌提示、继续等待/邮件跟进选择和可取消状态；本地 JDBC 与 UI 夹具通过。
- [x] 实现办公时间结束时未接单请求的显式收敛及非办公时间回复；本地验证接单竞争和回复权恢复。
- [x] 实现邮箱校验、最小必要联系方式保存、邮件 Outbox、发送回执、重试和最终失败恢复；SENT 仅网关受理，真实投递待验。
- [ ] 验证 16:59 申请、营业结束、跨日/节假日、用户离开/重连和继续等待边界。

阶段出口：无隐式永久等待；状态与实际客服/邮件 owner 一致；邮件写入记录不冒充送达。

## 阶段 5：AI 结束与满意度

- [x] 实现并验证 `proposeResolution` 后端受限 schema、可信候选持久化、同轮幂等及 generation 失效读取；不代表运行时或问卷闭环已启用。
- [x] 在最终 AI 回复及 Turn 成功后，以已保存用户消息和同范围成功 Turn 证据确定性记录明确结束表达；与模型候选分离、摘要不含正文。
- [x] “没有”类短答仅在上一条成功 AI 回复确实询问是否还有问题/需要帮助时记录确认信号；致谢、寒暄、长句和无追问上下文均不命中。
- [ ] 接通 `propose_resolution` 运行时调用；继续保持 disabled，直到问卷资格、survey owner、C 端卡片/消费和可靠投递闭环就绪。
- [x] 实现最终 AI Turn 成功后的 10 分钟 idle deadline；新 generation/人工 owner 同事务取消旧 deadline，到期仅记录 `IDLE_TIMEOUT`，Worker 默认关闭。
- [x] 落地 Conversation owner 的 survey 状态持久化及状态/Outbox 原子提交；覆盖每会话唯一、满意/不满意、人工 opt-in/拒绝、过期、幂等和组织范围约束，不代表资格或访客投递已接通。
- [x] 实现 Conversation owner 的问卷资格创建编排；按当前 generation、确定性用户结束信号、成功 Turn、完整人工历史和完整内容评估逐项 fail-closed，创建与 offered Outbox 同事务提交。
- [x] 验证问卷同会话重试复用首次记录、Outbox 写失败整体回滚、旧 generation 失效拒绝创建、历史覆盖不足和内容评估缺失拒绝创建。
- [x] 持久化不可变的完整内容评估结论；写入经当前 generation 围栏，按轮次幂等并拒绝异结论，数据表只保存引用/摘要、不保存聊天正文。
- [x] 人工历史门禁区分覆盖完整与不足；当前唯一接待 owner 返回 COMPLETE，任意状态的人工周期均排除调查，其他不完整来源仍拒绝。
- [x] 按当前开发阶段的 owner 决定，以本项目客服编排的完整接待历史作为唯一人工历史；不保留外部/旧系统兼容查询。
- [x] 保守排除人工历史、寒暄、推广、未解决和不完整历史；资格策略、generation 围栏与真实显式结束路径已验证。
- [x] 实现按 conversation 唯一的满意度卡、结果持久化、过期和恢复展示；JDBC/HTTP/UI 夹具验证，不代表真实资格已接通。
- [x] 实现满意致谢/结束评价交互；不满意询问是否人工；拒绝只记评价；同意后创建新 handoff episode；本地事务与 UI 已验证。
- [x] 确保评价来源为 `SURVEY_DISSATISFIED_OPT_IN`，关联 surveyId、轮次、原因和触发主体；并发重放仅一周期，失败全回滚。

知识纠错通知、负责人配置和自动知识更新不在本期；按 2026-11-09 用户决定，由人工后续依据满意度维护知识库。

阶段出口：对话结束识别不与人工状态混淆；问卷至多一次；不满意本身不自动视为用户同意人工。

## 阶段 6：目标环境验收与发布

- [ ] 在实际 MySQL/Kingbase 目标版本验证 migration、约束、行锁、并发、Outbox 和恢复。
- [ ] 在真实内部客服系统验证目录、认领、消息、撤权、通知和回调签名。
- [ ] 在真实访客认证和两个原型对应前端执行完整浏览器 E2E。
- [ ] 执行跨组织/组越权、撤权时旧连接、Tool 越权、回调重放及个人信息日志检查。
- [ ] 执行消息延迟、候选竞争、持续等待、邮件失败、死信和多节点恢复演练。
- [ ] 建立 handoff 来源、原因、认领、首响、解决、满意度和通知失败的对账指标/告警。
- [ ] 形成阶段验收证据、灰度/关闭新申请方案和数据清理审批；未满足 P0 则保持 PARTIAL。

## 当前进度

2026-11-06 增量：正式 HTTP 路由已统一为 `/api/customer-service/v2`，不保留 v1 双路；新增会展 AI `proposeHandoff` 受限 Tool schema 和策略，模型只能提交固定原因码和置信度，服务端核验当前会话、turn、租户、组织、大会与智能体绑定，并对无上下文、非法原因码和低置信结果 fail-closed。新增独立于 `cs_session/cs_event` 外键的 proposal、访客确认/拒绝回执、REQUESTED episode 与 handoff Outbox owner；同轮重复建议、命令重放/异参、定时过期和 Outbox 同事务回滚均有 H2 JDBC 用例。建议 TTL 默认 10 分钟（1 分钟至 24 小时），到期扫描默认关闭并受批次/间隔上限约束。新增建议事务用例 8/8、原策略用例 5/5、MySQL/Kingbase 迁移契约 1/1 通过。

2026-11-06 Outbox 增量：新增 handoff v2 独立 Rabbit durable queue、confirm/mandatory-return publisher 和租约 Worker；数据库领取、过期租约接管、投递回执 CAS、有界重试及 DEAD 状态已实现。Worker H2 测试覆盖成功、失败耗尽、租约接管和批次上限；Rabbit publisher 测试覆盖稳定 envelope/幂等头、NACK 与 ACK 后 mandatory return。两个测试类单独编译并由 JUnit Console 执行，合计 7/7 通过。生产 service reactor `package` 通过，但使用 `-Dmaven.test.skip=true`。全量 Maven testCompile 被既有腾讯云测试缺少 `TencentAvatarProperties`、`TencentStsProperties` 和 DTO 类型阻断；单模块 Surefire 因加载旧本地 domain jar 找不到新增接口也未能执行。

2026-11-06 访客建议卡增量：新增仅面向正式登录账号的建议卡恢复/确认/拒绝 v2 HTTP Adapter。账号来自认证 `accountId`，租户和顶级组织来自 AI conversation 持久审计归属，并要求现有用户-会话关系 owner 确认访问权；请求体不接收身份、组织或客服字段，匿名访客在没有正式恢复凭据前 fail-closed。可信 `topOrgId` 现由已解析 assistant 上下文写入认证请求和异步会话快照，客服建议策略可按服务端组织上下文判定。service 定向测试 18/18、web 授权及路由测试 8/8 通过，后者覆盖最小恢复卡片、幂等决策映射、伪造授权字段拒绝、匿名拒绝和无组织归属拒绝。为绕开既有腾讯云测试缺失类型，仅在本地临时限制 Maven testIncludes 编译目标用例，验证后已移除过滤配置；编译阻断本身未修改。扩大相邻回归时另有两个既存失败：`AssistantConversationBindingTest.eventAssistantUsesCompositeMemoryKeyWithoutChangingSessionId` 的旧 memory key 断言，以及已被本地座位查询模板替换的 `AssistantPromptProfileRoutingTest` 旧模板断言；这两项不属于本次客服建议切片，未改其产品行为或断言。

2026-11-06 generation fencing 增量：可信 `generationEpoch` 随 AI 请求上下文传播；MySQL/Kingbase 分别使用本地幂等插入语法，AI 新轮次、人工 owner 切换与 epoch 更新都由共享数据源事务/行锁保护；流式输出逐 chunk 校验 owner，最终回复写入和 Turn 完成也在持锁事务中校验，失效 Turn 进入独立 `SUPERSEDED` 终态。新增上下文、流取消、状态机、仓储并发及 proposal/episode/Outbox/owner 原子回滚测试，相关定向测试合计 19/19 通过；基础设施测试补齐 H2 test-scope 依赖并使用 MySQL 兼容模式。MySQL/Kingbase 真实目标版本尚未执行，阶段 6 不勾选。

2026-11-07 访客直接申请增量：新增 `/api/customer-service/v2/conversations/{conversationId}/handoffs`，请求只收幂等 requestId，正式认证 Adapter 从登录主体、conversation 持久归属及用户-会话关系解析租户、组织、assistant 与大会；未知请求字段即使被 JSON 层忽略也不能覆盖可信命令字段。直接申请不创建 AI proposal、不需要二次确认，在共享事务中写 `VISITOR_DIRECT_REQUEST` / `USER_REQUESTED` 的 `REQUESTED` episode、独立命令回执和 `handoff.requested` Outbox，再将 Conversation owner 原子切换为 HUMAN；同键同参重放复用原 episode，同键异参及 HUMAN 接待中新增 requestId 均拒绝，并通过并发同键请求证明只有一个 episode/Outbox。响应文案只说明申请已记录并等待受理，不表述客服已接入。新增 JDBC 直接申请/回滚用例及策略用例 22/22、HTTP/正式授权用例 7/7、MySQL/Kingbase 迁移契约 1/1 通过；测试定位并修复 owner 锁查询的 SQL 空格拼接错误，并明确拒绝模型使用仅供访客直申的 `USER_REQUESTED` 原因码。临时 Maven 测试过滤配置已移除。此增量只证明本地 H2 和 HTTP Adapter 契约，未证明 Outbox 消费、真实客服受理、匿名访客恢复、C 端卡片推送或目标数据库行为。

2026-11-07 恢复与取消意图增量：新增 `GET /api/customer-service/v2/conversations/{conversationId}/handoffs/latest`，正式授权后按租户/组织/会话/assistant/大会恢复最新 episode 和可选取消进度，无记录返回 `episode=null`；投影不含主体、内部路由或 trace。新增 `POST .../handoffs/{episodeId}/cancellation`，要求原申请者、稳定 requestId 和正数 expectedEpisodeVersion，在 episode 行锁及 2 秒短事务内原子写独立 `PENDING_OWNER_ACK` 取消意图和 `handoff.cancellation.requested` Outbox，HTTP 202 仅表示等待客服系统确认。同键同参保留取消标识和首次时间，同键异参/新键重复意图/旧版本均拒绝；不改 episode 版本、原来源原因或 HUMAN owner。新增取消表双方言初始迁移，完整归属外键约束父 episode，并补复合唯一键。定向 Maven reactor 回归 service 38/38（JDBC 25、策略 6、Worker 4、Publisher 3）、web 18/18（本次 Controller 10、proposal Controller 4、正式授权 4）、迁移契约 2/2 通过；覆盖恢复隔离、并发取消重放、失败整体回滚及同键重试、跨会话外键拒绝和生产 Spring 构造装配。修正两个应用服务的生产构造选择，避免固定时钟测试构造影响 Spring 注入。临时 testIncludes 过滤已移除；未修改既有腾讯云测试。已落地接口与部署前提见 [handoff-api.md](handoff-api.md)。

主动取消已落地意图，仍未实现终态：规范要求先由内部客服 owner 确认撤销或阻止迟到认领，之后才能把 Conversation owner 恢复给 AI。当前缺少该 owner 的取消接口/回执和 Outbox 消费者，因此不提供本地 CANCELLED 或恢复 AI 的捷径；阶段 2 完整取消入口和真实数据库/认证出口保持未勾选。

2026-11-07 AI 结束候选增量：按阶段 5 增加 Conversation owner 的 `AiResolutionCandidate`、候选应用服务/JDBC 端口和受限 `proposeResolution` Tool，模型只收 confidence，不能提交身份、路由、用户确认、idle 时长或问卷结果。服务端以 0.85 阈值和 `ai-resolution-candidate-v1` 策略决定是否保存；同 generation 行锁的 2 秒事务中记录不可变 `MODEL_POSSIBLE_RESOLUTION` 信号，同轮同参复用首次引用/trace/时间，异参拒绝。新轮次或转人工后旧候选不再由 `findCurrent` 恢复，历史仍保留；数据库故障安全降级，主回答可继续，修复后原轮次能重试。新增 MySQL/Kingbase 一次性迁移，父 generation 增加组织范围唯一键、候选用完整组织范围外键保护，不访问客服表、不发送问卷或通知。真实 MySQL 脚本在 H2 MySQL 模式执行，双方言另做静态契约验证；这不等于目标数据库验收。

本轮候选及相邻回归 Maven reactor 70/70 通过：新增 23 项（JDBC 14、策略 4、Tool/schema/生产装配 3、迁移静态契约 2），相邻 generation/人工建议/取消/Outbox 等 47 项。真实 Tool schema 测试定位到未保留 Java 参数名导致 confidence 退化为 arg1；为 service 模块持久配置 `maven.compiler.parameters=true`，清理该模块旧构建产物后重新编译，候选与相邻 handoff schema 均验证为业务字段且不暴露 memoryId。临时测试源码过滤已移除，原有依赖和既有腾讯云测试未修改。详细边界见 [ai-resolution-candidate.md](ai-resolution-candidate.md)。

建议 Tool 的存储/确认授权和 AI generation fence 已有定向证据，但 Rabbit Outbox 尚无访客网关消费者，C 端实时 handoff 卡片渲染契约也未在当前仓库实现，因此 Outbox Worker 继续默认关闭、`proposeHandoff` 继续不注册到运行时。`proposeResolution` 已有后端候选切片，仍保持 enabled=false、不进基础工具集；确定性明确结束/追问确认信号及十分钟 idle deadline 已落地，idle Worker 默认关闭。客服 owner 历史资格、问卷和满意度闭环、完整 episode 状态机及内部客服 Adapter 契约仍未完成。

阶段 0 至 6 均未达到新规范出口。已有代码和旧 Fake/H2 测试作为可复用研究，不自动勾选新验收项。TAPD 原型与内部客服接口契约未取得，相关依赖关闭前不进入依赖真实 owner 的正式实现。service 模块完整 testCompile 的既有腾讯云类型缺失阻断未处理；最新证据为临时限制目标测试源码后运行 Maven reactor 定向测试，不是全仓编译或真实数据库/E2E 通过。

2026-11-07 确定性用户结束信号增量：在成功 AI 回复保存且 Turn 为 `SUCCESS` 后，Conversation owner 使用完整组织范围的 Turn/消息证据识别 `USER_EXPLICIT_END` 或 `USER_CONFIRMED_NO_FURTHER_QUESTION`。独立 `ai_resolution_user_signal` 只保存消息引用、类型、策略和不含正文的摘要；“没有”必须对应上一条成功 AI 的真实追问，致谢、寒暄、复合长句及无历史问答不命中。读写与当前 AI generation 行锁同事务，读取/写入故障按无信号降级不阻塞回答；新 generation/HUMAN owner 后历史信号不能恢复为当前事实。模型候选仍保持独立，`proposeResolution` 仍禁用；此增量不创建 resolution episode、不证明从未人工、不问卷/推送。

2026-11-07 idle deadline 增量：最终成功 Turn 提交后以已保存消息证据安排固定 10 分钟 UTC deadline；新 generation 推进或转人工与取消旧期限处于同一 owner 事务。期限由有租约、有限重试和失败留痕的可恢复 Worker 处理，先锁 generation 再锁 deadline；成功仅新增独立 `IDLE_TIMEOUT` 信号并完成任务，不关会话、不问卷、不推送。deadline/信号以租户、组织、conversation、generation 复合外键绑定并保存 `ai-idle-deadline-v1` 规则版本。Worker 通过 `AI_RESOLUTION_IDLE_DEADLINE_ENABLED` 显式开启，默认关闭。最终回复/Turn 提交后另起信号和 deadline 短事务，确保附件事实失败不会回滚主回答。

验证：generation/信号/deadline 基础设施目标测试 17/17，core idle policy 和 generation 服务 3/3，service worker 正式 MySQL migration + H2 due/信号事务集成 1/1；主代码 `byt-cloud-agent-service -am compile` BUILD SUCCESS。worker 测试通过临时 testIncludes 只编译一个目标文件，之后已移除；全量 service testCompile 的历史 Tencent 类型阻断仍存在。未执行真实 MySQL/Kingbase、未启用 Worker、未对共享数据库写入。

验证：全模块主代码 reactor `mvn --% -o -pl byt-cloud-agent-service -am -DskipTests compile` BUILD SUCCESS。infra 目标组 11/11 通过；service 目标组 25/25 通过。service 全量 testCompile 仍因既有缺失的 Tencent DTO/properties 失败；本轮只临时纳入四个目标测试文件完成验证，临时 POM include 已移除。H2 MySQL 模式执行仓库 generation/结束信号 migration，但 Turn/明细为最小测试夹具；Kingbase 仅静态检查。没有真实数据库、模型、问卷投递或浏览器 E2E 证据。

本轮复核：infrastructure idle deadline/信号/迁移契约及 generation 目标测试 17/17 通过，core deadline/generation 3/3 通过，service 结束候选、信号策略/编排、Tool schema 与 idle Worker 29/29 通过；service reactor 主代码在上述测试构建中重新编译成功。先尝试命令行 testIncludes 未能限制测试编译，确认仍被既有 Tencent DTO/properties 缺失阻断；随后以临时 service POM testIncludes 限定目标测试源码完成验证并已删除临时配置。MySQL migration 在 H2 MySQL 模式执行，Kingbase 为静态迁移契约验证；真实目标数据库、运行时 Worker 启用及浏览器 E2E 未执行。

2026-11-07 人工历史查询初始增量（旧布尔契约，已由下方覆盖级别增量取代）：客服编排 owner 新增本地任意 episode 存在性端口，Conversation 在共享事务持有当前 generation 行锁后调用；当时的“当前未观察到”只代表本地查询为空，不能证明外部或切换前历史完整，也不能作为发放授权。跨租户、组织、会话隔离；路由异常或尚未受理也不漏掉同会话人工申请。初始定向测试与 H2/外部 owner 限制见记录；当前状态以 `CustomerServiceHandoffHistorySnapshot` 和显式覆盖级别契约为准。

2026-11-07 survey 持久化增量：Conversation owner 新增单会话唯一的 AI 满意度状态及同事务 Outbox；状态为 `OPEN/SATISFIED/DISSATISFIED/EXPIRED`，不满意后的 `PENDING/OPTED_IN/DECLINED` 独立记录。重复回答/创建幂等，过期不推断满意，回答时间由领域及数据库共同限制在 `[openedAt, expiresAt)`，opt-in 只产生 `handoff.survey-opt-in-requested`，不切换 AI/HUMAN owner、不冒充客服受理。MySQL/Kingbase 一次性迁移、状态检查、范围约束和回滚说明已补齐；infra 定向 10/10、core 6/6 通过，service 主代码 reactor compile 成功。H2 MySQL 模式执行了迁移；目标数据库未执行。资格编排尚未连接：本地历史观察不能证明外部/切换前历史完整，寒暄/垃圾/推销筛选、访客卡片恢复/命令 API、Outbox 消费者、opt-in handoff episode、真实模型和浏览器 E2E 仍未完成；`proposeResolution` Tool 与 survey 投递继续关闭，整体 PARTIAL。

2026-11-08 问卷资格编排与验证增量：Conversation owner 新增 `AiSurveyCreationService`，在当前 generation 行锁事务内核验确定性结束信号、同范围成功 Turn、人工历史覆盖和完整内容评估，再创建唯一调查及 `ai.survey.offered` Outbox；已有调查重试只复用首次引用，缺少内容评估、历史覆盖不完整或 generation 已失效均拒绝。新增 core 资格编排测试 5/5，覆盖通过路径、重试、失效代次、评估缺失和历史覆盖不足；infra 问卷 JDBC 测试 6/6，新增 Outbox 写入失败时 survey 与 generation 事务回滚验证；generation owner JDBC 测试 4/4，验证新代次及 HUMAN owner 使旧 epoch 失效。问卷仓储和 generation 仓储组合回归 10/10 通过。`mvn --% -o -pl byt-cloud-agent-core -am -Dtest=AiSurveyCreationServiceTest -Dsurefire.failIfNoSpecifiedTests=false test` 与 `mvn --% -o -pl byt-cloud-agent-infrastructure -am -Dtest=AiResolutionSurveyRepositoryJdbcTest,ConversationGenerationRepositoryImplTest -Dsurefire.failIfNoSpecifiedTests=false test` 均 BUILD SUCCESS。资格创建入口目前没有生产 HTTP/Tool/Worker 调用；内容评估 Adapter、覆盖外部及切换前记录的客服历史 owner、访客卡片恢复/消费和可靠投递仍未实现，运行时保持关闭。H2 MySQL 模式不代表目标 MySQL/Kingbase 验收，也没有真实浏览器 E2E 证据。

2026-11-08 内容评估 owner 增量：新增 `AiSurveyContentAssessmentRepository`、generation 行锁围栏写入器和 Conversation owner JDBC Adapter，评估按 tenant/org/conversation/epoch/turn 唯一；同结论重试保留首次引用/时间，异结论冲突拒绝，旧 generation 不得写入。MySQL/Kingbase 独立一次性 migration 含归属外键、分类检查、路由索引和回滚说明，表不复制聊天正文或提示词。core 围栏测试 3/3、infra JDBC 3/3、双方言迁移契约 2/2 通过；H2 MySQL 模式执行 MySQL migration，未执行目标 MySQL/Kingbase。尚无完整历史评估生产者连接此 owner，故缺少评估仍 fail-closed；问卷运行时、访客 API/卡片投递和 handoff 消费闭环继续关闭/未接通。

2026-11-09 owner 边界清理：删除本地客服资格/状态/容量/组关系维护的 HTTP、Service、Repository 与专属测试；UserCenter Adapter 只读取外部分组和账号，不再查询不存在于合并脚本中的本地资格表，也不返回 `alreadyAssigned`。合并 MySQL/Kingbase 脚本只包含已确认由 Conversation 与 handoff 编排 owner 写入的 13 张表；客服目录、在线状态、容量与 claim 仍须接内部系统 Adapter/契约。随后删除没有 SQL/owner 支撑的独立 `cs_session/cs_message/cs_assignment/cs_ticket` 工作台、消息、回执、本地分配及对应 HTTP/Worker/测试代码，并清掉死配置；旧静态工作台也因只调用这些接口而移除。真实“同一 AI conversation”聊天 Adapter 和 E2E 仍未完成。

2026-11-09 后续收尾：按用户确认，本批不实现知识纠错通知、负责人配置或通知网关；满意度结果保留给后续人工知识更新。问卷人工历史门禁统一采用本项目客服 owner 的完整周期记录，不再以 LOCAL_ONLY 阻断当前开发流程。等待期间新增 CONTACT 动作，仅保存有效邮箱并保持在线等待；EMAIL 仍显式取消未接单等待后创建跟进工单。调查读取、回答、人工选择和展示回执均受当前 generation 围栏保护，过期/人工接管后的未决问卷不再显示或写入。

2026-11-09 清理与验证：service 与 web 主代码 reactor compile 均通过；Common 6/6、Infrastructure 36/36、Core 15/15 定向测试通过。Service `test-compile` 仍被无关既有 Tencent 测试缺失类型阻断（`TencentAvatarProperties`、`TencentStsProperties` 及 Tencent DTO）；未执行开发库，目标 MySQL/Kingbase、真实认证、内部客服 Adapter、同一 conversation 双端聊天及卡片/问卷投递仍未验收。

2026-11-09 登录请求头修复：OIDC `getToken()` 返回值通过共享 `AuthManager.getAuthorizationHeader()` 统一规范为服务端要求的 `Bearer <token>`，聊天测试 API、管理台和海报 API 使用同一方法；避免已登录请求落入 Session 认证回退。Node 请求头契约测试 3/3、四个相关静态 JS 文件 `node --check` 通过；未使用真实 token 或进行浏览器认证 E2E。
