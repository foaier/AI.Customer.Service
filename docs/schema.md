# 独立数据库字典

由 ORM 模型和中文 XML 注释生成。所有表均物理保留，不启用隐式逻辑删除。

## ai_conversation_generation

Conversation owner 的回复代次和当前轮次，人工与 AI 共用同一会话。。Owner：Conversation。

| 字段                   | 必填 | 业务含义                                                       |
| ---------------------- | ---- | -------------------------------------------------------------- |
| tenant_id              | 是   | 服务端确定的租户，参与每次读写条件。                           |
| top_org_id             | 是   | 服务端核验的组织，参与每次读写条件。                           |
| id                     | 是   | owner 内唯一业务标识，与租户、组织共同构成数据库主键。         |
| assistant_config_id    | 是   | 服务端配置的助手引用，不随模型输出变化。                       |
| bvent_id               | 否   | 来源大会引用，空表示非展会场景。                               |
| conversation_id        | 是   | 贯穿 AI、人工、调查及异步任务的会话引用。                      |
| created_at             | 是   | 首次创建 UTC 时间。                                            |
| current_turn_id        | 否   | 正在生成的 AI 轮次，拒绝并发轮次及迟到响应。                   |
| epoch                  | 是   | 回复代次，每次人工接管或恢复均递增。                           |
| human_episode_id       | 否   | 当前独占回复权的人工周期，空表示允许 AI。                      |
| last_completed_turn_id | 否   | 最近完整成功轮次，调查恢复 Worker 仅处理尚未评估的新成功轮次。 |
| next_seq               | 是   | 整个会话共享的人工消息最大序列。                               |
| principal_id           | 是   | 原会话的访客账号，仅用于归属校验。                             |
| survey_checked_turn_id | 否   | 最后已完成资格评估的成功轮次，不根据用户沉默产生新调查。       |
| turn_lease_until       | 否   | AI 生成租约 UTC 截止，崩溃后允许新轮次恢复。                   |
| updated_at             | 是   | 最后活动 UTC 时间，不作为调查结束证据。                        |
| version                | 是   | 从一递增的乐观并发版本，幂等重放不得递增。                     |

## ai_human_message

Conversation owner 的人工消息，所有周期共享递增序列。。Owner：Conversation。

| 字段                | 必填 | 业务含义                                               |
| ------------------- | ---- | ------------------------------------------------------ |
| tenant_id           | 是   | 服务端确定的租户，参与每次读写条件。                   |
| top_org_id          | 是   | 服务端核验的组织，参与每次读写条件。                   |
| id                  | 是   | owner 内唯一业务标识，与租户、组织共同构成数据库主键。 |
| assistant_config_id | 是   | 服务端配置的助手引用，不随模型输出变化。               |
| bvent_id            | 否   | 来源大会引用，空表示非展会场景。                       |
| client_msg_id       | 是   | 发送主体下稳定的消息幂等键。                           |
| content             | 是   | 1 至 4000 字符纯文本。                                 |
| conversation_id     | 是   | 贯穿 AI、人工、调查及异步任务的会话引用。              |
| created_at          | 是   | 首次保存 UTC 时间。                                    |
| episode_id          | 是   | 发送时的有效周期。                                     |
| principal_id        | 是   | 原会话的访客账号，仅用于归属校验。                     |
| sender_id           | 是   | 认证和认领关系确定的发送账号。                         |
| sender_role         | 是   | 只允许 VISITOR 或 AGENT。                              |
| seq                 | 是   | 会话内唯一递增序列。                                   |
| version             | 是   | 从一递增的乐观并发版本，幂等重放不得递增。             |

## ai_human_receipt

单一会话主体的接收及已读水位，不能代替对方确认。。Owner：Conversation。

| 字段                | 必填 | 业务含义                                               |
| ------------------- | ---- | ------------------------------------------------------ |
| tenant_id           | 是   | 服务端确定的租户，参与每次读写条件。                   |
| top_org_id          | 是   | 服务端核验的组织，参与每次读写条件。                   |
| id                  | 是   | owner 内唯一业务标识，与租户、组织共同构成数据库主键。 |
| actor_id            | 是   | 回执所属认证账号。                                     |
| assistant_config_id | 是   | 服务端配置的助手引用，不随模型输出变化。               |
| bvent_id            | 否   | 来源大会引用，空表示非展会场景。                       |
| conversation_id     | 是   | 贯穿 AI、人工、调查及异步任务的会话引用。              |
| delivered_seq       | 是   | 只增不减的接收序列。                                   |
| principal_id        | 是   | 原会话的访客账号，仅用于归属校验。                     |
| read_seq            | 是   | 不超过接收水位的已读序列。                             |
| version             | 是   | 从一递增的乐观并发版本，幂等重放不得递增。             |

