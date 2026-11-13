-- 目的：AI.Customer.Service 首次部署独立数据库；方言：Sqlite。
-- 前提：全新空库、独占建库权限、已备份目标；禁止在 Java 原业务库或已有 .NET 库执行。
-- 兼容：结构与 Java 原库不同，不包含历史数据搬迁；Kingbase 要求 PostgreSQL 兼容模式。
-- 回滚：失败时回滚 DDL 事务（数据库支持时）；否则仅清理本次新建的独立空库后重新部署。
-- 隔离：tenant_id 来自签名身份，top_org_id 来自可信助手/用户中心；复合主键和 FK 同时约束归属。
-- 约束：NOT NULL 禁止缺失必需事实，UNIQUE 保证幂等，CHECK 约束状态/租约/版本，FK 禁止跨组织引用。
-- 删除：全部物理保留，不做逻辑删除；按组织停写后依赖顺序归档清理，不删除未终结工单或待投递任务。
-- 表 ai_conversation_generation：Conversation owner 的回复代次和当前轮次，人工与 AI 共用同一会话。；数据 owner：Conversation；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 ai_conversation_generation.tenant_id：服务端确定的租户，参与每次读写条件。；非空，业务必需。
-- 字段 ai_conversation_generation.top_org_id：服务端核验的组织，参与每次读写条件。；非空，业务必需。
-- 字段 ai_conversation_generation.id：owner 内唯一业务标识，与租户、组织共同构成数据库主键。；非空，业务必需。
-- 字段 ai_conversation_generation.assistant_config_id：服务端配置的助手引用，不随模型输出变化。；非空，业务必需。
-- 字段 ai_conversation_generation.bvent_id：来源大会引用，空表示非展会场景。；可空。
-- 字段 ai_conversation_generation.conversation_id：贯穿 AI、人工、调查及异步任务的会话引用。；非空，业务必需。
-- 字段 ai_conversation_generation.created_at：首次创建 UTC 时间。；非空，业务必需。
-- 字段 ai_conversation_generation.current_turn_id：正在生成的 AI 轮次，拒绝并发轮次及迟到响应。；可空。
-- 字段 ai_conversation_generation.epoch：回复代次，每次人工接管或恢复均递增。；非空，业务必需。
-- 字段 ai_conversation_generation.human_episode_id：当前独占回复权的人工周期，空表示允许 AI。；可空。
-- 字段 ai_conversation_generation.last_completed_turn_id：最近完整成功轮次，调查恢复 Worker 仅处理尚未评估的新成功轮次。；可空。
-- 字段 ai_conversation_generation.next_seq：整个会话共享的人工消息最大序列。；非空，业务必需。
-- 字段 ai_conversation_generation.principal_id：原会话的访客账号，仅用于归属校验。；非空，业务必需。
-- 字段 ai_conversation_generation.survey_checked_turn_id：最后已完成资格评估的成功轮次，不根据用户沉默产生新调查。；可空。
-- 字段 ai_conversation_generation.turn_lease_until：AI 生成租约 UTC 截止，崩溃后允许新轮次恢复。；可空。
-- 字段 ai_conversation_generation.updated_at：最后活动 UTC 时间，不作为调查结束证据。；非空，业务必需。
-- 字段 ai_conversation_generation.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 唯一索引 IX_ai_conversation_generation_tenant_id_id：tenant_id, id；用于同归属业务键去重。
-- 表 ai_human_message：Conversation owner 的人工消息，所有周期共享递增序列。；数据 owner：Conversation；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 ai_human_message.tenant_id：服务端确定的租户，参与每次读写条件。；非空，业务必需。
-- 字段 ai_human_message.top_org_id：服务端核验的组织，参与每次读写条件。；非空，业务必需。
-- 字段 ai_human_message.id：owner 内唯一业务标识，与租户、组织共同构成数据库主键。；非空，业务必需。
-- 字段 ai_human_message.assistant_config_id：服务端配置的助手引用，不随模型输出变化。；非空，业务必需。
-- 字段 ai_human_message.bvent_id：来源大会引用，空表示非展会场景。；可空。
-- 字段 ai_human_message.client_msg_id：发送主体下稳定的消息幂等键。；非空，业务必需。
-- 字段 ai_human_message.content：1 至 4000 字符纯文本。；非空，业务必需。
-- 字段 ai_human_message.conversation_id：贯穿 AI、人工、调查及异步任务的会话引用。；非空，业务必需。
-- 字段 ai_human_message.created_at：首次保存 UTC 时间。；非空，业务必需。
-- 字段 ai_human_message.episode_id：发送时的有效周期。；非空，业务必需。
-- 字段 ai_human_message.principal_id：原会话的访客账号，仅用于归属校验。；非空，业务必需。
-- 字段 ai_human_message.sender_id：认证和认领关系确定的发送账号。；非空，业务必需。
-- 字段 ai_human_message.sender_role：只允许 VISITOR 或 AGENT。；非空，业务必需。
-- 字段 ai_human_message.seq：会话内唯一递增序列。；非空，业务必需。
-- 字段 ai_human_message.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 唯一索引 IX_ai_human_message_tenant_id_top_org_id_conversation_id_seq：tenant_id, top_org_id, conversation_id, seq；用于同归属业务键去重。
-- 唯一索引 IX_ai_human_message_tenant_id_top_org_id_conversation_id_sender_id_client_msg_id：tenant_id, top_org_id, conversation_id, sender_id, client_msg_id；用于同归属业务键去重。
-- 表 ai_human_receipt：单一会话主体的接收及已读水位，不能代替对方确认。；数据 owner：Conversation；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 ai_human_receipt.tenant_id：服务端确定的租户，参与每次读写条件。；非空，业务必需。
-- 字段 ai_human_receipt.top_org_id：服务端核验的组织，参与每次读写条件。；非空，业务必需。
-- 字段 ai_human_receipt.id：owner 内唯一业务标识，与租户、组织共同构成数据库主键。；非空，业务必需。
-- 字段 ai_human_receipt.actor_id：回执所属认证账号。；非空，业务必需。
-- 字段 ai_human_receipt.assistant_config_id：服务端配置的助手引用，不随模型输出变化。；非空，业务必需。
-- 字段 ai_human_receipt.bvent_id：来源大会引用，空表示非展会场景。；可空。
-- 字段 ai_human_receipt.conversation_id：贯穿 AI、人工、调查及异步任务的会话引用。；非空，业务必需。
-- 字段 ai_human_receipt.delivered_seq：只增不减的接收序列。；非空，业务必需。
-- 字段 ai_human_receipt.principal_id：原会话的访客账号，仅用于归属校验。；非空，业务必需。
-- 字段 ai_human_receipt.read_seq：不超过接收水位的已读序列。；非空，业务必需。
-- 字段 ai_human_receipt.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 唯一索引 IX_ai_human_receipt_tenant_id_top_org_id_conversation_id_actor_id：tenant_id, top_org_id, conversation_id, actor_id；用于同归属业务键去重。
-- 表 ai_resolution_evidence：Conversation owner 的模型结束候选或确定性用户结束信号，只保存引用及摘要，不复制正文。；数据 owner：Conversation；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 ai_resolution_evidence.tenant_id：服务端确定的租户，参与每次读写条件。；非空，业务必需。
-- 字段 ai_resolution_evidence.top_org_id：服务端核验的组织，参与每次读写条件。；非空，业务必需。
-- 字段 ai_resolution_evidence.id：owner 内唯一业务标识，与租户、组织共同构成数据库主键。；非空，业务必需。
-- 字段 ai_resolution_evidence.assistant_config_id：服务端配置的助手引用，不随模型输出变化。；非空，业务必需。
-- 字段 ai_resolution_evidence.bvent_id：来源大会引用，空表示非展会场景。；可空。
-- 字段 ai_resolution_evidence.confidence：模型候选置信度，用户确定性信号为空。；可空。
-- 字段 ai_resolution_evidence.conversation_id：贯穿 AI、人工、调查及异步任务的会话引用。；非空，业务必需。
-- 字段 ai_resolution_evidence.created_at：首次记录 UTC 时间，同参数重放不改写。；非空，业务必需。
-- 字段 ai_resolution_evidence.epoch：当前 AI 回复代次，后续用户输入和人工接管后不能恢复为当前信号。；非空，业务必需。
-- 字段 ai_resolution_evidence.fingerprint：绑定真实引用、代次和参数的摘要，异参数不能覆盖首条证据。；非空，业务必需。
-- 字段 ai_resolution_evidence.kind：MODEL_POSSIBLE_RESOLUTION、USER_EXPLICIT_END 或 USER_CONFIRMED_NO_FURTHER_QUESTION。；非空，业务必需。
-- 字段 ai_resolution_evidence.policy：版本化策略来源，模型候选不能代替用户明确结束。；非空，业务必需。
-- 字段 ai_resolution_evidence.principal_id：原会话的访客账号，仅用于归属校验。；非空，业务必需。
-- 字段 ai_resolution_evidence.trace_id：来源轮次的追踪标识，跨恢复任务保持。；非空，业务必需。
-- 字段 ai_resolution_evidence.turn_id：证据来自的实际成功轮次。；非空，业务必需。
-- 字段 ai_resolution_evidence.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 唯一索引 IX_ai_resolution_evidence_tenant_id_top_org_id_conversation_id_turn_id_kind：tenant_id, top_org_id, conversation_id, turn_id, kind；用于同归属业务键去重。
-- 表 ai_resolution_survey：Conversation owner 的单次满意度事实，人工结束不会创建调查。；数据 owner：Conversation；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 ai_resolution_survey.tenant_id：服务端确定的租户，参与每次读写条件。；非空，业务必需。
-- 字段 ai_resolution_survey.top_org_id：服务端核验的组织，参与每次读写条件。；非空，业务必需。
-- 字段 ai_resolution_survey.id：owner 内唯一业务标识，与租户、组织共同构成数据库主键。；非空，业务必需。
-- 字段 ai_resolution_survey.assistant_config_id：服务端配置的助手引用，不随模型输出变化。；非空，业务必需。
-- 字段 ai_resolution_survey.bvent_id：来源大会引用，空表示非展会场景。；可空。
-- 字段 ai_resolution_survey.conversation_id：贯穿 AI、人工、调查及异步任务的会话引用。；非空，业务必需。
-- 字段 ai_resolution_survey.displayed_at：实际展示回执时间，查询不能代填。；可空。
-- 字段 ai_resolution_survey.epoch：创建时的 AI 回复代次，新用户轮次或人工接管后禁止首次评价和人工选择。；非空，业务必需。
-- 字段 ai_resolution_survey.evidence：结束证据类型和策略版本，不复制正文。；非空，业务必需。
-- 字段 ai_resolution_survey.expires_at：调查 UTC 失效时间。；非空，业务必需。
-- 字段 ai_resolution_survey.handoff_choice：不满意后独立的人工选择。；非空，业务必需。
-- 字段 ai_resolution_survey.opened_at：通过真实结束证据门禁并创建调查的 UTC 时间，用于固定窗口对账。；非空，业务必需。
-- 字段 ai_resolution_survey.principal_id：原会话的访客账号，仅用于归属校验。；非空，业务必需。
-- 字段 ai_resolution_survey.responded_at：首次明确回答 UTC 时间。；可空。
-- 字段 ai_resolution_survey.source_turn_id：用户结束证据所在成功轮次。；非空，业务必需。
-- 字段 ai_resolution_survey.status：单次评价状态。；非空，业务必需。
-- 字段 ai_resolution_survey.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 唯一索引 IX_ai_resolution_survey_tenant_id_top_org_id_conversation_id：tenant_id, top_org_id, conversation_id；用于同归属业务键去重。
-- 表 ai_turn：真实 AI 轮次及成功证据，失败回答不得进入调查资格。；数据 owner：Conversation；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 ai_turn.tenant_id：服务端确定的租户，参与每次读写条件。；非空，业务必需。
-- 字段 ai_turn.top_org_id：服务端核验的组织，参与每次读写条件。；非空，业务必需。
-- 字段 ai_turn.id：owner 内唯一业务标识，与租户、组织共同构成数据库主键。；非空，业务必需。
-- 字段 ai_turn.assistant_config_id：服务端配置的助手引用，不随模型输出变化。；非空，业务必需。
-- 字段 ai_turn.assistant_content：模型完整成功的展示文本。；可空。
-- 字段 ai_turn.bvent_id：来源大会引用，空表示非展会场景。；可空。
-- 字段 ai_turn.completed_at：完成或失败 UTC 时间。；可空。
-- 字段 ai_turn.conversation_id：贯穿 AI、人工、调查及异步任务的会话引用。；非空，业务必需。
-- 字段 ai_turn.created_at：生成开始 UTC 时间。；非空，业务必需。
-- 字段 ai_turn.epoch：开始时回复代次，完成时必须匹配。；非空，业务必需。
-- 字段 ai_turn.principal_id：原会话的访客账号，仅用于归属校验。；非空，业务必需。
-- 字段 ai_turn.status：RUNNING、SUCCEEDED、FAILED 或 FENCED。；非空，业务必需。
-- 字段 ai_turn.trace_id：传播至模型和故障任务的关联标识。；非空，业务必需。
-- 字段 ai_turn.user_content：本轮访客原始纯文本。；非空，业务必需。
-- 字段 ai_turn.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 查询索引 IX_ai_turn_tenant_id_top_org_id_conversation_id：tenant_id, top_org_id, conversation_id；用于组织队列或 Worker 有界恢复。
-- 表 cs_agent_availability：组织级客服在线代次和租约，不授予成员资格。；数据 owner：CustomerService；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 cs_agent_availability.tenant_id：服务端确定的租户，参与每次读写条件。；非空，业务必需。
-- 字段 cs_agent_availability.top_org_id：服务端核验的组织，参与每次读写条件。；非空，业务必需。
-- 字段 cs_agent_availability.id：owner 内唯一业务标识，与租户、组织共同构成数据库主键。；非空，业务必需。
-- 字段 cs_agent_availability.expires_at：ONLINE 心跳 UTC 截止，旧代次不能续约。；可空。
-- 字段 cs_agent_availability.status：明确选择的展示状态。；非空，业务必需。
-- 字段 cs_agent_availability.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 表 cs_command_receipt：不可变命令回执，同键重试仍需复核权限。；数据 owner：CustomerService；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 cs_command_receipt.tenant_id：服务端确定的租户，参与每次读写条件。；非空，业务必需。
-- 字段 cs_command_receipt.top_org_id：服务端核验的组织，参与每次读写条件。；非空，业务必需。
-- 字段 cs_command_receipt.id：owner 内唯一业务标识，与租户、组织共同构成数据库主键。；非空，业务必需。
-- 字段 cs_command_receipt.action：明确的业务动作。；非空，业务必需。
-- 字段 cs_command_receipt.actor_id：首次命令主体。；非空，业务必需。
-- 字段 cs_command_receipt.assistant_config_id：服务端配置的助手引用，不随模型输出变化。；非空，业务必需。
-- 字段 cs_command_receipt.bvent_id：来源大会引用，空表示非展会场景。；可空。
-- 字段 cs_command_receipt.conversation_id：贯穿 AI、人工、调查及异步任务的会话引用。；非空，业务必需。
-- 字段 cs_command_receipt.created_at：首次成功命令的 UTC 审计时间，重放不更新。；非空，业务必需。
-- 字段 cs_command_receipt.fingerprint：动作和原输入 SHA256 摘要。；非空，业务必需。
-- 字段 cs_command_receipt.principal_id：原会话的访客账号，仅用于归属校验。；非空，业务必需。
-- 字段 cs_command_receipt.request_id：首次请求幂等键。；非空，业务必需。
-- 字段 cs_command_receipt.result_json：首次成功的安全响应投影。；非空，业务必需。
-- 字段 cs_command_receipt.trace_id：首次成功命令的服务端追踪标识。；非空，业务必需。
-- 字段 cs_command_receipt.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 唯一索引 IX_cs_command_receipt_tenant_id_top_org_id_conversation_id_actor_id_action_request_id：tenant_id, top_org_id, conversation_id, actor_id, action, request_id；用于同归属业务键去重。
-- 表 cs_followup_form：原周期的留言表单及认领事实，回复另有生命周期。；数据 owner：CustomerService；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 cs_followup_form.tenant_id：服务端确定的租户，参与每次读写条件。；非空，业务必需。
-- 字段 cs_followup_form.top_org_id：服务端核验的组织，参与每次读写条件。；非空，业务必需。
-- 字段 cs_followup_form.id：owner 内唯一业务标识，与租户、组织共同构成数据库主键。；非空，业务必需。
-- 字段 cs_followup_form.assigned_to：已认领客服，未认领为空。；可空。
-- 字段 cs_followup_form.assistant_config_id：服务端配置的助手引用，不随模型输出变化。；非空，业务必需。
-- 字段 cs_followup_form.bvent_id：来源大会引用，空表示非展会场景。；可空。
-- 字段 cs_followup_form.claimed_at：首次认领 UTC 时间。；可空。
-- 字段 cs_followup_form.contact_name：可空称呼，最多 100 字符。；可空。
-- 字段 cs_followup_form.content：主动留言为 1 至 4000 字符；仅邮箱跟进时为空字符串。；非空，业务必需。
-- 字段 cs_followup_form.conversation_id：贯穿 AI、人工、调查及异步任务的会话引用。；非空，业务必需。
-- 字段 cs_followup_form.episode_id：固定原人工周期。；非空，业务必需。
-- 字段 cs_followup_form.fingerprint：原留言及邮箱摘要，拒绝异内容重放。；非空，业务必需。
-- 字段 cs_followup_form.group_id：原申请冻结的接待组。；非空，业务必需。
-- 字段 cs_followup_form.origin：FORM 表示访客主动留言，EMAIL 表示仅选择邮箱跟进，不能伪造访客正文。；非空，业务必需。
-- 字段 cs_followup_form.principal_id：原会话的访客账号，仅用于归属校验。；非空，业务必需。
-- 字段 cs_followup_form.request_id：首次留言请求键。；非空，业务必需。
-- 字段 cs_followup_form.state：OPEN 待认领或 CLAIMED 已认领。；非空，业务必需。
-- 字段 cs_followup_form.submitted_at：首次提交 UTC 时间。；非空，业务必需。
-- 字段 cs_followup_form.ticket_id：等待 owner 的工单号。；非空，业务必需。
-- 字段 cs_followup_form.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 查询索引 IX_cs_followup_form_tenant_id_top_org_id_conversation_id：tenant_id, top_org_id, conversation_id；用于组织队列或 Worker 有界恢复。
-- 唯一索引 IX_cs_followup_form_tenant_id_top_org_id_episode_id：tenant_id, top_org_id, episode_id；用于同归属业务键去重。
-- 表 cs_followup_response：客服最终邮件回复，内容与可靠任务原子保存。；数据 owner：CustomerService；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 cs_followup_response.tenant_id：服务端确定的租户，参与每次读写条件。；非空，业务必需。
-- 字段 cs_followup_response.top_org_id：服务端核验的组织，参与每次读写条件。；非空，业务必需。
-- 字段 cs_followup_response.id：owner 内唯一业务标识，与租户、组织共同构成数据库主键。；非空，业务必需。
-- 字段 cs_followup_response.actor_id：首次回复的认领客服。；非空，业务必需。
-- 字段 cs_followup_response.assistant_config_id：服务端配置的助手引用，不随模型输出变化。；非空，业务必需。
-- 字段 cs_followup_response.bvent_id：来源大会引用，空表示非展会场景。；可空。
-- 字段 cs_followup_response.content：最多 10000 字符的最终纯文本回复。；非空，业务必需。
-- 字段 cs_followup_response.conversation_id：贯穿 AI、人工、调查及异步任务的会话引用。；非空，业务必需。
-- 字段 cs_followup_response.created_at：首次回复 UTC 时间。；非空，业务必需。
-- 字段 cs_followup_response.episode_id：固定原周期，不能被新申请替换。；非空，业务必需。
-- 字段 cs_followup_response.principal_id：原会话的访客账号，仅用于归属校验。；非空，业务必需。
-- 字段 cs_followup_response.state：受理、失败及显式结案状态。；非空，业务必需。
-- 字段 cs_followup_response.subject：最多 200 字符，禁止换行。；非空，业务必需。
-- 字段 cs_followup_response.ticket_id：唯一原工单引用。；非空，业务必需。
-- 字段 cs_followup_response.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 查询索引 IX_cs_followup_response_tenant_id_top_org_id_conversation_id：tenant_id, top_org_id, conversation_id；用于组织队列或 Worker 有界恢复。
-- 唯一索引 IX_cs_followup_response_tenant_id_top_org_id_episode_id：tenant_id, top_org_id, episode_id；用于同归属业务键去重。
-- 表 cs_handoff_episode：客服 owner 的一次人工申请及冻结路由，不复制账号主数据。；数据 owner：CustomerService；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 cs_handoff_episode.tenant_id：服务端确定的租户，参与每次读写条件。；非空，业务必需。
-- 字段 cs_handoff_episode.top_org_id：服务端核验的组织，参与每次读写条件。；非空，业务必需。
-- 字段 cs_handoff_episode.id：owner 内唯一业务标识，与租户、组织共同构成数据库主键。；非空，业务必需。
-- 字段 cs_handoff_episode.agent_id：真实认领客服账号。；可空。
-- 字段 cs_handoff_episode.assistant_config_id：服务端配置的助手引用，不随模型输出变化。；非空，业务必需。
-- 字段 cs_handoff_episode.bvent_id：来源大会引用，空表示非展会场景。；可空。
-- 字段 cs_handoff_episode.category：申请时冻结的咨询主题。；非空，业务必需。
-- 字段 cs_handoff_episode.claimed_at：首次认领 UTC 时间。；可空。
-- 字段 cs_handoff_episode.classification_source：MODEL 或明确的降级原因。；非空，业务必需。
-- 字段 cs_handoff_episode.config_version：申请时的路由配置版本。；非空，业务必需。
-- 字段 cs_handoff_episode.conversation_id：贯穿 AI、人工、调查及异步任务的会话引用。；非空，业务必需。
-- 字段 cs_handoff_episode.finished_at：取消或结束 UTC 时间。；可空。
-- 字段 cs_handoff_episode.first_response_at：客服首条有效消息 UTC 时间。；可空。
-- 字段 cs_handoff_episode.group_id：申请时冻结的本地业务组。；非空，业务必需。
-- 字段 cs_handoff_episode.principal_id：原会话的访客账号，仅用于归属校验。；非空，业务必需。
-- 字段 cs_handoff_episode.reason_code：首次业务原因，与触发来源分别保存。；非空，业务必需。
-- 字段 cs_handoff_episode.requested_at：首次申请 UTC 时间。；非空，业务必需。
-- 字段 cs_handoff_episode.status：接待状态，禁止客户端直接指定。；非空，业务必需。
-- 字段 cs_handoff_episode.trace_id：首次申请跨任务追踪标识。；非空，业务必需。
-- 字段 cs_handoff_episode.trigger_source：首次确定性来源，改派不得覆盖。；非空，业务必需。
-- 字段 cs_handoff_episode.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 查询索引 IX_cs_handoff_episode_tenant_id_top_org_id_conversation_id：tenant_id, top_org_id, conversation_id；用于组织队列或 Worker 有界恢复。
-- 查询索引 IX_cs_handoff_episode_tenant_id_top_org_id_assistant_config_id_status：tenant_id, top_org_id, assistant_config_id, status；用于组织队列或 Worker 有界恢复。
-- 表 cs_handoff_proposal：受限 AI 工具建议卡，访客确认后才创建人工周期。；数据 owner：CustomerService；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 cs_handoff_proposal.tenant_id：服务端确定的租户，参与每次读写条件。；非空，业务必需。
-- 字段 cs_handoff_proposal.top_org_id：服务端核验的组织，参与每次读写条件。；非空，业务必需。
-- 字段 cs_handoff_proposal.id：owner 内唯一业务标识，与租户、组织共同构成数据库主键。；非空，业务必需。
-- 字段 cs_handoff_proposal.assistant_config_id：服务端配置的助手引用，不随模型输出变化。；非空，业务必需。
-- 字段 cs_handoff_proposal.bvent_id：来源大会引用，空表示非展会场景。；可空。
-- 字段 cs_handoff_proposal.confidence：模型置信度，范围 0 至 1。；非空，业务必需。
-- 字段 cs_handoff_proposal.conversation_id：贯穿 AI、人工、调查及异步任务的会话引用。；非空，业务必需。
-- 字段 cs_handoff_proposal.created_at：建议创建 UTC 时间。；非空，业务必需。
-- 字段 cs_handoff_proposal.epoch：建议时的回复代次，确认时必须匹配。；非空，业务必需。
-- 字段 cs_handoff_proposal.expires_at：服务端 UTC 失效时间。；非空，业务必需。
-- 字段 cs_handoff_proposal.principal_id：原会话的访客账号，仅用于归属校验。；非空，业务必需。
-- 字段 cs_handoff_proposal.reason_code：白名单内的模型建议原因。；非空，业务必需。
-- 字段 cs_handoff_proposal.status：单次决策状态。；非空，业务必需。
-- 字段 cs_handoff_proposal.turn_id：服务端绑定的 AI 轮次。；非空，业务必需。
-- 字段 cs_handoff_proposal.user_facing_reason：有界可展示说明，不允许可执行 URL。；非空，业务必需。
-- 字段 cs_handoff_proposal.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 查询索引 IX_cs_handoff_proposal_tenant_id_top_org_id_conversation_id：tenant_id, top_org_id, conversation_id；用于组织队列或 Worker 有界恢复。
-- 表 cs_organization_gate：基础设施组织写入门闩，跨进程串行容量与本地撤权，不保存业务主数据。；数据 owner：Infrastructure；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 cs_organization_gate.tenant_id：认证租户，复合主键组成部分。；非空，业务必需。
-- 字段 cs_organization_gate.top_org_id：核验组织，复合主键组成部分。；非空，业务必需。
-- 字段 cs_organization_gate.version：事务持有期间原子递增，用 ORM 类型化更新获取数据库写锁。；非空，业务必需。
-- 表 cs_reception_calendar：组织级营业日历，只影响新等待周期，既有 UTC 期限保持冻结。；数据 owner：CustomerService；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 cs_reception_calendar.tenant_id：服务端确定的租户，参与每次读写条件。；非空，业务必需。
-- 字段 cs_reception_calendar.top_org_id：服务端核验的组织，参与每次读写条件。；非空，业务必需。
-- 字段 cs_reception_calendar.id：owner 内唯一业务标识，与租户、组织共同构成数据库主键。；非空，业务必需。
-- 字段 cs_reception_calendar.calendar_version：业务规则版本，改变时区、时间或日期覆盖时必须升级。；非空，业务必需。
-- 字段 cs_reception_calendar.closes：同日营业结束，不含边界，默认 17:00。；非空，业务必需。
-- 字段 cs_reception_calendar.opens：同日营业开始，含边界，默认 10:00。；非空，业务必需。
-- 字段 cs_reception_calendar.overrides：ISO 日期到开闭班覆盖，最多 1000 项，优先于工作日规则。；非空，业务必需。
-- 字段 cs_reception_calendar.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 字段 cs_reception_calendar.zone：IANA 时区，默认中国标准时间。；非空，业务必需。
-- 唯一索引 IX_cs_reception_calendar_tenant_id_top_org_id：tenant_id, top_org_id；用于同归属业务键去重。
-- 表 cs_reception_configuration：客服 owner 的组织级分组、成员关联和助手路由，整体版本提交。；数据 owner：CustomerService；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 cs_reception_configuration.tenant_id：服务端确定的租户，参与每次读写条件。；非空，业务必需。
-- 字段 cs_reception_configuration.top_org_id：服务端核验的组织，参与每次读写条件。；非空，业务必需。
-- 字段 cs_reception_configuration.id：owner 内唯一业务标识，与租户、组织共同构成数据库主键。；非空，业务必需。
-- 字段 cs_reception_configuration.groups：四类本地接待组，账号只保存用户中心引用。；非空，业务必需。
-- 字段 cs_reception_configuration.routes：每个助手的四类主题到本地组的映射。；非空，业务必需。
-- 字段 cs_reception_configuration.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 唯一索引 IX_cs_reception_configuration_tenant_id_top_org_id：tenant_id, top_org_id；用于同归属业务键去重。
-- 表 cs_reliable_task：带租约退避死信的副作用，核心事务只登记任务。；数据 owner：CustomerService；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 cs_reliable_task.tenant_id：服务端确定的租户，参与每次读写条件。；非空，业务必需。
-- 字段 cs_reliable_task.top_org_id：服务端核验的组织，参与每次读写条件。；非空，业务必需。
-- 字段 cs_reliable_task.id：owner 内唯一业务标识，与租户、组织共同构成数据库主键。；非空，业务必需。
-- 字段 cs_reliable_task.assistant_config_id：服务端配置的助手引用，不随模型输出变化。；非空，业务必需。
-- 字段 cs_reliable_task.attempts：当前人工恢复周期领取次数，上限五次。；非空，业务必需。
-- 字段 cs_reliable_task.bvent_id：来源大会引用，空表示非展会场景。；可空。
-- 字段 cs_reliable_task.conversation_id：贯穿 AI、人工、调查及异步任务的会话引用。；非空，业务必需。
-- 字段 cs_reliable_task.created_at：任务登记 UTC 时间。；非空，业务必需。
-- 字段 cs_reliable_task.due_at：下次可领取 UTC 时间。；非空，业务必需。
-- 字段 cs_reliable_task.episode_id：原周期；系统任务登记时可为空。；非空，业务必需。
-- 字段 cs_reliable_task.event_key：Worker 与网关共用的稳定幂等键。；非空，业务必需。
-- 字段 cs_reliable_task.kind：INTERNAL、EMAIL、EMAIL_REPLY、SYSTEM_HANDOFF 或 EVENT。；非空，业务必需。
-- 字段 cs_reliable_task.last_error：不含正文邮箱的稳定失败分类。；可空。
-- 字段 cs_reliable_task.last_retried_at：最近一次人工恢复的 UTC 时间，初次任务为空。；可空。
-- 字段 cs_reliable_task.last_retried_by：最近一次人工恢复的账号引用，自动重试不覆盖。；可空。
-- 字段 cs_reliable_task.lease_id：本次随机租约，完成时需匹配。；可空。
-- 字段 cs_reliable_task.lease_until：租约 UTC 截止，崩溃后可恢复。；可空。
-- 字段 cs_reliable_task.manual_retry_count：正式客服人工恢复预算的累计次数，仅递增。；非空，业务必需。
-- 字段 cs_reliable_task.payload：受控 JSON 业务载荷，不含认证密钥。；非空，业务必需。
-- 字段 cs_reliable_task.principal_id：原会话的访客账号，仅用于归属校验。；非空，业务必需。
-- 字段 cs_reliable_task.state：可靠任务集中状态。；非空，业务必需。
-- 字段 cs_reliable_task.total_attempts：包括历次人工恢复周期在内的累计领取次数，仅递增。；非空，业务必需。
-- 字段 cs_reliable_task.trace_id：原始追踪标识，跨 Worker 保持。；非空，业务必需。
-- 字段 cs_reliable_task.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 查询索引 IX_cs_reliable_task_kind_state_due_at：kind, state, due_at；用于组织队列或 Worker 有界恢复。
-- 查询索引 IX_cs_reliable_task_tenant_id_top_org_id_conversation_id：tenant_id, top_org_id, conversation_id；用于组织队列或 Worker 有界恢复。
-- 唯一索引 IX_cs_reliable_task_tenant_id_top_org_id_event_key：tenant_id, top_org_id, event_key；用于同归属业务键去重。
-- 表 cs_waiting_workflow：独立的等待和联系方式聚合，期限在初始化时冻结且只按需推进。；数据 owner：CustomerService；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 cs_waiting_workflow.tenant_id：服务端确定的租户，参与每次读写条件。；非空，业务必需。
-- 字段 cs_waiting_workflow.top_org_id：服务端核验的组织，参与每次读写条件。；非空，业务必需。
-- 字段 cs_waiting_workflow.id：owner 内唯一业务标识，与租户、组织共同构成数据库主键。；非空，业务必需。
-- 字段 cs_waiting_workflow.assistant_config_id：服务端配置的助手引用，不随模型输出变化。；非空，业务必需。
-- 字段 cs_waiting_workflow.busy_at：T+10 用户提示 UTC 期限。；非空，业务必需。
-- 字段 cs_waiting_workflow.bvent_id：来源大会引用，空表示非展会场景。；可空。
-- 字段 cs_waiting_workflow.calendar_version：期限的业务日历版本。；非空，业务必需。
-- 字段 cs_waiting_workflow.closes_at：冻结的营业结束 UTC 时间。；非空，业务必需。
-- 字段 cs_waiting_workflow.conversation_id：贯穿 AI、人工、调查及异步任务的会话引用。；非空，业务必需。
-- 字段 cs_waiting_workflow.email：访客明确提交的邮箱，仅客服 owner 保存。；可空。
-- 字段 cs_waiting_workflow.episode_id：唯一对应的原人工周期。；非空，业务必需。
-- 字段 cs_waiting_workflow.next_opens_at：冻结的下次营业 UTC 时间。；非空，业务必需。
-- 字段 cs_waiting_workflow.principal_id：原会话的访客账号，仅用于归属校验。；非空，业务必需。
-- 字段 cs_waiting_workflow.remind_at：T+5 内部提醒 UTC 期限。；非空，业务必需。
-- 字段 cs_waiting_workflow.reminded：是否登记提醒，不表示实际送达。；非空，业务必需。
-- 字段 cs_waiting_workflow.state：独立等待工作流状态。；非空，业务必需。
-- 字段 cs_waiting_workflow.ticket_id：唯一跟进工单号，不代表邮件已发送。；可空。
-- 字段 cs_waiting_workflow.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 查询索引 IX_cs_waiting_workflow_tenant_id_top_org_id_conversation_id：tenant_id, top_org_id, conversation_id；用于组织队列或 Worker 有界恢复。
-- 唯一索引 IX_cs_waiting_workflow_tenant_id_top_org_id_episode_id：tenant_id, top_org_id, episode_id；用于同归属业务键去重。
CREATE TABLE "ai_conversation_generation" (
    "id" TEXT NOT NULL,
    "tenant_id" TEXT NOT NULL,
    "top_org_id" TEXT NOT NULL,
    "last_completed_turn_id" TEXT NULL,
    "survey_checked_turn_id" TEXT NULL,
    "epoch" INTEGER NOT NULL,
    "human_episode_id" TEXT NULL,
    "current_turn_id" TEXT NULL,
    "turn_lease_until" TEXT NULL,
    "next_seq" INTEGER NOT NULL,
    "created_at" TEXT NOT NULL,
    "updated_at" TEXT NOT NULL,
    "version" INTEGER NOT NULL,
    "conversation_id" TEXT NOT NULL,
    "assistant_config_id" TEXT NOT NULL,
    "bvent_id" TEXT NULL,
    "principal_id" TEXT NOT NULL,
    CONSTRAINT "PK_ai_conversation_generation" PRIMARY KEY ("tenant_id", "top_org_id", "id"),
    CONSTRAINT "ck_ai_conversation_generation_scope" CHECK (
        tenant_id <> ''
        AND top_org_id <> ''
        AND id <> ''
        AND version >= 1
    ),
    CONSTRAINT "ck_conversation_identity" CHECK (
        conversation_id = id
        AND epoch >= 1
        AND next_seq >= 0
    )
);

