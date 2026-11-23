-- 目的：AI.Customer.Service 首次部署独立数据库；方言：Kingbase。
-- 前提：全新空库、独占建库权限、已备份目标；禁止在已有业务表的数据库执行。
-- 兼容：Kingbase 要求 PostgreSQL 兼容模式。
-- 回滚：失败时回滚 DDL 事务（数据库支持时）；否则仅清理本次新建的独立空库后重新部署。
-- 范围：会话绑定服务端注册的助手，复合主键和 FK 约束业务引用。
-- 约束：NOT NULL 禁止缺失必需事实，UNIQUE 保证幂等，CHECK 约束状态/租约/版本，FK 禁止跨组织引用。
-- 删除：全部物理保留，不做逻辑删除；按依赖顺序归档清理，不删除未终结工单或待投递任务。

-- 表 ai_anonymous_conversation_access：Conversation owner 的匿名访问授权；原始凭据只在创建响应中返回一次。；数据 owner：CustomerService；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 ai_anonymous_conversation_access.id：owner 内唯一业务标识。；非空，业务必需。
-- 字段 ai_anonymous_conversation_access.assistant_config_id：服务端配置的助手引用，不随模型输出变化。；非空，业务必需。
-- 字段 ai_anonymous_conversation_access.bvent_id：来源大会引用，空表示非展会场景。；可空。
-- 字段 ai_anonymous_conversation_access.conversation_id：贯穿 AI、人工、调查及异步任务的会话引用。；非空，业务必需。
-- 字段 ai_anonymous_conversation_access.credential_hash：256 位随机凭据的 SHA-256 摘要，不保存明文。；非空，业务必需。
-- 字段 ai_anonymous_conversation_access.expires_at：固定七天有效期，UTC。；非空，业务必需。
-- 字段 ai_anonymous_conversation_access.principal_id：原会话的访客账号，仅用于归属校验。；非空，业务必需。
-- 字段 ai_anonymous_conversation_access.revoked_at：非空即撤销，不删除原会话和消息。；可空。
-- 字段 ai_anonymous_conversation_access.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 唯一索引 IX_ai_anonymous_conversation_access_conversation_id：conversation_id；用于同归属业务键去重。
-- 唯一索引 IX_ai_anonymous_conversation_access_credential_hash：credential_hash；用于同归属业务键去重。

-- 表 ai_conversation_external_identity：Conversation owner 的不可覆盖第三方身份绑定；它不授予会话或客服权限。；数据 owner：CustomerService；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 ai_conversation_external_identity.id：owner 内唯一业务标识。；非空，业务必需。
-- 字段 ai_conversation_external_identity.assistant_config_id：服务端配置的助手引用，不随模型输出变化。；非空，业务必需。
-- 字段 ai_conversation_external_identity.bound_at：首次成功绑定的 UTC 时间。；非空，业务必需。
-- 字段 ai_conversation_external_identity.bvent_id：来源大会引用，空表示非展会场景。；可空。
-- 字段 ai_conversation_external_identity.conversation_id：贯穿 AI、人工、调查及异步任务的会话引用。；非空，业务必需。
-- 字段 ai_conversation_external_identity.external_auth_id：不透明第三方 cookie ID，最多 512 字符，不回显或进入通知载荷。；非空，业务必需。
-- 字段 ai_conversation_external_identity.principal_id：原会话的访客账号，仅用于归属校验。；非空，业务必需。
-- 字段 ai_conversation_external_identity.provider_code：当前仅接受 ADSALE，服务端固定。；非空，业务必需。
-- 字段 ai_conversation_external_identity.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 唯一索引 IX_ai_conversation_external_identity_conversation_id_provider_~：conversation_id, provider_code；用于同归属业务键去重。

-- 表 ai_conversation_generation：Conversation owner 的回复代次和当前轮次，人工与 AI 共用同一会话。；数据 owner：Conversation；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 ai_conversation_generation.id：owner 内唯一业务标识。；非空，业务必需。
-- 字段 ai_conversation_generation.assistant_config_id：服务端配置的助手引用，不随模型输出变化。；非空，业务必需。
-- 字段 ai_conversation_generation.bvent_id：来源大会引用，空表示非展会场景。；可空。
-- 字段 ai_conversation_generation.conversation_id：贯穿 AI、人工、调查及异步任务的会话引用。；非空，业务必需。
-- 字段 ai_conversation_generation.created_at：首次创建 UTC 时间。；非空，业务必需。
-- 字段 ai_conversation_generation.current_turn_id：正在生成的 AI 轮次，拒绝并发轮次及迟到响应。；可空。
-- 字段 ai_conversation_generation.epoch：回复代次，每次人工接管或恢复均递增。；非空，业务必需。
-- 字段 ai_conversation_generation.external_identity_required：创建时冻结的第三方身份要求，后续助手配置变化不追溯已有会话。；非空，业务必需。
-- 字段 ai_conversation_generation.human_episode_id：当前独占回复权的人工周期，空表示允许 AI。；可空。
-- 字段 ai_conversation_generation.last_completed_turn_id：最近完整成功轮次，调查恢复 Worker 仅处理尚未评估的新成功轮次。；可空。
-- 字段 ai_conversation_generation.next_seq：整个会话共享的人工消息最大序列。；非空，业务必需。
-- 字段 ai_conversation_generation.principal_id：原会话的访客账号，仅用于归属校验。；非空，业务必需。
-- 字段 ai_conversation_generation.survey_checked_turn_id：最后已完成资格评估的成功轮次，不根据用户沉默产生新调查。；可空。
-- 字段 ai_conversation_generation.turn_lease_until：AI 生成租约 UTC 截止，崩溃后允许新轮次恢复。；可空。
-- 字段 ai_conversation_generation.updated_at：最后活动 UTC 时间，不作为调查结束证据。；非空，业务必需。
-- 字段 ai_conversation_generation.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 唯一索引 IX_ai_conversation_generation_id：id；用于同归属业务键去重。