## ai_resolution_evidence

Conversation owner 的模型结束候选或确定性用户结束信号，只保存引用及摘要，不复制正文。。Owner：Conversation。

| 字段                | 必填 | 业务含义                                                                             |
| ------------------- | ---- | ------------------------------------------------------------------------------------ |
| tenant_id           | 是   | 服务端确定的租户，参与每次读写条件。                                                 |
| top_org_id          | 是   | 服务端核验的组织，参与每次读写条件。                                                 |
| id                  | 是   | owner 内唯一业务标识，与租户、组织共同构成数据库主键。                               |
| assistant_config_id | 是   | 服务端配置的助手引用，不随模型输出变化。                                             |
| bvent_id            | 否   | 来源大会引用，空表示非展会场景。                                                     |
| confidence          | 否   | 模型候选置信度，用户确定性信号为空。                                                 |
| conversation_id     | 是   | 贯穿 AI、人工、调查及异步任务的会话引用。                                            |
| created_at          | 是   | 首次记录 UTC 时间，同参数重放不改写。                                                |
| epoch               | 是   | 当前 AI 回复代次，后续用户输入和人工接管后不能恢复为当前信号。                       |
| fingerprint         | 是   | 绑定真实引用、代次和参数的摘要，异参数不能覆盖首条证据。                             |
| kind                | 是   | MODEL_POSSIBLE_RESOLUTION、USER_EXPLICIT_END 或 USER_CONFIRMED_NO_FURTHER_QUESTION。 |
| policy              | 是   | 版本化策略来源，模型候选不能代替用户明确结束。                                       |
| principal_id        | 是   | 原会话的访客账号，仅用于归属校验。                                                   |
| trace_id            | 是   | 来源轮次的追踪标识，跨恢复任务保持。                                                 |
| turn_id             | 是   | 证据来自的实际成功轮次。                                                             |
| version             | 是   | 从一递增的乐观并发版本，幂等重放不得递增。                                           |

## ai_resolution_survey

Conversation owner 的单次满意度事实，人工结束不会创建调查。。Owner：Conversation。

| 字段                | 必填 | 业务含义                                                             |
| ------------------- | ---- | -------------------------------------------------------------------- |
| tenant_id           | 是   | 服务端确定的租户，参与每次读写条件。                                 |
| top_org_id          | 是   | 服务端核验的组织，参与每次读写条件。                                 |
| id                  | 是   | owner 内唯一业务标识，与租户、组织共同构成数据库主键。               |
| assistant_config_id | 是   | 服务端配置的助手引用，不随模型输出变化。                             |
| bvent_id            | 否   | 来源大会引用，空表示非展会场景。                                     |
| conversation_id     | 是   | 贯穿 AI、人工、调查及异步任务的会话引用。                            |
| displayed_at        | 否   | 实际展示回执时间，查询不能代填。                                     |
| epoch               | 是   | 创建时的 AI 回复代次，新用户轮次或人工接管后禁止首次评价和人工选择。 |
| evidence            | 是   | 结束证据类型和策略版本，不复制正文。                                 |
| expires_at          | 是   | 调查 UTC 失效时间。                                                  |
| handoff_choice      | 是   | 不满意后独立的人工选择。                                             |
| opened_at           | 是   | 通过真实结束证据门禁并创建调查的 UTC 时间，用于固定窗口对账。        |
| principal_id        | 是   | 原会话的访客账号，仅用于归属校验。                                   |
| responded_at        | 否   | 首次明确回答 UTC 时间。                                              |
| source_turn_id      | 是   | 用户结束证据所在成功轮次。                                           |
| status              | 是   | 单次评价状态。                                                       |
| version             | 是   | 从一递增的乐观并发版本，幂等重放不得递增。                           |

## ai_turn