CREATE TABLE "cs_agent_availability" (
    "id" TEXT NOT NULL,
    "tenant_id" TEXT NOT NULL,
    "top_org_id" TEXT NOT NULL,
    "status" INTEGER NOT NULL,
    "expires_at" TEXT NULL,
    "version" INTEGER NOT NULL,
    CONSTRAINT "PK_cs_agent_availability" PRIMARY KEY ("tenant_id", "top_org_id", "id"),
    CONSTRAINT "ck_cs_agent_availability_scope" CHECK (
        tenant_id <> ''
        AND top_org_id <> ''
        AND id <> ''
        AND version >= 1
    )
);

CREATE TABLE "cs_organization_gate" (
    "tenant_id" TEXT NOT NULL,
    "top_org_id" TEXT NOT NULL,
    "version" INTEGER NOT NULL,
    CONSTRAINT "PK_cs_organization_gate" PRIMARY KEY ("tenant_id", "top_org_id")
);

CREATE TABLE "cs_reception_calendar" (
    "id" TEXT NOT NULL,
    "tenant_id" TEXT NOT NULL,
    "top_org_id" TEXT NOT NULL,
    "calendar_version" TEXT NOT NULL,
    "zone" TEXT NOT NULL,
    "opens" TEXT NOT NULL,
    "closes" TEXT NOT NULL,
    "overrides" TEXT NOT NULL,
    "version" INTEGER NOT NULL,
    CONSTRAINT "PK_cs_reception_calendar" PRIMARY KEY ("tenant_id", "top_org_id", "id"),
    CONSTRAINT "ck_cs_reception_calendar_scope" CHECK (
        tenant_id <> ''
        AND top_org_id <> ''
        AND id <> ''
        AND version >= 1
    )
);