-- 表 ai_human_message：Conversation owner 的人工消息，所有周期共享递增序列。；数据 owner：Conversation；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 ai_human_message.id：owner 内唯一业务标识。；非空，业务必需。
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
-- 唯一索引 IX_ai_human_message_conversation_id_seq：conversation_id, seq；用于同归属业务键去重。
-- 唯一索引 IX_ai_human_message_conversation_id_sender_id_client_msg_id：conversation_id, sender_id, client_msg_id；用于同归属业务键去重。

-- 表 ai_human_receipt：单一会话主体的接收及已读水位，不能代替对方确认。；数据 owner：Conversation；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 ai_human_receipt.id：owner 内唯一业务标识。；非空，业务必需。
-- 字段 ai_human_receipt.actor_id：回执所属认证账号。；非空，业务必需。
-- 字段 ai_human_receipt.assistant_config_id：服务端配置的助手引用，不随模型输出变化。；非空，业务必需。
-- 字段 ai_human_receipt.bvent_id：来源大会引用，空表示非展会场景。；可空。
-- 字段 ai_human_receipt.conversation_id：贯穿 AI、人工、调查及异步任务的会话引用。；非空，业务必需。
-- 字段 ai_human_receipt.delivered_seq：只增不减的接收序列。；非空，业务必需。
-- 字段 ai_human_receipt.principal_id：原会话的访客账号，仅用于归属校验。；非空，业务必需。
-- 字段 ai_human_receipt.read_seq：不超过接收水位的已读序列。；非空，业务必需。
-- 字段 ai_human_receipt.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 唯一索引 IX_ai_human_receipt_conversation_id_actor_id：conversation_id, actor_id；用于同归属业务键去重。

-- 表 ai_resolution_evidence：Conversation owner 的模型结束候选或确定性用户结束信号，只保存引用及摘要，不复制正文。；数据 owner：Conversation；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 ai_resolution_evidence.id：owner 内唯一业务标识。；非空，业务必需。
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
-- 唯一索引 IX_ai_resolution_evidence_conversation_id_turn_id_kind：conversation_id, turn_id, kind；用于同归属业务键去重。

-- 表 ai_resolution_survey：Conversation owner 的单次满意度事实，人工结束不会创建调查。；数据 owner：Conversation；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 ai_resolution_survey.id：owner 内唯一业务标识。；非空，业务必需。
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
-- 唯一索引 IX_ai_resolution_survey_conversation_id：conversation_id；用于同归属业务键去重。

-- 表 ai_turn：真实 AI 轮次及成功证据，失败回答不得进入调查资格；数据 owner：Conversation；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 ai_turn.id：owner 内唯一业务标识。；非空，业务必需。
-- 字段 ai_turn.assistant_config_id：服务端配置的助手引用，不随模型输出变化。；非空，业务必需。
-- 字段 ai_turn.assistant_content：模型完整成功的展示文本；可空。
-- 字段 ai_turn.bvent_id：来源大会引用，空表示非展会场景。；可空。
-- 字段 ai_turn.completed_at：完成或失败 UTC 时间；可空。
-- 字段 ai_turn.conversation_id：贯穿 AI、人工、调查及异步任务的会话引用。；非空，业务必需。
-- 字段 ai_turn.created_at：生成开始 UTC 时间；非空，业务必需。
-- 字段 ai_turn.epoch：开始时回复代次，完成时必须匹配；非空，业务必需。
-- 字段 ai_turn.principal_id：原会话的访客账号，仅用于归属校验。；非空，业务必需。
-- 字段 ai_turn.status：RUNNING、SUCCEEDED、FAILED 或 FENCED；非空，业务必需。
-- 字段 ai_turn.trace_id：传播至模型和故障任务的关联标识；非空，业务必需。
-- 字段 ai_turn.user_content：本轮访客原始纯文本；非空，业务必需。
-- 字段 ai_turn.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 查询索引 IX_ai_turn_conversation_id：conversation_id；用于组织队列或 Worker 有界恢复。

-- 表 cs_agent_availability：组织级客服在线代次和租约，不授予成员资格；数据 owner：CustomerService；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 cs_agent_availability.id：owner 内唯一业务标识。；非空，业务必需。
-- 字段 cs_agent_availability.expires_at：ONLINE 心跳 UTC 截止，旧代次不能续约；可空。
-- 字段 cs_agent_availability.status：明确选择的展示状态；非空，业务必需。
-- 字段 cs_agent_availability.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。

-- 表 cs_chat_notification_configuration：组织聊天通知的统一持久配置；主加密密钥只由部署维护；数据 owner：CustomerService；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 cs_chat_notification_configuration.id：owner 内唯一业务标识。；非空，业务必需。
-- 字段 cs_chat_notification_configuration.account_code：固定对账账号，不作为认证凭据；非空，业务必需。
-- 字段 cs_chat_notification_configuration.active_provider：ADSALE 或 WECOM_ROBOT；每笔新申请冻结一个通道；非空，业务必需。
-- 字段 cs_chat_notification_configuration.api_token_ciphertext：AES-GCM 密文；只由保存入口加密、通知适配器解密，禁止 API 回显；可空。
-- 字段 cs_chat_notification_configuration.base_url：固定官方雅式 API 根地址；非空，业务必需。
-- 字段 cs_chat_notification_configuration.chat_group：SALES、VISITOR、FINANCE、EMP 对应的官方企微机器人 Hook ID；非空，业务必需。
-- 字段 cs_chat_notification_configuration.enabled：统一控制即时通知、旧提醒和邮件投递；默认关闭；非空，业务必需。
-- 字段 cs_chat_notification_configuration.public_base_url：客服站点 HTTPS 根地址，通知链接不携带凭据；非空，业务必需。
-- 字段 cs_chat_notification_configuration.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 唯一索引 IX_cs_chat_notification_configuration_id：id；用于同归属业务键去重。

