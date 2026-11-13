# AI 对话结束信号与候选实现契约

更新：2026-11-09。属于 [实施计划](plan.md) 阶段 5，产品规范为 [spec.md](spec.md) 4.6。模型候选、用户结束信号、idle deadline、完整客服历史门禁、内容评估、单次 survey 与访客 HTTP/卡片及明确人工选择已实现。真实 admin/模型/MySQL 5.7 的显式解决调查闭环已通过；真实客服身份、外部通知、Kingbase 和多节点发布验收仍有缺口，整个 013 保持 PARTIAL。

## 1. 模型接口与信任边界

逻辑工具 `propose_resolution` 的运行时名为 `proposeResolution`，只允许模型提交：

```json
{ "confidence": 0.93 }
```

`@ToolMemoryId` 由运行环境绑定，不能出现在模型参数中。会话、轮次、租户、组织、assistant、大会、trace、generation 和策略版本均从服务端请求快照取得。模型不能提交 `userConfirmed`、`idleMinutes`、问卷、满意度结果或转人工目标。

候选仅表达模型认为“可能已解决”。服务端使用有限零到一的置信度，固定阈值为 `0.85`，策略版本 `ai-resolution-candidate-v1`。阈值仅控制是否保存候选，不代替是否发送评价的业务规则；更改阈值须更新策略版本。无完整上下文、会话串线、非法/低置信值均无候选写入。

本轮为 service 模块显式设置 `maven.compiler.parameters=true`。LangChain4j 1.10 的 `@P` 不支持参数别名，默认编译若未保留方法参数名，会把 schema 生成成 `arg1`。构建必须保留参数名；已有旧 classes 时需重新编译该模块，单改 POM 的增量构建不会重写旧 class。候选与相邻 handoff Tool 的真实 schema 均纳入回归；本项目不保留 `argN` 兼容协议。

## 2. 记录、重放和时效性

Conversation 应用服务捕获不可变候选后，通过 generation 应用端口进入共享主数据源的 2 秒短事务，锁定并验证完整范围、当前 AI owner 和 generation epoch，再保存候选。

- 唯一信号类型为 `MODEL_POSSIBLE_RESOLUTION`；没有“已关闭”“用户已确认”或“已发送评价”状态。
- 服务端生成 `resolutionEpisodeId`，同 tenant/org/conversation/turn 只保存一次。
- 同轮同参重试返回首次引用、时间和 trace；摘要绑定可信范围、turn、epoch、置信度及策略，trace 和临时生成的新引用不参与摘要。
- 同轮不同置信度或策略等输入发生冲突，保留首次候选，不覆盖审计。真正新一轮须有新 turn 和 generation。
- 读取 `findCurrent(binding, epoch)` 时再次锁定 generation；旧 epoch、转人工或归属不符均返回空。新消息/人工切换后历史候选保留，但不能再作为当前信号。
- 数据库故障在事务回滚后返回明确降级，不返回不存在的候选引用；修复后原轮次可以重试。Tool 不继承外层模型调用事务，避免长事务持锁。

成功结果示例：

```json
{
    "code": "CANDIDATE_RECORDED",
    "resolutionEpisodeId": "are_generated-id",
    "proposedAt": "2026-11-07T02:00:00Z",
    "replayed": false
}
```

| code                         | 含义                                                 |
| ---------------------------- | ---------------------------------------------------- |
| `CANDIDATE_RECORDED`         | 本地候选已保存，仍须独立判断结束和调查资格           |
| `CONTEXT_UNAVAILABLE`        | 缺少可信会展业务/会话/轮次/追踪/代次绑定或范围不一致 |
| `INVALID_MODEL_OUTPUT`       | 置信度为空、非有限或越界                             |
| `BELOW_CONFIDENCE_THRESHOLD` | 未达服务端记录阈值                                   |
| `SUPERSEDED`                 | 当前 owner 已非 AI，或本轮代次/持久范围失效          |
| `CONFLICTING_SIGNAL`         | 同轮已有不同业务输入，保留首次记录                   |
| `TEMPORARILY_UNAVAILABLE`    | 存储故障或短事务前提不满足；主回答可继续             |

所有拒绝结果的候选引用、时间均为空，`replayed=false`。普通日志只保留 session/turn/trace/org/bvent 引用和错误类型，不记录模型内容、凭据或原始异常报文。