CREATE TABLE "cs_reception_configuration" (
    "id" TEXT NOT NULL,
    "tenant_id" TEXT NOT NULL,
    "top_org_id" TEXT NOT NULL,
    "groups" TEXT NOT NULL,
    "routes" TEXT NOT NULL,
    "version" INTEGER NOT NULL,
    CONSTRAINT "PK_cs_reception_configuration" PRIMARY KEY ("tenant_id", "top_org_id", "id"),
    CONSTRAINT "ck_cs_reception_configuration_scope" CHECK (
        tenant_id <> ''
        AND top_org_id <> ''
        AND id <> ''
        AND version >= 1
    )
);

CREATE TABLE "ai_human_message" (
    "id" TEXT NOT NULL,
    "tenant_id" TEXT NOT NULL,
    "top_org_id" TEXT NOT NULL,
    "episode_id" TEXT NOT NULL,
    "seq" INTEGER NOT NULL,
    "sender_role" TEXT NOT NULL,
    "sender_id" TEXT NOT NULL,
    "client_msg_id" TEXT NOT NULL,
    "content" TEXT NOT NULL,
    "created_at" TEXT NOT NULL,
    "version" INTEGER NOT NULL,
    "conversation_id" TEXT NOT NULL,
    "assistant_config_id" TEXT NOT NULL,
    "bvent_id" TEXT NULL,
    "principal_id" TEXT NOT NULL,
    CONSTRAINT "PK_ai_human_message" PRIMARY KEY ("tenant_id", "top_org_id", "id"),
    CONSTRAINT "ck_ai_human_message_scope" CHECK (
        tenant_id <> ''
        AND top_org_id <> ''
        AND id <> ''
        AND version >= 1
    ),
    CONSTRAINT "ck_message_role" CHECK (
        sender_role IN ('VISITOR', 'AGENT')
        AND seq > 0
    ),
    CONSTRAINT "FK_ai_human_message_ai_conversation_generation_tenant_id_top_org_id_conversation_id" FOREIGN KEY ("tenant_id", "top_org_id", "conversation_id") REFERENCES "ai_conversation_generation" ("tenant_id", "top_org_id", "id") ON DELETE RESTRICT
);