-- 表 cs_command_receipt：不可变命令回执，同键重试仍需复核权限；数据 owner：CustomerService；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 cs_command_receipt.id：owner 内唯一业务标识。；非空，业务必需。
-- 字段 cs_command_receipt.action：明确的业务动作；非空，业务必需。
-- 字段 cs_command_receipt.actor_id：首次命令主体；非空，业务必需。
-- 字段 cs_command_receipt.assistant_config_id：服务端配置的助手引用，不随模型输出变化。；非空，业务必需。
-- 字段 cs_command_receipt.bvent_id：来源大会引用，空表示非展会场景。；可空。
-- 字段 cs_command_receipt.conversation_id：贯穿 AI、人工、调查及异步任务的会话引用。；非空，业务必需。
-- 字段 cs_command_receipt.created_at：首次成功命令的 UTC 审计时间，重放不更新；非空，业务必需。
-- 字段 cs_command_receipt.fingerprint：动作和原输入 SHA256 摘要；非空，业务必需。
-- 字段 cs_command_receipt.principal_id：原会话的访客账号，仅用于归属校验。；非空，业务必需。
-- 字段 cs_command_receipt.request_id：首次请求幂等键；非空，业务必需。
-- 字段 cs_command_receipt.result_json：首次成功的安全响应投影；非空，业务必需。
-- 字段 cs_command_receipt.trace_id：首次成功命令的服务端追踪标识；非空，业务必需。
-- 字段 cs_command_receipt.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 唯一索引 IX_cs_command_receipt_conversation_id_actor_id_action_request_~：conversation_id, actor_id, action, request_id；用于同归属业务键去重。

-- 表 cs_followup_form：原周期的留言表单及认领事实，回复另有生命周期。；数据 owner：CustomerService；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 cs_followup_form.id：owner 内唯一业务标识。；非空，业务必需。
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
-- 唯一索引 IX_cs_followup_form_conversation_id_episode_id：conversation_id, episode_id；用于同归属业务键去重。

-- 表 cs_followup_receipt：按客服账号隔离的离线留言首次查看事实。；数据 owner：CustomerService；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 cs_followup_receipt.id：owner 内唯一业务标识。；非空，业务必需。
-- 字段 cs_followup_receipt.assistant_config_id：服务端配置的助手引用，不随模型输出变化。；非空，业务必需。
-- 字段 cs_followup_receipt.bvent_id：来源大会引用，空表示非展会场景。；可空。
-- 字段 cs_followup_receipt.conversation_id：贯穿 AI、人工、调查及异步任务的会话引用。；非空，业务必需。
-- 字段 cs_followup_receipt.episode_id：固定原接待周期。；非空，业务必需。
-- 字段 cs_followup_receipt.principal_id：原会话的访客账号，仅用于归属校验。；非空，业务必需。
-- 字段 cs_followup_receipt.seen_at：首次确认查看的 UTC 时间。；非空，业务必需。
-- 字段 cs_followup_receipt.seen_by：正式认证客服账号。；非空，业务必需。
-- 字段 cs_followup_receipt.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 唯一索引 IX_cs_followup_receipt_conversation_id_episode_id_seen_by：conversation_id, episode_id, seen_by；用于同归属业务键去重。

-- 表 cs_followup_response：客服最终邮件回复，内容与可靠任务原子保存。；数据 owner：CustomerService；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 cs_followup_response.id：owner 内唯一业务标识。；非空，业务必需。
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
-- 唯一索引 IX_cs_followup_response_conversation_id_episode_id：conversation_id, episode_id；用于同归属业务键去重。

-- 表 cs_handoff_episode：客服 owner 的一次人工申请及冻结路由，不复制账号主数据。；数据 owner：CustomerService；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 cs_handoff_episode.id：owner 内唯一业务标识。；非空，业务必需。
-- 字段 cs_handoff_episode.agent_id：真实认领客服账号。；可空。
-- 字段 cs_handoff_episode.assistant_config_id：服务端配置的助手引用，不随模型输出变化。；非空，业务必需。
-- 字段 cs_handoff_episode.auto_close_at：ACTIVE 接待中最近客服回复后五分钟的自动结单截止。；可空。
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
-- 字段 cs_handoff_episode.resolution_reason：结束原因；RESOLVED 时为人工结束或访客超时结束。；可空。
-- 字段 cs_handoff_episode.status：接待状态，禁止客户端直接指定。；非空，业务必需。
-- 字段 cs_handoff_episode.trace_id：首次申请跨任务追踪标识。；非空，业务必需。
-- 字段 cs_handoff_episode.trigger_source：首次确定性来源，改派不得覆盖。；非空，业务必需。
-- 字段 cs_handoff_episode.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 查询索引 IX_cs_handoff_episode_conversation_id：conversation_id；用于组织队列或 Worker 有界恢复。
-- 查询索引 IX_cs_handoff_episode_assistant_config_id_status：assistant_config_id, status；用于组织队列或 Worker 有界恢复。
-- 查询索引 IX_cs_handoff_episode_status_auto_close_at_id：status, auto_close_at, id；用于组织队列或 Worker 有界恢复。

-- 表 cs_handoff_proposal：受限 AI 工具建议卡，访客确认后才创建人工周期。；数据 owner：CustomerService；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 cs_handoff_proposal.id：owner 内唯一业务标识。；非空，业务必需。
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
-- 查询索引 IX_cs_handoff_proposal_conversation_id：conversation_id；用于组织队列或 Worker 有界恢复。