## 3. 确定性用户结束信号

此信号独立于 `MODEL_POSSIBLE_RESOLUTION`，由 Conversation owner 在最终 AI 回复和 Turn `SUCCESS` 已提交后判定。信号 以提交后的独立短事务读取/写入，并重新锁定当前 generation；读取错误、缺消息、失败 Turn、明细范围不匹配或 owner/epoch 已变化均 fail-closed，且不会回滚已提交的主回答。

分类策略为 `ai-user-resolution-v1`：

- `USER_EXPLICIT_END`：只接受完整、无歧义的已解决表达，例如“问题解决了”“已解决”“已经处理好了”。普通致谢/寒暄、否定、未解决长句和正文中偶然包含结束词不命中。
- `USER_CONFIRMED_NO_FURTHER_QUESTION`：只接受精确的无问题短答，例如“没有/没有了/不需要了/no thanks”，并且上一条成功 AI 回复确实以疑问方式询问是否还有其他问题或需要帮助。普通欢迎语、陈述“有需要请继续问”、缺失上一条成功回复都不能将“没有”变成确认。

信号记录 `resolution_signal_id`、信号类型、来源用户明细 ID、追问时的上一条助手 Turn/明细 ID、generation epoch、trace、策略版本和不含正文的 SHA-256 摘要；不保存或写日志输出聊天正文。同 Turn 同类型只保留一个信号。重放保留首次 ID/时间/trace，异证据不能覆盖。`ai_resolution_user_signal` 与模型候选共用 generation owner 的组织范围外键。新用户消息推进 epoch 或人工取得 owner 后，旧信号不可恢复为当前信号。

2026-11-11 起取消用户沉默后触发满意度调查。AI成功回复后不再安排空闲期限，没有空闲扫描任务；历史空闲信号在仓储入口过滤，不会创建新调查。用户明确结束或确认无后续问题的文本规则继续生效。

这些信号不等于调查资格、用户满意或卡片送达。当前开发系统以本项目为唯一接待 owner，JDBC Adapter 覆盖所有状态的 episode 并返回 COMPLETE；不存在人工历史时为 NO_HISTORY_CONFIRMED，任何曾申请人工的会话都被排除。其他来源若声明覆盖不足仍 fail-closed。当前 AI owner 不能代替人工历史核验；新 generation 或人工接管后未决调查不展示、不接受首次回答/人工选择，已提交结果可幂等恢复。

## 4. 数据 owner 与部署

`ai_resolution_candidate` 和 `ai_resolution_user_signal` 的唯一写入 owner 是 AI Conversation 上下文，持久化 Adapter 位于 `infrastructure/agent/resolution`；模型候选应用服务位于 `service/conversation/resolution`，成功 Turn 证据、确定性信号位于 `core/agent/resolution`。客服模块不能直接写这些表，也不通过查询客服 Entity/表判断问卷资格。

`ai_resolution_survey` 与 `ai_resolution_survey_outbox` 由 Conversation owner 唯一写入。`AiSurveyRuntimeService` 按完整成功问答生成 `explicit-resolution-evidence-v4` 评估，不完整、寒暄、推广和未解决内容均不放行，模型候选与用户沉默不能替代用户明确结束信号；持久评估只保存引用和结论。`AiSurveyCreationService` 在 generation 锁内再次核验全部资格，创建单次 survey 与 offered Outbox。访客展示后显式 receipt 消费 offered；明确 opt-in 通过客服 owner 端口创建周期、转移回复权并消费相关事件，共用本地事务，失败全部回滚。

表只保存信号和关联引用，不保存聊天正文、个人联系方式或客服账号。复合外键 tenant/org/conversation 引用 generation owner 的唯一组织范围键，数据库拒绝跨组织候选；assistant、大会、epoch 在应用层持锁核验。旧候选/信号作为不可变审计事实保留；均不做逻辑删除，默认外键阻止先删父记录。

2026-11-11 起，本轮结构统一到 `data/sql/{mysql|kingbase}/ai_20261111_customer_service/all_in_one.sql`，双方言包含28张表，支持已有开发库整份重跑并补齐缺失结构，见[数据库入口](database-20261111.md)。新版不再新建空闲期限表；已有数据库的历史表和数据保留，不执行删除。