真实 AI 轮次及成功证据，失败回答不得进入调查资格。。Owner：Conversation。

| 字段                | 必填 | 业务含义                                               |
| ------------------- | ---- | ------------------------------------------------------ |
| tenant_id           | 是   | 服务端确定的租户，参与每次读写条件。                   |
| top_org_id          | 是   | 服务端核验的组织，参与每次读写条件。                   |
| id                  | 是   | owner 内唯一业务标识，与租户、组织共同构成数据库主键。 |
| assistant_config_id | 是   | 服务端配置的助手引用，不随模型输出变化。               |
| assistant_content   | 否   | 模型完整成功的展示文本。                               |
| bvent_id            | 否   | 来源大会引用，空表示非展会场景。                       |
| completed_at        | 否   | 完成或失败 UTC 时间。                                  |
| conversation_id     | 是   | 贯穿 AI、人工、调查及异步任务的会话引用。              |
| created_at          | 是   | 生成开始 UTC 时间。                                    |
| epoch               | 是   | 开始时回复代次，完成时必须匹配。                       |
| principal_id        | 是   | 原会话的访客账号，仅用于归属校验。                     |
| status              | 是   | RUNNING、SUCCEEDED、FAILED 或 FENCED。                 |
| trace_id            | 是   | 传播至模型和故障任务的关联标识。                       |
| user_content        | 是   | 本轮访客原始纯文本。                                   |
| version             | 是   | 从一递增的乐观并发版本，幂等重放不得递增。             |

## cs_agent_availability

组织级客服在线代次和租约，不授予成员资格。。Owner：CustomerService。

| 字段       | 必填 | 业务含义                                               |
| ---------- | ---- | ------------------------------------------------------ |
| tenant_id  | 是   | 服务端确定的租户，参与每次读写条件。                   |
| top_org_id | 是   | 服务端核验的组织，参与每次读写条件。                   |
| id         | 是   | owner 内唯一业务标识，与租户、组织共同构成数据库主键。 |
| expires_at | 否   | ONLINE 心跳 UTC 截止，旧代次不能续约。                 |
| status     | 是   | 明确选择的展示状态。                                   |
| version    | 是   | 从一递增的乐观并发版本，幂等重放不得递增。             |

## cs_command_receipt

不可变命令回执，同键重试仍需复核权限。。Owner：CustomerService。

| 字段                | 必填 | 业务含义                                               |
| ------------------- | ---- | ------------------------------------------------------ |
| tenant_id           | 是   | 服务端确定的租户，参与每次读写条件。                   |
| top_org_id          | 是   | 服务端核验的组织，参与每次读写条件。                   |
| id                  | 是   | owner 内唯一业务标识，与租户、组织共同构成数据库主键。 |
| action              | 是   | 明确的业务动作。                                       |
| actor_id            | 是   | 首次命令主体。                                         |
| assistant_config_id | 是   | 服务端配置的助手引用，不随模型输出变化。               |
| bvent_id            | 否   | 来源大会引用，空表示非展会场景。                       |
| conversation_id     | 是   | 贯穿 AI、人工、调查及异步任务的会话引用。              |
| created_at          | 是   | 首次成功命令的 UTC 审计时间，重放不更新。              |
| fingerprint         | 是   | 动作和原输入 SHA256 摘要。                             |
| principal_id        | 是   | 原会话的访客账号，仅用于归属校验。                     |
| request_id          | 是   | 首次请求幂等键。                                       |
| result_json         | 是   | 首次成功的安全响应投影。                               |
| trace_id            | 是   | 首次成功命令的服务端追踪标识。                         |
| version             | 是   | 从一递增的乐观并发版本，幂等重放不得递增。             |

## cs_followup_form

原周期的留言表单及认领事实，回复另有生命周期。。Owner：CustomerService。