-- 表 cs_reception_calendar：组织级营业日历，只影响新等待周期，既有 UTC 期限保持冻结；数据 owner：CustomerService；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 cs_reception_calendar.id：owner 内唯一业务标识。；非空，业务必需。
-- 字段 cs_reception_calendar.calendar_version：业务规则版本，改变时区、时间或日期覆盖时必须升级；非空，业务必需。
-- 字段 cs_reception_calendar.closes：同日营业结束，不含边界，默认 17:00；非空，业务必需。
-- 字段 cs_reception_calendar.opens：同日营业开始，含边界，默认 10:00；非空，业务必需。
-- 字段 cs_reception_calendar.overrides：ISO 日期到开闭班覆盖，最多 1000 项，优先于工作日规则；非空，业务必需。
-- 字段 cs_reception_calendar.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 字段 cs_reception_calendar.zone：IANA 时区，默认中国标准时间；非空，业务必需。
-- 唯一索引 IX_cs_reception_calendar_id：id；用于同归属业务键去重。

-- 表 cs_reception_configuration：客服 owner 的组织级分组、成员关联和助手路由，整体版本提交。；数据 owner：CustomerService；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 cs_reception_configuration.id：owner 内唯一业务标识。；非空，业务必需。
-- 字段 cs_reception_configuration.groups：四类本地接待组，账号只保存用户中心引用。；非空，业务必需。
-- 字段 cs_reception_configuration.routes：每个助手的四类主题到本地组的映射。；非空，业务必需。
-- 字段 cs_reception_configuration.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 唯一索引 IX_cs_reception_configuration_id：id；用于同归属业务键去重。

-- 表 cs_reliable_task：带租约退避死信的副作用，核心事务只登记任务。；数据 owner：CustomerService；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 cs_reliable_task.id：owner 内唯一业务标识。；非空，业务必需。
-- 字段 cs_reliable_task.assistant_config_id：服务端配置的助手引用，不随模型输出变化。；非空，业务必需。
-- 字段 cs_reliable_task.attempts：当前人工恢复周期领取次数，上限五次。；非空，业务必需。
-- 字段 cs_reliable_task.bvent_id：来源大会引用，空表示非展会场景。；可空。
-- 字段 cs_reliable_task.conversation_id：贯穿 AI、人工、调查及异步任务的会话引用。；非空，业务必需。
-- 字段 cs_reliable_task.created_at：任务登记 UTC 时间。；非空，业务必需。
-- 字段 cs_reliable_task.due_at：下次可领取 UTC 时间。；非空，业务必需。
-- 字段 cs_reliable_task.episode_id：原周期；系统任务登记时可为空。；非空，业务必需。
-- 字段 cs_reliable_task.event_key：Worker 与网关共用的稳定幂等键。；非空，业务必需。
-- 字段 cs_reliable_task.kind：HANDOFF_NOTIFY、INTERNAL、EMAIL、EMAIL_REPLY、SYSTEM_HANDOFF 或 EVENT。；非空，业务必需。
-- 字段 cs_reliable_task.last_error：不含正文邮箱的稳定失败分类。；可空。
-- 字段 cs_reliable_task.last_retried_at：最近一次人工恢复的 UTC 时间，初次任务为空。；可空。
-- 字段 cs_reliable_task.last_retried_by：最近一次人工恢复的账号引用，自动重试不覆盖。；可空。
-- 字段 cs_reliable_task.lease_id：本次随机租约，完成时需匹配。；可空。
-- 字段 cs_reliable_task.lease_until：租约 UTC 截止，崩溃后可恢复。；可空。
-- 字段 cs_reliable_task.manual_retry_count：正式客服人工恢复预算的累计次数，仅递增。；非空，业务必需。
-- 字段 cs_reliable_task.payload：受控 JSON 业务载荷，不含认证密钥。；非空，业务必需。
-- 字段 cs_reliable_task.principal_id：原会话的访客账号，仅用于归属校验。；非空，业务必需。
-- 字段 cs_reliable_task.provider_request_id：供应商明确回执中的安全 request_id，用于人工对账，不保存原始响应。；可空。
-- 字段 cs_reliable_task.state：可靠任务集中状态。；非空，业务必需。
-- 字段 cs_reliable_task.total_attempts：包括历次人工恢复周期在内的累计领取次数，仅递增。；非空，业务必需。
-- 字段 cs_reliable_task.trace_id：原始追踪标识，跨 Worker 保持。；非空，业务必需。
-- 字段 cs_reliable_task.version：从一递增的乐观并发版本，幂等重放不得递增。；非空，业务必需。
-- 查询索引 IX_cs_reliable_task_conversation_id：conversation_id；用于组织队列或 Worker 有界恢复。
-- 唯一索引 IX_cs_reliable_task_event_key：event_key；用于同归属业务键去重。
-- 查询索引 IX_cs_reliable_task_kind_state_due_at：kind, state, due_at；用于组织队列或 Worker 有界恢复。

-- 表 cs_waiting_workflow：独立的等待和联系方式聚合，期限在初始化时冻结且只按需推进。；数据 owner：CustomerService；仅该 owner 写入，其他模块经应用端口读取。
-- 字段 cs_waiting_workflow.id：owner 内唯一业务标识。；非空，业务必需。
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
-- 唯一索引 IX_cs_waiting_workflow_conversation_id_episode_id：conversation_id, episode_id；用于同归属业务键去重。

CREATE TABLE ai_conversation_generation (
    id character varying(128) NOT NULL,
    external_identity_required boolean NOT NULL,
    last_completed_turn_id character varying(128),
    survey_checked_turn_id character varying(128),
    epoch bigint NOT NULL,
    human_episode_id character varying(128),
    current_turn_id character varying(128),
    turn_lease_until timestamp with time zone,
    next_seq bigint NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    version bigint NOT NULL,
    conversation_id character varying(128) NOT NULL,
    assistant_config_id character varying(128) NOT NULL,
    bvent_id character varying(128),
    principal_id character varying(128) NOT NULL,
    CONSTRAINT "PK_ai_conversation_generation" PRIMARY KEY (id),
    CONSTRAINT ck_ai_conversation_generation_scope CHECK (id <> '' AND version >= 1),
    CONSTRAINT ck_conversation_identity CHECK (conversation_id = id AND epoch >= 1 AND next_seq >= 0)
);