CREATE TABLE "ai_human_receipt" (
    "id" TEXT NOT NULL,
    "tenant_id" TEXT NOT NULL,
    "top_org_id" TEXT NOT NULL,
    "actor_id" TEXT NOT NULL,
    "delivered_seq" INTEGER NOT NULL,
    "read_seq" INTEGER NOT NULL,
    "version" INTEGER NOT NULL,
    "conversation_id" TEXT NOT NULL,
    "assistant_config_id" TEXT NOT NULL,
    "bvent_id" TEXT NULL,
    "principal_id" TEXT NOT NULL,
    CONSTRAINT "PK_ai_human_receipt" PRIMARY KEY ("tenant_id", "top_org_id", "id"),
    CONSTRAINT "ck_ai_human_receipt_scope" CHECK (
        tenant_id <> ''
        AND top_org_id <> ''
        AND id <> ''
        AND version >= 1
    ),
    CONSTRAINT "ck_receipt_watermark" CHECK (
        read_seq >= 0
        AND delivered_seq >= read_seq
    ),
    CONSTRAINT "FK_ai_human_receipt_ai_conversation_generation_tenant_id_top_org_id_conversation_id" FOREIGN KEY ("tenant_id", "top_org_id", "conversation_id") REFERENCES "ai_conversation_generation" ("tenant_id", "top_org_id", "id") ON DELETE RESTRICT
);