| 字段                | 必填 | 业务含义                                                            |
| ------------------- | ---- | ------------------------------------------------------------------- |
| tenant_id           | 是   | 服务端确定的租户，参与每次读写条件。                                |
| top_org_id          | 是   | 服务端核验的组织，参与每次读写条件。                                |
| id                  | 是   | owner 内唯一业务标识，与租户、组织共同构成数据库主键。              |
| assigned_to         | 否   | 已认领客服，未认领为空。                                            |
| assistant_config_id | 是   | 服务端配置的助手引用，不随模型输出变化。                            |
| bvent_id            | 否   | 来源大会引用，空表示非展会场景。                                    |
| claimed_at          | 否   | 首次认领 UTC 时间。                                                 |
| contact_name        | 否   | 可空称呼，最多 100 字符。                                           |
| content             | 是   | 主动留言为 1 至 4000 字符；仅邮箱跟进时为空字符串。                 |
| conversation_id     | 是   | 贯穿 AI、人工、调查及异步任务的会话引用。                           |
| episode_id          | 是   | 固定原人工周期。                                                    |
| fingerprint         | 是   | 原留言及邮箱摘要，拒绝异内容重放。                                  |
| group_id            | 是   | 原申请冻结的接待组。                                                |
| origin              | 是   | FORM 表示访客主动留言，EMAIL 表示仅选择邮箱跟进，不能伪造访客正文。 |
| principal_id        | 是   | 原会话的访客账号，仅用于归属校验。                                  |
| request_id          | 是   | 首次留言请求键。                                                    |
| state               | 是   | OPEN 待认领或 CLAIMED 已认领。                                      |
| submitted_at        | 是   | 首次提交 UTC 时间。                                                 |
| ticket_id           | 是   | 等待 owner 的工单号。                                               |
| version             | 是   | 从一递增的乐观并发版本，幂等重放不得递增。                          |

## cs_followup_response

客服最终邮件回复，内容与可靠任务原子保存。。Owner：CustomerService。

| 字段                | 必填 | 业务含义                                               |
| ------------------- | ---- | ------------------------------------------------------ |
| tenant_id           | 是   | 服务端确定的租户，参与每次读写条件。                   |
| top_org_id          | 是   | 服务端核验的组织，参与每次读写条件。                   |
| id                  | 是   | owner 内唯一业务标识，与租户、组织共同构成数据库主键。 |
| actor_id            | 是   | 首次回复的认领客服。                                   |
| assistant_config_id | 是   | 服务端配置的助手引用，不随模型输出变化。               |
| bvent_id            | 否   | 来源大会引用，空表示非展会场景。                       |
| content             | 是   | 最多 10000 字符的最终纯文本回复。                      |
| conversation_id     | 是   | 贯穿 AI、人工、调查及异步任务的会话引用。              |
| created_at          | 是   | 首次回复 UTC 时间。                                    |
| episode_id          | 是   | 固定原周期，不能被新申请替换。                         |
| principal_id        | 是   | 原会话的访客账号，仅用于归属校验。                     |
| state               | 是   | 受理、失败及显式结案状态。                             |
| subject             | 是   | 最多 200 字符，禁止换行。                              |
| ticket_id           | 是   | 唯一原工单引用。                                       |
| version             | 是   | 从一递增的乐观并发版本，幂等重放不得递增。             |

## cs_handoff_episode

客服 owner 的一次人工申请及冻结路由，不复制账号主数据。。Owner：CustomerService。

| 字段                  | 必填 | 业务含义                                               |
| --------------------- | ---- | ------------------------------------------------------ |
| tenant_id             | 是   | 服务端确定的租户，参与每次读写条件。                   |
| top_org_id            | 是   | 服务端核验的组织，参与每次读写条件。                   |
| id                    | 是   | owner 内唯一业务标识，与租户、组织共同构成数据库主键。 |
| agent_id              | 否   | 真实认领客服账号。                                     |
| assistant_config_id   | 是   | 服务端配置的助手引用，不随模型输出变化。               |
| bvent_id              | 否   | 来源大会引用，空表示非展会场景。                       |
| category              | 是   | 申请时冻结的咨询主题。                                 |
| claimed_at            | 否   | 首次认领 UTC 时间。                                    |
| classification_source | 是   | MODEL 或明确的降级原因。                               |
| config_version        | 是   | 申请时的路由配置版本。                                 |
| conversation_id       | 是   | 贯穿 AI、人工、调查及异步任务的会话引用。              |
| finished_at           | 否   | 取消或结束 UTC 时间。                                  |
| first_response_at     | 否   | 客服首条有效消息 UTC 时间。                            |
| group_id              | 是   | 申请时冻结的本地业务组。                               |
| principal_id          | 是   | 原会话的访客账号，仅用于归属校验。                     |
| reason_code           | 是   | 首次业务原因，与触发来源分别保存。                     |
| requested_at          | 是   | 首次申请 UTC 时间。                                    |
| status                | 是   | 接待状态，禁止客户端直接指定。                         |
| trace_id              | 是   | 首次申请跨任务追踪标识。                               |
| trigger_source        | 是   | 首次确定性来源，改派不得覆盖。                         |
| version               | 是   | 从一递增的乐观并发版本，幂等重放不得递增。             |