CREATE TABLE cs_agent_availability (
    id character varying(128) NOT NULL,
    status integer NOT NULL,
    expires_at timestamp with time zone,
    version bigint NOT NULL,
    CONSTRAINT "PK_cs_agent_availability" PRIMARY KEY (id),
    CONSTRAINT ck_cs_agent_availability_scope CHECK (id <> '' AND version >= 1)
);


CREATE TABLE cs_chat_notification_configuration (
    id character varying(128) NOT NULL,
    enabled boolean NOT NULL,
    active_provider character varying(128) NOT NULL,
    account_code character varying(128) NOT NULL,
    base_url character varying(256) NOT NULL,
    public_base_url character varying(256) NOT NULL,
    api_token_ciphertext character varying(8192),
    chat_group text NOT NULL,
    version bigint NOT NULL,
    CONSTRAINT "PK_cs_chat_notification_configuration" PRIMARY KEY (id),
    CONSTRAINT ck_cs_chat_notification_configuration_scope CHECK (id <> '' AND version >= 1)
);


CREATE TABLE cs_reception_calendar (
    id character varying(128) NOT NULL,
    calendar_version character varying(128) NOT NULL,
    zone character varying(128) NOT NULL,
    opens character varying(128) NOT NULL,
    closes character varying(128) NOT NULL,
    overrides text NOT NULL,
    version bigint NOT NULL,
    CONSTRAINT "PK_cs_reception_calendar" PRIMARY KEY (id),
    CONSTRAINT ck_cs_reception_calendar_scope CHECK (id <> '' AND version >= 1)
);


CREATE TABLE cs_reception_configuration (
    id character varying(128) NOT NULL,
    groups text NOT NULL,
    routes text NOT NULL,
    version bigint NOT NULL,
    CONSTRAINT "PK_cs_reception_configuration" PRIMARY KEY (id),
    CONSTRAINT ck_cs_reception_configuration_scope CHECK (id <> '' AND version >= 1)
);


CREATE TABLE ai_anonymous_conversation_access (
    id character varying(128) NOT NULL,
    credential_hash character varying(64) NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    revoked_at timestamp with time zone,
    version bigint NOT NULL,
    conversation_id character varying(128) NOT NULL,
    assistant_config_id character varying(128) NOT NULL,
    bvent_id character varying(128),
    principal_id character varying(128) NOT NULL,
    CONSTRAINT "PK_ai_anonymous_conversation_access" PRIMARY KEY (id),
    CONSTRAINT ck_ai_anonymous_conversation_access_scope CHECK (id <> '' AND version >= 1),
    CONSTRAINT "FK_ai_anonymous_conversation_access_ai_conversation_generation~" FOREIGN KEY (conversation_id) REFERENCES ai_conversation_generation (id) ON DELETE RESTRICT
);


CREATE TABLE ai_conversation_external_identity (
    id character varying(128) NOT NULL,
    provider_code character varying(128) NOT NULL,
    external_auth_id character varying(512) NOT NULL,
    bound_at timestamp with time zone NOT NULL,
    version bigint NOT NULL,
    conversation_id character varying(128) NOT NULL,
    assistant_config_id character varying(128) NOT NULL,
    bvent_id character varying(128),
    principal_id character varying(128) NOT NULL,
    CONSTRAINT "PK_ai_conversation_external_identity" PRIMARY KEY (id),
    CONSTRAINT ck_ai_conversation_external_identity_scope CHECK (id <> '' AND version >= 1),
    CONSTRAINT ck_external_identity_provider CHECK (provider_code = 'ADSALE'),
    CONSTRAINT "FK_ai_conversation_external_identity_ai_conversation_generatio~" FOREIGN KEY (conversation_id) REFERENCES ai_conversation_generation (id) ON DELETE RESTRICT
);


CREATE TABLE ai_human_message (
    id character varying(128) NOT NULL,
    episode_id character varying(128) NOT NULL,
    seq bigint NOT NULL,
    sender_role character varying(128) NOT NULL,
    sender_id character varying(128) NOT NULL,
    client_msg_id character varying(128) NOT NULL,
    content character varying(65535) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    version bigint NOT NULL,
    conversation_id character varying(128) NOT NULL,
    assistant_config_id character varying(128) NOT NULL,
    bvent_id character varying(128),
    principal_id character varying(128) NOT NULL,
    CONSTRAINT "PK_ai_human_message" PRIMARY KEY (id),
    CONSTRAINT ck_ai_human_message_scope CHECK (id <> '' AND version >= 1),
    CONSTRAINT ck_message_role CHECK (sender_role IN ('VISITOR', 'AGENT') AND seq > 0),
    CONSTRAINT "FK_ai_human_message_ai_conversation_generation_conversation_id" FOREIGN KEY (conversation_id) REFERENCES ai_conversation_generation (id) ON DELETE RESTRICT
);


CREATE TABLE ai_human_receipt (
    id character varying(128) NOT NULL,
    actor_id character varying(128) NOT NULL,
    delivered_seq bigint NOT NULL,
    read_seq bigint NOT NULL,
    version bigint NOT NULL,
    conversation_id character varying(128) NOT NULL,
    assistant_config_id character varying(128) NOT NULL,
    bvent_id character varying(128),
    principal_id character varying(128) NOT NULL,
    CONSTRAINT "PK_ai_human_receipt" PRIMARY KEY (id),
    CONSTRAINT ck_ai_human_receipt_scope CHECK (id <> '' AND version >= 1),
    CONSTRAINT ck_receipt_watermark CHECK (read_seq >= 0 AND delivered_seq >= read_seq),
    CONSTRAINT "FK_ai_human_receipt_ai_conversation_generation_conversation_id" FOREIGN KEY (conversation_id) REFERENCES ai_conversation_generation (id) ON DELETE RESTRICT
);