CREATE TABLE "ai_resolution_evidence" (
    "id" TEXT NOT NULL,
    "tenant_id" TEXT NOT NULL,
    "top_org_id" TEXT NOT NULL,
    "kind" TEXT NOT NULL,
    "turn_id" TEXT NOT NULL,
    "epoch" INTEGER NOT NULL,
    "confidence" REAL NULL,
    "policy" TEXT NOT NULL,
    "fingerprint" TEXT NOT NULL,
    "created_at" TEXT NOT NULL,
    "trace_id" TEXT NOT NULL,
    "version" INTEGER NOT NULL,
    "conversation_id" TEXT NOT NULL,
    "assistant_config_id" TEXT NOT NULL,
    "bvent_id" TEXT NULL,
    "principal_id" TEXT NOT NULL,
    CONSTRAINT "PK_ai_resolution_evidence" PRIMARY KEY ("tenant_id", "top_org_id", "id"),
    CONSTRAINT "ck_ai_resolution_evidence_scope" CHECK (
        tenant_id <> ''
        AND top_org_id <> ''
        AND id <> ''
        AND version >= 1
    ),
    CONSTRAINT "FK_ai_resolution_evidence_ai_conversation_generation_tenant_id_top_org_id_conversation_id" FOREIGN KEY ("tenant_id", "top_org_id", "conversation_id") REFERENCES "ai_conversation_generation" ("tenant_id", "top_org_id", "id") ON DELETE RESTRICT
);