## cs_handoff_proposal

受限 AI 工具建议卡，访客确认后才创建人工周期。。Owner：CustomerService。

| 字段                | 必填 | 业务含义                                               |
| ------------------- | ---- | ------------------------------------------------------ |
| tenant_id           | 是   | 服务端确定的租户，参与每次读写条件。                   |
| top_org_id          | 是   | 服务端核验的组织，参与每次读写条件。                   |
| id                  | 是   | owner 内唯一业务标识，与租户、组织共同构成数据库主键。 |
| assistant_config_id | 是   | 服务端配置的助手引用，不随模型输出变化。               |
| bvent_id            | 否   | 来源大会引用，空表示非展会场景。                       |
| confidence          | 是   | 模型置信度，范围 0 至 1。                              |
| conversation_id     | 是   | 贯穿 AI、人工、调查及异步任务的会话引用。              |
| created_at          | 是   | 建议创建 UTC 时间。                                    |
| epoch               | 是   | 建议时的回复代次，确认时必须匹配。                     |
| expires_at          | 是   | 服务端 UTC 失效时间。                                  |
| principal_id        | 是   | 原会话的访客账号，仅用于归属校验。                     |
| reason_code         | 是   | 白名单内的模型建议原因。                               |
| status              | 是   | 单次决策状态。                                         |
| turn_id             | 是   | 服务端绑定的 AI 轮次。                                 |
| user_facing_reason  | 是   | 有界可展示说明，不允许可执行 URL。                     |
| version             | 是   | 从一递增的乐观并发版本，幂等重放不得递增。             |

## cs_organization_gate

基础设施组织写入门闩，跨进程串行容量与本地撤权，不保存业务主数据。。Owner：Infrastructure。

| 字段       | 必填 | 业务含义                                                |
| ---------- | ---- | ------------------------------------------------------- |
| tenant_id  | 是   | 认证租户，复合主键组成部分。                            |
| top_org_id | 是   | 核验组织，复合主键组成部分。                            |
| version    | 是   | 事务持有期间原子递增，用 ORM 类型化更新获取数据库写锁。 |

## cs_reception_calendar

组织级营业日历，只影响新等待周期，既有 UTC 期限保持冻结。。Owner：CustomerService。

| 字段             | 必填 | 业务含义                                               |
| ---------------- | ---- | ------------------------------------------------------ |
| tenant_id        | 是   | 服务端确定的租户，参与每次读写条件。                   |
| top_org_id       | 是   | 服务端核验的组织，参与每次读写条件。                   |
| id               | 是   | owner 内唯一业务标识，与租户、组织共同构成数据库主键。 |
| calendar_version | 是   | 业务规则版本，改变时区、时间或日期覆盖时必须升级。     |
| closes           | 是   | 同日营业结束，不含边界，默认 17:00。                   |
| opens            | 是   | 同日营业开始，含边界，默认 10:00。                     |
| overrides        | 是   | ISO 日期到开闭班覆盖，最多 1000 项，优先于工作日规则。 |
| version          | 是   | 从一递增的乐观并发版本，幂等重放不得递增。             |
| zone             | 是   | IANA 时区，默认中国标准时间。                          |

## cs_reception_configuration

客服 owner 的组织级分组、成员关联和助手路由，整体版本提交。。Owner：CustomerService。

| 字段       | 必填 | 业务含义                                               |
| ---------- | ---- | ------------------------------------------------------ |
| tenant_id  | 是   | 服务端确定的租户，参与每次读写条件。                   |
| top_org_id | 是   | 服务端核验的组织，参与每次读写条件。                   |
| id         | 是   | owner 内唯一业务标识，与租户、组织共同构成数据库主键。 |
| groups     | 是   | 四类本地接待组，账号只保存用户中心引用。               |
| routes     | 是   | 每个助手的四类主题到本地组的映射。                     |
| version    | 是   | 从一递增的乐观并发版本，幂等重放不得递增。             |