CREATE TABLE ai_resolution_evidence (
    id character varying(128) NOT NULL,
    kind character varying(128) NOT NULL,
    turn_id character varying(128) NOT NULL,
    epoch bigint NOT NULL,
    confidence double precision,
    policy character varying(128) NOT NULL,
    fingerprint character varying(128) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    trace_id character varying(128) NOT NULL,
    version bigint NOT NULL,
    conversation_id character varying(128) NOT NULL,
    assistant_config_id character varying(128) NOT NULL,
    bvent_id character varying(128),
    principal_id character varying(128) NOT NULL,
    CONSTRAINT "PK_ai_resolution_evidence" PRIMARY KEY (id),
    CONSTRAINT ck_ai_resolution_evidence_scope CHECK (id <> '' AND version >= 1),
    CONSTRAINT "FK_ai_resolution_evidence_ai_conversation_generation_conversat~" FOREIGN KEY (conversation_id) REFERENCES ai_conversation_generation (id) ON DELETE RESTRICT
);


CREATE TABLE ai_resolution_survey (
    id character varying(128) NOT NULL,
    opened_at timestamp with time zone NOT NULL,
    epoch bigint NOT NULL,
    status integer NOT NULL,
    handoff_choice integer NOT NULL,
    source_turn_id character varying(128) NOT NULL,
    evidence character varying(128) NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    responded_at timestamp with time zone,
    displayed_at timestamp with time zone,
    version bigint NOT NULL,
    conversation_id character varying(128) NOT NULL,
    assistant_config_id character varying(128) NOT NULL,
    bvent_id character varying(128),
    principal_id character varying(128) NOT NULL,
    CONSTRAINT "PK_ai_resolution_survey" PRIMARY KEY (id),
    CONSTRAINT ck_ai_resolution_survey_scope CHECK (id <> '' AND version >= 1),
    CONSTRAINT ck_survey_state CHECK (status BETWEEN 0 AND 3 AND handoff_choice BETWEEN 0 AND 3 AND epoch >= 1),
    CONSTRAINT "FK_ai_resolution_survey_ai_conversation_generation_conversatio~" FOREIGN KEY (conversation_id) REFERENCES ai_conversation_generation (id) ON DELETE RESTRICT
);


CREATE TABLE ai_turn (
    id character varying(128) NOT NULL,
    epoch bigint NOT NULL,
    user_content character varying(65535) NOT NULL,
    assistant_content character varying(65535),
    status character varying(128) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    completed_at timestamp with time zone,
    trace_id character varying(128) NOT NULL,
    version bigint NOT NULL,
    conversation_id character varying(128) NOT NULL,
    assistant_config_id character varying(128) NOT NULL,
    bvent_id character varying(128),
    principal_id character varying(128) NOT NULL,
    CONSTRAINT "PK_ai_turn" PRIMARY KEY (id),
    CONSTRAINT ck_ai_turn_scope CHECK (id <> '' AND version >= 1),
    CONSTRAINT "FK_ai_turn_ai_conversation_generation_conversation_id" FOREIGN KEY (conversation_id) REFERENCES ai_conversation_generation (id) ON DELETE RESTRICT
);


CREATE TABLE cs_command_receipt (
    id character varying(128) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    trace_id character varying(128) NOT NULL,
    actor_id character varying(128) NOT NULL,
    action character varying(128) NOT NULL,
    request_id character varying(128) NOT NULL,
    fingerprint character varying(128) NOT NULL,
    result_json character varying(65535) NOT NULL,
    version bigint NOT NULL,
    conversation_id character varying(128) NOT NULL,
    assistant_config_id character varying(128) NOT NULL,
    bvent_id character varying(128),
    principal_id character varying(128) NOT NULL,
    CONSTRAINT "PK_cs_command_receipt" PRIMARY KEY (id),
    CONSTRAINT ck_cs_command_receipt_scope CHECK (id <> '' AND version >= 1),
    CONSTRAINT "FK_cs_command_receipt_ai_conversation_generation_conversation_~" FOREIGN KEY (conversation_id) REFERENCES ai_conversation_generation (id) ON DELETE RESTRICT
);


CREATE TABLE cs_followup_form (
    id character varying(128) NOT NULL,
    origin character varying(128) NOT NULL,
    episode_id character varying(128) NOT NULL,
    ticket_id character varying(128) NOT NULL,
    group_id character varying(128) NOT NULL,
    request_id character varying(128) NOT NULL,
    fingerprint character varying(128) NOT NULL,
    contact_name character varying(128),
    content character varying(65535) NOT NULL,
    state character varying(128) NOT NULL,
    assigned_to character varying(128),
    submitted_at timestamp with time zone NOT NULL,
    claimed_at timestamp with time zone,
    version bigint NOT NULL,
    conversation_id character varying(128) NOT NULL,
    assistant_config_id character varying(128) NOT NULL,
    bvent_id character varying(128),
    principal_id character varying(128) NOT NULL,
    CONSTRAINT "PK_cs_followup_form" PRIMARY KEY (id),
    CONSTRAINT "AK_cs_followup_form_conversation_id_episode_id" UNIQUE (conversation_id, episode_id),
    CONSTRAINT ck_cs_followup_form_scope CHECK (id <> '' AND version >= 1),
    CONSTRAINT ck_followup_assignee CHECK ((state = 'OPEN' AND assigned_to IS NULL) OR (state = 'CLAIMED' AND assigned_to IS NOT NULL)),
    CONSTRAINT ck_followup_origin CHECK (origin IN ('FORM', 'EMAIL') AND (origin <> 'FORM' OR content <> '')),
    CONSTRAINT "FK_cs_followup_form_ai_conversation_generation_conversation_id" FOREIGN KEY (conversation_id) REFERENCES ai_conversation_generation (id) ON DELETE RESTRICT
);