CREATE TABLE "ai_resolution_survey" (
    "id" TEXT NOT NULL,
    "tenant_id" TEXT NOT NULL,
    "top_org_id" TEXT NOT NULL,
    "opened_at" TEXT NOT NULL,
    "epoch" INTEGER NOT NULL,
    "status" INTEGER NOT NULL,
    "handoff_choice" INTEGER NOT NULL,
    "source_turn_id" TEXT NOT NULL,
    "evidence" TEXT NOT NULL,
    "expires_at" TEXT NOT NULL,
    "responded_at" TEXT NULL,
    "displayed_at" TEXT NULL,
    "version" INTEGER NOT NULL,
    "conversation_id" TEXT NOT NULL,
    "assistant_config_id" TEXT NOT NULL,
    "bvent_id" TEXT NULL,
    "principal_id" TEXT NOT NULL,
    CONSTRAINT "PK_ai_resolution_survey" PRIMARY KEY ("tenant_id", "top_org_id", "id"),
    CONSTRAINT "ck_ai_resolution_survey_scope" CHECK (
        tenant_id <> ''
        AND top_org_id <> ''
        AND id <> ''
        AND version >= 1
    ),
    CONSTRAINT "ck_survey_state" CHECK (
        status BETWEEN 0 AND 3
        AND handoff_choice BETWEEN 0 AND 3
        AND epoch >= 1
    ),
    CONSTRAINT "FK_ai_resolution_survey_ai_conversation_generation_tenant_id_top_org_id_conversation_id" FOREIGN KEY ("tenant_id", "top_org_id", "conversation_id") REFERENCES "ai_conversation_generation" ("tenant_id", "top_org_id", "id") ON DELETE RESTRICT
);

CREATE TABLE "ai_turn" (
    "id" TEXT NOT NULL,
    "tenant_id" TEXT NOT NULL,
    "top_org_id" TEXT NOT NULL,
    "epoch" INTEGER NOT NULL,
    "user_content" TEXT NOT NULL,
    "assistant_content" TEXT NULL,
    "status" TEXT NOT NULL,
    "created_at" TEXT NOT NULL,
    "completed_at" TEXT NULL,
    "trace_id" TEXT NOT NULL,
    "version" INTEGER NOT NULL,
    "conversation_id" TEXT NOT NULL,
    "assistant_config_id" TEXT NOT NULL,
    "bvent_id" TEXT NULL,
    "principal_id" TEXT NOT NULL,
    CONSTRAINT "PK_ai_turn" PRIMARY KEY ("tenant_id", "top_org_id", "id"),
    CONSTRAINT "ck_ai_turn_scope" CHECK (
        tenant_id <> ''
        AND top_org_id <> ''
        AND id <> ''
        AND version >= 1
    ),
    CONSTRAINT "FK_ai_turn_ai_conversation_generation_tenant_id_top_org_id_conversation_id" FOREIGN KEY ("tenant_id", "top_org_id", "conversation_id") REFERENCES "ai_conversation_generation" ("tenant_id", "top_org_id", "id") ON DELETE RESTRICT
);

CREATE TABLE "cs_command_receipt" (
    "id" TEXT NOT NULL,
    "tenant_id" TEXT NOT NULL,
    "top_org_id" TEXT NOT NULL,
    "created_at" TEXT NOT NULL,
    "trace_id" TEXT NOT NULL,
    "actor_id" TEXT NOT NULL,
    "action" TEXT NOT NULL,
    "request_id" TEXT NOT NULL,
    "fingerprint" TEXT NOT NULL,
    "result_json" TEXT NOT NULL,
    "version" INTEGER NOT NULL,
    "conversation_id" TEXT NOT NULL,
    "assistant_config_id" TEXT NOT NULL,
    "bvent_id" TEXT NULL,
    "principal_id" TEXT NOT NULL,
    CONSTRAINT "PK_cs_command_receipt" PRIMARY KEY ("tenant_id", "top_org_id", "id"),
    CONSTRAINT "ck_cs_command_receipt_scope" CHECK (
        tenant_id <> ''
        AND top_org_id <> ''
        AND id <> ''
        AND version >= 1
    ),
    CONSTRAINT "FK_cs_command_receipt_ai_conversation_generation_tenant_id_top_org_id_conversation_id" FOREIGN KEY ("tenant_id", "top_org_id", "conversation_id") REFERENCES "ai_conversation_generation" ("tenant_id", "top_org_id", "id") ON DELETE RESTRICT
);