## cs_reliable_task

带租约退避死信的副作用，核心事务只登记任务。。Owner：CustomerService。

| 字段                | 必填 | 业务含义                                                |
| ------------------- | ---- | ------------------------------------------------------- |
| tenant_id           | 是   | 服务端确定的租户，参与每次读写条件。                    |
| top_org_id          | 是   | 服务端核验的组织，参与每次读写条件。                    |
| id                  | 是   | owner 内唯一业务标识，与租户、组织共同构成数据库主键。  |
| assistant_config_id | 是   | 服务端配置的助手引用，不随模型输出变化。                |
| attempts            | 是   | 当前人工恢复周期领取次数，上限五次。                    |
| bvent_id            | 否   | 来源大会引用，空表示非展会场景。                        |
| conversation_id     | 是   | 贯穿 AI、人工、调查及异步任务的会话引用。               |
| created_at          | 是   | 任务登记 UTC 时间。                                     |
| due_at              | 是   | 下次可领取 UTC 时间。                                   |
| episode_id          | 是   | 原周期；系统任务登记时可为空。                          |
| event_key           | 是   | Worker 与网关共用的稳定幂等键。                         |
| kind                | 是   | INTERNAL、EMAIL、EMAIL_REPLY、SYSTEM_HANDOFF 或 EVENT。 |
| last_error          | 否   | 不含正文邮箱的稳定失败分类。                            |
| last_retried_at     | 否   | 最近一次人工恢复的 UTC 时间，初次任务为空。             |
| last_retried_by     | 否   | 最近一次人工恢复的账号引用，自动重试不覆盖。            |
| lease_id            | 否   | 本次随机租约，完成时需匹配。                            |
| lease_until         | 否   | 租约 UTC 截止，崩溃后可恢复。                           |
| manual_retry_count  | 是   | 正式客服人工恢复预算的累计次数，仅递增。                |
| payload             | 是   | 受控 JSON 业务载荷，不含认证密钥。                      |
| principal_id        | 是   | 原会话的访客账号，仅用于归属校验。                      |
| state               | 是   | 可靠任务集中状态。                                      |
| total_attempts      | 是   | 包括历次人工恢复周期在内的累计领取次数，仅递增。        |
| trace_id            | 是   | 原始追踪标识，跨 Worker 保持。                          |
| version             | 是   | 从一递增的乐观并发版本，幂等重放不得递增。              |

## cs_waiting_workflow

独立的等待和联系方式聚合，期限在初始化时冻结且只按需推进。。Owner：CustomerService。

| 字段                | 必填 | 业务含义                                               |
| ------------------- | ---- | ------------------------------------------------------ |
| tenant_id           | 是   | 服务端确定的租户，参与每次读写条件。                   |
| top_org_id          | 是   | 服务端核验的组织，参与每次读写条件。                   |
| id                  | 是   | owner 内唯一业务标识，与租户、组织共同构成数据库主键。 |
| assistant_config_id | 是   | 服务端配置的助手引用，不随模型输出变化。               |
| busy_at             | 是   | T+10 用户提示 UTC 期限。                               |
| bvent_id            | 否   | 来源大会引用，空表示非展会场景。                       |
| calendar_version    | 是   | 期限的业务日历版本。                                   |
| closes_at           | 是   | 冻结的营业结束 UTC 时间。                              |
| conversation_id     | 是   | 贯穿 AI、人工、调查及异步任务的会话引用。              |
| email               | 否   | 访客明确提交的邮箱，仅客服 owner 保存。                |
| episode_id          | 是   | 唯一对应的原人工周期。                                 |
| next_opens_at       | 是   | 冻结的下次营业 UTC 时间。                              |
| principal_id        | 是   | 原会话的访客账号，仅用于归属校验。                     |
| remind_at           | 是   | T+5 内部提醒 UTC 期限。                                |
| reminded            | 是   | 是否登记提醒，不表示实际送达。                         |
| state               | 是   | 独立等待工作流状态。                                   |
| ticket_id           | 否   | 唯一跟进工单号，不代表邮件已发送。                     |
| version             | 是   | 从一递增的乐观并发版本，幂等重放不得递增。             |