CREATE TABLE cs_followup_response (
    id character varying(128) NOT NULL,
    episode_id character varying(128) NOT NULL,
    ticket_id character varying(128) NOT NULL,
    actor_id character varying(128) NOT NULL,
    subject character varying(200) NOT NULL,
    content character varying(65535) NOT NULL,
    state integer NOT NULL,
    created_at timestamp with time zone NOT NULL,
    version bigint NOT NULL,
    conversation_id character varying(128) NOT NULL,
    assistant_config_id character varying(128) NOT NULL,
    bvent_id character varying(128),
    principal_id character varying(128) NOT NULL,
    CONSTRAINT "PK_cs_followup_response" PRIMARY KEY (id),
    CONSTRAINT ck_cs_followup_response_scope CHECK (id <> '' AND version >= 1),
    CONSTRAINT "FK_cs_followup_response_ai_conversation_generation_conversatio~" FOREIGN KEY (conversation_id) REFERENCES ai_conversation_generation (id) ON DELETE RESTRICT
);


CREATE TABLE cs_handoff_episode (
    id character varying(128) NOT NULL,
    status integer NOT NULL,
    trigger_source integer NOT NULL,
    reason_code integer NOT NULL,
    group_id character varying(128) NOT NULL,
    category integer NOT NULL,
    config_version bigint NOT NULL,
    classification_source character varying(128) NOT NULL,
    agent_id character varying(128),
    requested_at timestamp with time zone NOT NULL,
    claimed_at timestamp with time zone,
    first_response_at timestamp with time zone,
    auto_close_at timestamp with time zone,
    resolution_reason character varying(128),
    finished_at timestamp with time zone,
    trace_id character varying(128) NOT NULL,
    version bigint NOT NULL,
    conversation_id character varying(128) NOT NULL,
    assistant_config_id character varying(128) NOT NULL,
    bvent_id character varying(128),
    principal_id character varying(128) NOT NULL,
    CONSTRAINT "PK_cs_handoff_episode" PRIMARY KEY (id),
    CONSTRAINT ck_cs_handoff_episode_scope CHECK (id <> '' AND version >= 1),
    CONSTRAINT ck_episode_assignee CHECK (status NOT IN (1, 2, 3) OR agent_id IS NOT NULL),
    CONSTRAINT ck_episode_auto_close CHECK ((status = 3 AND resolution_reason IS NOT NULL AND resolution_reason IN ('AGENT_MANUAL', 'VISITOR_INACTIVE_5M') AND auto_close_at IS NULL) OR (status <> 3 AND resolution_reason IS NULL AND (auto_close_at IS NULL OR status = 2))),
    CONSTRAINT ck_episode_state CHECK (status BETWEEN 0 AND 4),
    CONSTRAINT "FK_cs_handoff_episode_ai_conversation_generation_conversation_~" FOREIGN KEY (conversation_id) REFERENCES ai_conversation_generation (id) ON DELETE RESTRICT
);


CREATE TABLE cs_handoff_proposal (
    id character varying(128) NOT NULL,
    turn_id character varying(128) NOT NULL,
    epoch bigint NOT NULL,
    status integer NOT NULL,
    reason_code integer NOT NULL,
    user_facing_reason character varying(500) NOT NULL,
    confidence double precision NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL,
    version bigint NOT NULL,
    conversation_id character varying(128) NOT NULL,
    assistant_config_id character varying(128) NOT NULL,
    bvent_id character varying(128),
    principal_id character varying(128) NOT NULL,
    CONSTRAINT "PK_cs_handoff_proposal" PRIMARY KEY (id),
    CONSTRAINT ck_cs_handoff_proposal_scope CHECK (id <> '' AND version >= 1),
    CONSTRAINT ck_proposal_confidence CHECK (confidence >= 0 AND confidence <= 1 AND status BETWEEN 0 AND 3),
    CONSTRAINT "FK_cs_handoff_proposal_ai_conversation_generation_conversation~" FOREIGN KEY (conversation_id) REFERENCES ai_conversation_generation (id) ON DELETE RESTRICT
);


CREATE TABLE cs_reliable_task (
    id character varying(128) NOT NULL,
    kind character varying(128) NOT NULL,
    event_key character varying(512) NOT NULL,
    episode_id character varying(128) NOT NULL,
    payload character varying(65535) NOT NULL,
    state integer NOT NULL,
    attempts integer NOT NULL,
    total_attempts bigint NOT NULL,
    manual_retry_count bigint NOT NULL,
    last_retried_by character varying(128),
    last_retried_at timestamp with time zone,
    due_at timestamp with time zone NOT NULL,
    lease_id character varying(128),
    lease_until timestamp with time zone,
    last_error character varying(128),
    provider_request_id character varying(128),
    trace_id character varying(128) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    version bigint NOT NULL,
    conversation_id character varying(128) NOT NULL,
    assistant_config_id character varying(128) NOT NULL,
    bvent_id character varying(128),
    principal_id character varying(128) NOT NULL,
    CONSTRAINT "PK_cs_reliable_task" PRIMARY KEY (id),
    CONSTRAINT ck_cs_reliable_task_scope CHECK (id <> '' AND version >= 1),
    CONSTRAINT ck_task_lease CHECK ((state = 1 AND lease_id IS NOT NULL AND lease_until IS NOT NULL) OR (state <> 1 AND lease_id IS NULL AND lease_until IS NULL)),
    CONSTRAINT ck_task_retry_audit CHECK (total_attempts >= attempts AND manual_retry_count >= 0 AND (manual_retry_count = 0 OR (last_retried_by IS NOT NULL AND last_retried_at IS NOT NULL))),
    CONSTRAINT ck_task_state CHECK (state BETWEEN 0 AND 4 AND attempts BETWEEN 0 AND 5),
    CONSTRAINT "FK_cs_reliable_task_ai_conversation_generation_conversation_id" FOREIGN KEY (conversation_id) REFERENCES ai_conversation_generation (id) ON DELETE RESTRICT
);