CREATE TABLE "cs_followup_form" (
    "id" TEXT NOT NULL,
    "tenant_id" TEXT NOT NULL,
    "top_org_id" TEXT NOT NULL,
    "origin" TEXT NOT NULL,
    "episode_id" TEXT NOT NULL,
    "ticket_id" TEXT NOT NULL,
    "group_id" TEXT NOT NULL,
    "request_id" TEXT NOT NULL,
    "fingerprint" TEXT NOT NULL,
    "contact_name" TEXT NULL,
    "content" TEXT NOT NULL,
    "state" TEXT NOT NULL,
    "assigned_to" TEXT NULL,
    "submitted_at" TEXT NOT NULL,
    "claimed_at" TEXT NULL,
    "version" INTEGER NOT NULL,
    "conversation_id" TEXT NOT NULL,
    "assistant_config_id" TEXT NOT NULL,
    "bvent_id" TEXT NULL,
    "principal_id" TEXT NOT NULL,
    CONSTRAINT "PK_cs_followup_form" PRIMARY KEY ("tenant_id", "top_org_id", "id"),
    CONSTRAINT "ck_cs_followup_form_scope" CHECK (
        tenant_id <> ''
        AND top_org_id <> ''
        AND id <> ''
        AND version >= 1
    ),
    CONSTRAINT "ck_followup_assignee" CHECK (
        (
            state = 'OPEN'
            AND assigned_to IS NULL
        )
        OR (
            state = 'CLAIMED'
            AND assigned_to IS NOT NULL
        )
    ),
    CONSTRAINT "ck_followup_origin" CHECK (
        origin IN ('FORM', 'EMAIL')
        AND (
            origin <> 'FORM'
            OR content <> ''
        )
    ),
    CONSTRAINT "FK_cs_followup_form_ai_conversation_generation_tenant_id_top_org_id_conversation_id" FOREIGN KEY ("tenant_id", "top_org_id", "conversation_id") REFERENCES "ai_conversation_generation" ("tenant_id", "top_org_id", "id") ON DELETE RESTRICT
);

CREATE TABLE "cs_followup_response" (
    "id" TEXT NOT NULL,
    "tenant_id" TEXT NOT NULL,
    "top_org_id" TEXT NOT NULL,
    "episode_id" TEXT NOT NULL,
    "ticket_id" TEXT NOT NULL,
    "actor_id" TEXT NOT NULL,
    "subject" TEXT NOT NULL,
    "content" TEXT NOT NULL,
    "state" INTEGER NOT NULL,
    "created_at" TEXT NOT NULL,
    "version" INTEGER NOT NULL,
    "conversation_id" TEXT NOT NULL,
    "assistant_config_id" TEXT NOT NULL,
    "bvent_id" TEXT NULL,
    "principal_id" TEXT NOT NULL,
    CONSTRAINT "PK_cs_followup_response" PRIMARY KEY ("tenant_id", "top_org_id", "id"),
    CONSTRAINT "ck_cs_followup_response_scope" CHECK (
        tenant_id <> ''
        AND top_org_id <> ''
        AND id <> ''
        AND version >= 1
    ),
    CONSTRAINT "FK_cs_followup_response_ai_conversation_generation_tenant_id_top_org_id_conversation_id" FOREIGN KEY ("tenant_id", "top_org_id", "conversation_id") REFERENCES "ai_conversation_generation" ("tenant_id", "top_org_id", "id") ON DELETE RESTRICT
);

CREATE TABLE "cs_handoff_episode" (
    "id" TEXT NOT NULL,
    "tenant_id" TEXT NOT NULL,
    "top_org_id" TEXT NOT NULL,
    "status" INTEGER NOT NULL,
    "trigger_source" INTEGER NOT NULL,
    "reason_code" INTEGER NOT NULL,
    "group_id" TEXT NOT NULL,
    "category" INTEGER NOT NULL,
    "config_version" INTEGER NOT NULL,
    "classification_source" TEXT NOT NULL,
    "agent_id" TEXT NULL,
    "requested_at" TEXT NOT NULL,
    "claimed_at" TEXT NULL,
    "first_response_at" TEXT NULL,
    "finished_at" TEXT NULL,
    "trace_id" TEXT NOT NULL,
    "version" INTEGER NOT NULL,
    "conversation_id" TEXT NOT NULL,
    "assistant_config_id" TEXT NOT NULL,
    "bvent_id" TEXT NULL,
    "principal_id" TEXT NOT NULL,
    CONSTRAINT "PK_cs_handoff_episode" PRIMARY KEY ("tenant_id", "top_org_id", "id"),
    CONSTRAINT "ck_cs_handoff_episode_scope" CHECK (
        tenant_id <> ''
        AND top_org_id <> ''
        AND id <> ''
        AND version >= 1
    ),
    CONSTRAINT "ck_episode_assignee" CHECK (
        status NOT IN (1, 2, 3)
        OR agent_id IS NOT NULL
    ),
    CONSTRAINT "ck_episode_state" CHECK (status BETWEEN 0 AND 4),
    CONSTRAINT "FK_cs_handoff_episode_ai_conversation_generation_tenant_id_top_org_id_conversation_id" FOREIGN KEY ("tenant_id", "top_org_id", "conversation_id") REFERENCES "ai_conversation_generation" ("tenant_id", "top_org_id", "id") ON DELETE RESTRICT
);

CREATE TABLE "cs_handoff_proposal" (
    "id" TEXT NOT NULL,
    "tenant_id" TEXT NOT NULL,
    "top_org_id" TEXT NOT NULL,
    "turn_id" TEXT NOT NULL,
    "epoch" INTEGER NOT NULL,
    "status" INTEGER NOT NULL,
    "reason_code" INTEGER NOT NULL,
    "user_facing_reason" TEXT NOT NULL,
    "confidence" REAL NOT NULL,
    "expires_at" TEXT NOT NULL,
    "created_at" TEXT NOT NULL,
    "version" INTEGER NOT NULL,
    "conversation_id" TEXT NOT NULL,
    "assistant_config_id" TEXT NOT NULL,
    "bvent_id" TEXT NULL,
    "principal_id" TEXT NOT NULL,
    CONSTRAINT "PK_cs_handoff_proposal" PRIMARY KEY ("tenant_id", "top_org_id", "id"),
    CONSTRAINT "ck_cs_handoff_proposal_scope" CHECK (
        tenant_id <> ''
        AND top_org_id <> ''
        AND id <> ''
        AND version >= 1
    ),
    CONSTRAINT "ck_proposal_confidence" CHECK (
        confidence >= 0
        AND confidence <= 1
        AND status BETWEEN 0 AND 3
    ),
    CONSTRAINT "FK_cs_handoff_proposal_ai_conversation_generation_tenant_id_top_org_id_conversation_id" FOREIGN KEY ("tenant_id", "top_org_id", "conversation_id") REFERENCES "ai_conversation_generation" ("tenant_id", "top_org_id", "id") ON DELETE RESTRICT
);