CREATE TABLE cs_waiting_workflow (
    id character varying(128) NOT NULL,
    episode_id character varying(128) NOT NULL,
    state integer NOT NULL,
    remind_at timestamp with time zone NOT NULL,
    busy_at timestamp with time zone NOT NULL,
    closes_at timestamp with time zone NOT NULL,
    next_opens_at timestamp with time zone NOT NULL,
    calendar_version character varying(128) NOT NULL,
    reminded boolean NOT NULL,
    email character varying(254),
    ticket_id character varying(128),
    version bigint NOT NULL,
    conversation_id character varying(128) NOT NULL,
    assistant_config_id character varying(128) NOT NULL,
    bvent_id character varying(128),
    principal_id character varying(128) NOT NULL,
    CONSTRAINT "PK_cs_waiting_workflow" PRIMARY KEY (id),
    CONSTRAINT ck_cs_waiting_workflow_scope CHECK (id <> '' AND version >= 1),
    CONSTRAINT ck_waiting_state CHECK (state BETWEEN 0 AND 6),
    CONSTRAINT "FK_cs_waiting_workflow_ai_conversation_generation_conversation~" FOREIGN KEY (conversation_id) REFERENCES ai_conversation_generation (id) ON DELETE RESTRICT
);


CREATE TABLE cs_followup_receipt (
    id character varying(128) NOT NULL,
    episode_id character varying(128) NOT NULL,
    seen_by character varying(128) NOT NULL,
    seen_at timestamp with time zone NOT NULL,
    version bigint NOT NULL,
    conversation_id character varying(128) NOT NULL,
    assistant_config_id character varying(128) NOT NULL,
    bvent_id character varying(128),
    principal_id character varying(128) NOT NULL,
    CONSTRAINT "PK_cs_followup_receipt" PRIMARY KEY (id),
    CONSTRAINT ck_cs_followup_receipt_scope CHECK (id <> '' AND version >= 1),
    CONSTRAINT "FK_cs_followup_receipt_ai_conversation_generation_conversation~" FOREIGN KEY (conversation_id) REFERENCES ai_conversation_generation (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_cs_followup_receipt_cs_followup_form_conversation_id_episod~" FOREIGN KEY (conversation_id, episode_id) REFERENCES cs_followup_form (conversation_id, episode_id) ON DELETE RESTRICT
);


CREATE UNIQUE INDEX "IX_ai_anonymous_conversation_access_conversation_id" ON ai_anonymous_conversation_access (conversation_id);


CREATE UNIQUE INDEX "IX_ai_anonymous_conversation_access_credential_hash" ON ai_anonymous_conversation_access (credential_hash);


CREATE UNIQUE INDEX "IX_ai_conversation_external_identity_conversation_id_provider_~" ON ai_conversation_external_identity (conversation_id, provider_code);


CREATE UNIQUE INDEX "IX_ai_conversation_generation_id" ON ai_conversation_generation (id);


CREATE UNIQUE INDEX "IX_ai_human_message_conversation_id_sender_id_client_msg_id" ON ai_human_message (conversation_id, sender_id, client_msg_id);


CREATE UNIQUE INDEX "IX_ai_human_message_conversation_id_seq" ON ai_human_message (conversation_id, seq);


CREATE UNIQUE INDEX "IX_ai_human_receipt_conversation_id_actor_id" ON ai_human_receipt (conversation_id, actor_id);


CREATE UNIQUE INDEX "IX_ai_resolution_evidence_conversation_id_turn_id_kind" ON ai_resolution_evidence (conversation_id, turn_id, kind);


CREATE UNIQUE INDEX "IX_ai_resolution_survey_conversation_id" ON ai_resolution_survey (conversation_id);


CREATE INDEX "IX_ai_turn_conversation_id" ON ai_turn (conversation_id);


CREATE UNIQUE INDEX "IX_cs_chat_notification_configuration_id" ON cs_chat_notification_configuration (id);


CREATE UNIQUE INDEX "IX_cs_command_receipt_conversation_id_actor_id_action_request_~" ON cs_command_receipt (conversation_id, actor_id, action, request_id);


CREATE UNIQUE INDEX "IX_cs_followup_form_conversation_id_episode_id" ON cs_followup_form (conversation_id, episode_id);


CREATE UNIQUE INDEX "IX_cs_followup_receipt_conversation_id_episode_id_seen_by" ON cs_followup_receipt (conversation_id, episode_id, seen_by);


CREATE UNIQUE INDEX "IX_cs_followup_response_conversation_id_episode_id" ON cs_followup_response (conversation_id, episode_id);


CREATE INDEX "IX_cs_handoff_episode_assistant_config_id_status" ON cs_handoff_episode (assistant_config_id, status);


CREATE INDEX "IX_cs_handoff_episode_conversation_id" ON cs_handoff_episode (conversation_id);


CREATE INDEX "IX_cs_handoff_episode_status_auto_close_at_id" ON cs_handoff_episode (status, auto_close_at, id);


CREATE INDEX "IX_cs_handoff_proposal_conversation_id" ON cs_handoff_proposal (conversation_id);


CREATE UNIQUE INDEX "IX_cs_reception_calendar_id" ON cs_reception_calendar (id);


CREATE UNIQUE INDEX "IX_cs_reception_configuration_id" ON cs_reception_configuration (id);


CREATE INDEX "IX_cs_reliable_task_conversation_id" ON cs_reliable_task (conversation_id);


CREATE UNIQUE INDEX "IX_cs_reliable_task_event_key" ON cs_reliable_task (event_key);


CREATE INDEX "IX_cs_reliable_task_kind_state_due_at" ON cs_reliable_task (kind, state, due_at);


CREATE UNIQUE INDEX "IX_cs_waiting_workflow_conversation_id_episode_id" ON cs_waiting_workflow (conversation_id, episode_id);