CREATE TABLE "cs_reliable_task" (
    "id" TEXT NOT NULL,
    "tenant_id" TEXT NOT NULL,
    "top_org_id" TEXT NOT NULL,
    "kind" TEXT NOT NULL,
    "event_key" TEXT NOT NULL,
    "episode_id" TEXT NOT NULL,
    "payload" TEXT NOT NULL,
    "state" INTEGER NOT NULL,
    "attempts" INTEGER NOT NULL,
    "total_attempts" INTEGER NOT NULL,
    "manual_retry_count" INTEGER NOT NULL,
    "last_retried_by" TEXT NULL,
    "last_retried_at" TEXT NULL,
    "due_at" TEXT NOT NULL,
    "lease_id" TEXT NULL,
    "lease_until" TEXT NULL,
    "last_error" TEXT NULL,
    "trace_id" TEXT NOT NULL,
    "created_at" TEXT NOT NULL,
    "version" INTEGER NOT NULL,
    "conversation_id" TEXT NOT NULL,
    "assistant_config_id" TEXT NOT NULL,
    "bvent_id" TEXT NULL,
    "principal_id" TEXT NOT NULL,
    CONSTRAINT "PK_cs_reliable_task" PRIMARY KEY ("tenant_id", "top_org_id", "id"),
    CONSTRAINT "ck_cs_reliable_task_scope" CHECK (
        tenant_id <> ''
        AND top_org_id <> ''
        AND id <> ''
        AND version >= 1
    ),
    CONSTRAINT "ck_task_lease" CHECK (
        (
            state = 1
            AND lease_id IS NOT NULL
            AND lease_until IS NOT NULL
        )
        OR (
            state <> 1
            AND lease_id IS NULL
            AND lease_until IS NULL
        )
    ),
    CONSTRAINT "ck_task_retry_audit" CHECK (
        total_attempts >= attempts
        AND manual_retry_count >= 0
        AND (
            manual_retry_count = 0
            OR (
                last_retried_by IS NOT NULL
                AND last_retried_at IS NOT NULL
            )
        )
    ),
    CONSTRAINT "ck_task_state" CHECK (
        state BETWEEN 0 AND 4
        AND attempts BETWEEN 0 AND 5
    ),
    CONSTRAINT "FK_cs_reliable_task_ai_conversation_generation_tenant_id_top_org_id_conversation_id" FOREIGN KEY ("tenant_id", "top_org_id", "conversation_id") REFERENCES "ai_conversation_generation" ("tenant_id", "top_org_id", "id") ON DELETE RESTRICT
);

CREATE TABLE "cs_waiting_workflow" (
    "id" TEXT NOT NULL,
    "tenant_id" TEXT NOT NULL,
    "top_org_id" TEXT NOT NULL,
    "episode_id" TEXT NOT NULL,
    "state" INTEGER NOT NULL,
    "remind_at" TEXT NOT NULL,
    "busy_at" TEXT NOT NULL,
    "closes_at" TEXT NOT NULL,
    "next_opens_at" TEXT NOT NULL,
    "calendar_version" TEXT NOT NULL,
    "reminded" INTEGER NOT NULL,
    "email" TEXT NULL,
    "ticket_id" TEXT NULL,
    "version" INTEGER NOT NULL,
    "conversation_id" TEXT NOT NULL,
    "assistant_config_id" TEXT NOT NULL,
    "bvent_id" TEXT NULL,
    "principal_id" TEXT NOT NULL,
    CONSTRAINT "PK_cs_waiting_workflow" PRIMARY KEY ("tenant_id", "top_org_id", "id"),
    CONSTRAINT "ck_cs_waiting_workflow_scope" CHECK (
        tenant_id <> ''
        AND top_org_id <> ''
        AND id <> ''
        AND version >= 1
    ),
    CONSTRAINT "ck_waiting_state" CHECK (state BETWEEN 0 AND 6),
    CONSTRAINT "FK_cs_waiting_workflow_ai_conversation_generation_tenant_id_top_org_id_conversation_id" FOREIGN KEY ("tenant_id", "top_org_id", "conversation_id") REFERENCES "ai_conversation_generation" ("tenant_id", "top_org_id", "id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_ai_conversation_generation_tenant_id_id" ON "ai_conversation_generation" ("tenant_id", "id");

CREATE UNIQUE INDEX "IX_ai_human_message_tenant_id_top_org_id_conversation_id_sender_id_client_msg_id" ON "ai_human_message" (
    "tenant_id",
    "top_org_id",
    "conversation_id",
    "sender_id",
    "client_msg_id"
);

CREATE UNIQUE INDEX "IX_ai_human_message_tenant_id_top_org_id_conversation_id_seq" ON "ai_human_message" (
    "tenant_id",
    "top_org_id",
    "conversation_id",
    "seq"
);

CREATE UNIQUE INDEX "IX_ai_human_receipt_tenant_id_top_org_id_conversation_id_actor_id" ON "ai_human_receipt" (
    "tenant_id",
    "top_org_id",
    "conversation_id",
    "actor_id"
);

CREATE UNIQUE INDEX "IX_ai_resolution_evidence_tenant_id_top_org_id_conversation_id_turn_id_kind" ON "ai_resolution_evidence" (
    "tenant_id",
    "top_org_id",
    "conversation_id",
    "turn_id",
    "kind"
);

CREATE UNIQUE INDEX "IX_ai_resolution_survey_tenant_id_top_org_id_conversation_id" ON "ai_resolution_survey" ("tenant_id", "top_org_id", "conversation_id");

CREATE INDEX "IX_ai_turn_tenant_id_top_org_id_conversation_id" ON "ai_turn" ("tenant_id", "top_org_id", "conversation_id");

CREATE UNIQUE INDEX "IX_cs_command_receipt_tenant_id_top_org_id_conversation_id_actor_id_action_request_id" ON "cs_command_receipt" (
    "tenant_id",
    "top_org_id",
    "conversation_id",
    "actor_id",
    "action",
    "request_id"
);

CREATE INDEX "IX_cs_followup_form_tenant_id_top_org_id_conversation_id" ON "cs_followup_form" ("tenant_id", "top_org_id", "conversation_id");

CREATE UNIQUE INDEX "IX_cs_followup_form_tenant_id_top_org_id_episode_id" ON "cs_followup_form" ("tenant_id", "top_org_id", "episode_id");

CREATE INDEX "IX_cs_followup_response_tenant_id_top_org_id_conversation_id" ON "cs_followup_response" ("tenant_id", "top_org_id", "conversation_id");

CREATE UNIQUE INDEX "IX_cs_followup_response_tenant_id_top_org_id_episode_id" ON "cs_followup_response" ("tenant_id", "top_org_id", "episode_id");

CREATE INDEX "IX_cs_handoff_episode_tenant_id_top_org_id_assistant_config_id_status" ON "cs_handoff_episode" (
    "tenant_id",
    "top_org_id",
    "assistant_config_id",
    "status"
);

CREATE INDEX "IX_cs_handoff_episode_tenant_id_top_org_id_conversation_id" ON "cs_handoff_episode" ("tenant_id", "top_org_id", "conversation_id");

CREATE INDEX "IX_cs_handoff_proposal_tenant_id_top_org_id_conversation_id" ON "cs_handoff_proposal" ("tenant_id", "top_org_id", "conversation_id");

CREATE UNIQUE INDEX "IX_cs_reception_calendar_tenant_id_top_org_id" ON "cs_reception_calendar" ("tenant_id", "top_org_id");

CREATE UNIQUE INDEX "IX_cs_reception_configuration_tenant_id_top_org_id" ON "cs_reception_configuration" ("tenant_id", "top_org_id");

CREATE INDEX "IX_cs_reliable_task_kind_state_due_at" ON "cs_reliable_task" ("kind", "state", "due_at");

CREATE INDEX "IX_cs_reliable_task_tenant_id_top_org_id_conversation_id" ON "cs_reliable_task" ("tenant_id", "top_org_id", "conversation_id");

CREATE UNIQUE INDEX "IX_cs_reliable_task_tenant_id_top_org_id_event_key" ON "cs_reliable_task" ("tenant_id", "top_org_id", "event_key");

CREATE INDEX "IX_cs_waiting_workflow_tenant_id_top_org_id_conversation_id" ON "cs_waiting_workflow" ("tenant_id", "top_org_id", "conversation_id");

CREATE UNIQUE INDEX "IX_cs_waiting_workflow_tenant_id_top_org_id_episode_id" ON "cs_waiting_workflow" ("tenant_id", "top_org_id", "episode_id");
