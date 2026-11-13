-- 2026-11-11 简化：取消用户沉默触发调查，不再创建空闲期限表或信号来源列；已有历史记录保留，应用忽略旧空闲信号。
-- ============================================================================
-- 2026-11-11 客服及关联 AI 流程全量迭代 SQL（本方言唯一部署入口）。
-- 目的：按依赖顺序汇总 generation、转人工、AI 结束判断、调查、接待、等待、跟进、在线及路由。
-- 数据库方言：KingbaseES 8.6 PostgreSQL 兼容模式；保留 CHECK、COMMENT ON、函数及触发器。
-- 执行器必须正确识别 $ 函数体及 $cs_migration$ 匿名块；维护窗口失败先回滚并盘点对象。
-- 执行前提：备份并核对目标库；沿用已有 AI 会话/Turn，跨 owner 操作使用同一事务数据源。
-- 完整安装/开发库重跑：从头到尾顺序执行；已有表跳过，已知增量列/索引/约束缺失时补建。
-- 部分已部署：核对后可整份重跑；不删除表、不回填业务数据、不重置在线代次或申请状态。
-- 边界：面向本批既有版本和中断续跑；不自动纠正同名对象的异构定义或补齐任意历史缺列。
-- 执行前停止相关业务写入并备份；不可并发执行。CHECK/触发器会按最终规则重建，错误必须停止。
-- 数据 owner：Conversation 唯一主写 generation、人工消息正文、结束信号、调查及内容评估；
--             Customer Service 唯一主写建议、接待周期、消息索引/回执、等待、跟进、运行状态及路由。
-- 用户中心唯一主写账号、租户和组织启用；客服只保存账号引用、业务组关联及申请时的归属快照。
-- 隔离及生命周期：沿用 tenant_id/top_org_id 复合约束；按各段 owner 统一归档，不级联删除业务事实。
-- 回滚：停止相关写入并导出审计及待投递事实，由 owner 按依赖逆序审查；本文件不自动删表或数据。
-- 兼容：结构存在性保护不改变字段含义、唯一性、复合隔离或状态机；不会吞掉其他 DDL 错误。
-- 历史说明：来源段中的旧 owner、enabled 开关、阶段边界是历史上下文，现行契约以本文件及
--           specs/013-human-customer-service/configuration.md、routing-realtime.md 为准。
--           原段“一次性/禁止重跑”为历史说明，整份执行方式以本文件头和 database-20261111.md 为准。
-- 登录认证：实时连接复用登录JWT，订阅范围仅在连接内存中保存。
-- 当日业务规则：手动接单只要求登录、业务组权限及容量；在线租约仅用于展示及接收改派。
-- 原段中的“ONLINE接新单”是历史说明，以本规则为准；手动接单简化无新增结构；空闲调查专用结构已从新库脚本移除，旧库不要求删除。
-- 原来源文件只作为定位标记，已统一收录于此；不得再作为第二套独立脚本重复执行。
-- 执行顺序：
-- 1. ai_20261109_customer_service_and_resolution.sql
-- 2. ai_20261109_customer_service_reception.sql
-- 3. ai_20261109_customer_service_followup.sql
-- 4. ai_20261109_customer_service_survey_runtime.sql
-- 5. ai_20261109_customer_service_followup_response.sql
-- 6. ai_20261109_customer_service_availability.sql
-- 7. ai_20261109_customer_service_availability_lease.sql
-- 8. ai_20261109_customer_service_system.sql
-- 9. ai_20261110_customer_service_routing.sql
-- ============================================================================
-- 合并来源文件：data/sql/kingbase/ai_20261109_customer_service_and_resolution.sql
-- ============================================================================
-- 2026-11-09 handoff 与 AI Conversation 结束评价开发库结构
-- 目的：合并当前已明确 owner 的 generation、handoff episode、结束信号、survey 和内容评估 DDL。
-- 数据 owner：handoff 表由客服编排应用写入；generation、用户明确结束信号、survey 与内容评估由 AI Conversation owner 写入。
-- 执行前提：先确认开发库使用项目验证的 KingbaseES 版本、备份并核对本批核心表尚未部分执行；跨 owner 原子事务须共用数据源。
-- 边界：不创建内部系统拥有的账号/组/成员，也不创建 owner 未确认的客服在线、容量、claim、客服会话或独立消息表。
-- 方言：MySQL 8.0.16+/InnoDB；Kingbase 使用项目部署版本。两个方言必须分别执行，不可交叉执行。
-- 执行方式：一次性按本文件顺序执行；不支持并行、跳步或盲目重跑。MySQL DDL 失败可能留下已提交对象，应先盘点失败点。
-- 回滚：本文件不附自动回滚；停用相关写入并归档业务事实后，按对象依赖逆序删除本文件创建的表和 generation 唯一约束。删除会丢失转人工、审计和评价事实。
-- 兼容：仅面向开发阶段当前结构，不保留旧迁移顺序或旧 owner 的双写兼容。
-- ============================================================================
-- 来源片段：data/sql/kingbase/ai_20261106_conversation_generation/formal/add_ai_conversation_generation.sql
-- ============================================================================
-- AI Conversation 回复 owner 与 generation epoch 控制事实。
-- 执行前提：客服建议 v2 与 AI conversation 使用同一个 KingbaseES 主数据源，
--          应用已完成共享事务配置；部署前备份数据库并确认 conversation_id 全局租户内唯一。
-- 数据 owner：AI conversation 上下文唯一写入租户/组织/assistant 绑定、reply_owner 和 generation_epoch；
--             客服应用只能在确认/结束事务内调用 owner 端口变更 AI/HUMAN 状态。
-- 方言：KingbaseES 8.6 PostgreSQL 兼容方言，使用 TIMESTAMP(6)、CHECK、COMMENT ON 和 ON CONFLICT。
-- 回滚：先关闭转人工确认与 AI generation 写入，完成运行中流的收敛并归档客服 episode/Turn；
--       再 DROP TABLE ai_conversation_generation。回滚会失去当前 owner 与 epoch，禁止在线直接删除。
-- 兼容：本迁移是新契约，不双写旧客服 session mode；表中不保存聊天正文或客服账号/组主数据。
-- 删除策略：与 AI conversation 生命周期一致；会话归档/清理作业须同事务或在归档后删除本控制行。
-- ============================================================================
-- 每个租户下的 Conversation 唯一控制行；HUMAN owner_ref 指向当前 handoff episode。
CREATE TABLE IF NOT EXISTS ai_conversation_generation (
    tenant_id VARCHAR(64) NOT NULL,
    top_org_id VARCHAR(64) NOT NULL,
    conversation_id VARCHAR(128) NOT NULL,
    assistant_config_id VARCHAR(64) NOT NULL,
    bvent_id VARCHAR(64),
    reply_owner VARCHAR(16) NOT NULL,
    generation_epoch BIGINT NOT NULL,
    owner_ref VARCHAR(64),
    created_at TIMESTAMP(6) NOT NULL,
    updated_at TIMESTAMP(6) NOT NULL,
    CONSTRAINT pk_ai_conversation_generation PRIMARY KEY (tenant_id, conversation_id),
    CONSTRAINT ck_ai_conversation_generation_owner CHECK (reply_owner IN ('AI', 'HUMAN')),
    CONSTRAINT ck_ai_conversation_generation_epoch CHECK (generation_epoch >= 0),
    CONSTRAINT ck_ai_conversation_generation_ref CHECK (
        (
            reply_owner = 'AI'
            AND owner_ref IS NULL
        )
        OR (
            reply_owner = 'HUMAN'
            AND owner_ref IS NOT NULL
        )
    )
);

CREATE INDEX IF NOT EXISTS idx_ai_conversation_generation_owner ON ai_conversation_generation (tenant_id, top_org_id, reply_owner, updated_at);

COMMENT ON TABLE ai_conversation_generation IS 'AI Conversation 回复 owner 与跨节点 generation fencing 控制事实';

COMMENT ON COLUMN ai_conversation_generation.tenant_id IS '认证上下文解析得到的租户归属';

COMMENT ON COLUMN ai_conversation_generation.top_org_id IS '可信 assistant 上下文解析得到的顶级组织归属';

COMMENT ON COLUMN ai_conversation_generation.conversation_id IS '稳定 AI conversation 标识，不是客服 session 主数据';

COMMENT ON COLUMN ai_conversation_generation.assistant_config_id IS '该 Conversation 首次可信绑定的 assistant 配置';

COMMENT ON COLUMN ai_conversation_generation.bvent_id IS '可信大会范围，非大会智能体为空且后续不可改写';

COMMENT ON COLUMN ai_conversation_generation.reply_owner IS '当前唯一回复 owner：AI 或 HUMAN';

COMMENT ON COLUMN ai_conversation_generation.generation_epoch IS '每次开始 AI、转人工或人工结束时单调递增的回复代次';

COMMENT ON COLUMN ai_conversation_generation.owner_ref IS 'HUMAN 时为当前 handoff episode ID；AI 时必须为空';

COMMENT ON COLUMN ai_conversation_generation.created_at IS '控制行首次建立的 UTC 时间';

COMMENT ON COLUMN ai_conversation_generation.updated_at IS '最近一次 owner 或 epoch 变化的 UTC 时间';

-- 来源片段：data/sql/kingbase/ai_20261029_customer_service/formal/create_customer_service_handoff_proposal.sql
-- ============================================================================
-- AI conversation 转人工建议、访客决策、handoff episode 与独立可靠 Outbox。
-- 执行前提：本文件先创建 Conversation generation；handoff proposal 不依赖 cs_session 或本地客服目录表。
-- 数据 owner：客服编排服务唯一写入 proposal、episode、决策回执和 Outbox；AI conversation owner
-- 仍独占聊天记录/generation，内部客服系统仍独占客服账号、组、成员和真实 claim。
-- 方言：Kingbase 8.6 PostgreSQL 兼容方言，使用 TEXT/JSON/TIMESTAMP(6)/COMMENT ON。
-- 回滚：关闭建议与确认入口并排空/归档 Outbox 后，依次删除命令、episode、Outbox、proposal；
-- 回滚会丢失访客选择和转接审计事实，必须先归档。此迁移不提供旧版双写兼容。
-- ============================================================================
-- 一条 AI 轮次最多一条待确认建议；同轮重复 Tool 调用由应用层指纹验证后返回原 proposal。
CREATE TABLE IF NOT EXISTS cs_handoff_proposal (
    tenant_id VARCHAR(64) NOT NULL,
    top_org_id VARCHAR(64) NOT NULL,
    proposal_id VARCHAR(64) NOT NULL,
    conversation_id VARCHAR(128) NOT NULL,
    turn_id VARCHAR(128) NOT NULL,
    assistant_config_id VARCHAR(64) NOT NULL,
    bvent_id VARCHAR(64),
    trace_id VARCHAR(128) NOT NULL,
    reason_code VARCHAR(48) NOT NULL,
    confidence NUMERIC(5, 4) NOT NULL,
    user_facing_reason VARCHAR(640) NOT NULL,
    policy_version VARCHAR(64) NOT NULL,
    status VARCHAR(16) NOT NULL,
    version BIGINT NOT NULL,
    input_fingerprint CHAR(64) NOT NULL,
    created_at TIMESTAMP(6) NOT NULL,
    expires_at TIMESTAMP(6) NOT NULL,
    updated_at TIMESTAMP(6),
    CONSTRAINT pk_cs_handoff_proposal PRIMARY KEY (tenant_id, top_org_id, proposal_id),
    CONSTRAINT uq_cs_handoff_proposal_turn UNIQUE (tenant_id, top_org_id, conversation_id, turn_id),
    CONSTRAINT ck_cs_handoff_proposal_reason CHECK (
        reason_code IN (
            'UNRESOLVED_AFTER_RETRY',
            'USER_DISSATISFIED',
            'REQUIRES_HUMAN_JUDGMENT'
        )
    ),
    CONSTRAINT ck_cs_handoff_proposal_confidence CHECK (
        confidence >= 0
        AND confidence <= 1
    ),
    CONSTRAINT ck_cs_handoff_proposal_status CHECK (
        status IN ('PENDING', 'CONFIRMED', 'DISMISSED', 'EXPIRED')
    ),
    CONSTRAINT ck_cs_handoff_proposal_version CHECK (
        version > 0
        AND expires_at > created_at
    )
);

CREATE INDEX IF NOT EXISTS idx_cs_handoff_proposal_conversation ON cs_handoff_proposal (
    tenant_id,
    top_org_id,
    conversation_id,
    status,
    created_at
);

COMMENT ON TABLE cs_handoff_proposal IS '客服编排 owner 持有的 AI 转人工建议，不依赖已建立的客服 session';

COMMENT ON COLUMN cs_handoff_proposal.tenant_id IS '服务端认证得到的租户归属';

COMMENT ON COLUMN cs_handoff_proposal.top_org_id IS '服务端解析得到的顶级组织归属';

COMMENT ON COLUMN cs_handoff_proposal.proposal_id IS '服务端生成的建议卡稳定标识';

COMMENT ON COLUMN cs_handoff_proposal.conversation_id IS '已有 AI conversation 标识，不复制 conversation 消息';

COMMENT ON COLUMN cs_handoff_proposal.turn_id IS '触发建议的 AI 轮次标识';

COMMENT ON COLUMN cs_handoff_proposal.assistant_config_id IS '可信请求上下文绑定的智能体配置';

COMMENT ON COLUMN cs_handoff_proposal.bvent_id IS '可信上下文绑定的大会标识，非大会场景为空';

COMMENT ON COLUMN cs_handoff_proposal.trace_id IS '跨异步边界传播的服务端追踪标识';

COMMENT ON COLUMN cs_handoff_proposal.reason_code IS '服务端白名单校验后的建议原因';

COMMENT ON COLUMN cs_handoff_proposal.confidence IS '通过阈值的模型置信度，范围 0 到 1';

COMMENT ON COLUMN cs_handoff_proposal.user_facing_reason IS '由服务端原因码生成的访客说明，不保存模型自由文本';

COMMENT ON COLUMN cs_handoff_proposal.policy_version IS '生成建议时使用的服务端策略版本';

COMMENT ON COLUMN cs_handoff_proposal.status IS 'PENDING、CONFIRMED、DISMISSED 或 EXPIRED';

COMMENT ON COLUMN cs_handoff_proposal.version IS '每次状态转移递增的乐观锁版本';

COMMENT ON COLUMN cs_handoff_proposal.input_fingerprint IS '同一 AI 轮次建议内容摘要，发现异参重试';

COMMENT ON COLUMN cs_handoff_proposal.created_at IS '建议事实创建 UTC 时间';

COMMENT ON COLUMN cs_handoff_proposal.expires_at IS '访客确认截止 UTC 时间';

COMMENT ON COLUMN cs_handoff_proposal.updated_at IS '最近一次建议状态转移 UTC 时间';

-- AI 建议确认或访客直接申请均创建 REQUESTED；内部系统接受或认领前不得显示客服已接入。
CREATE TABLE IF NOT EXISTS cs_handoff_episode (
    tenant_id VARCHAR(64) NOT NULL,
    top_org_id VARCHAR(64) NOT NULL,
    episode_id VARCHAR(64) NOT NULL,
    conversation_id VARCHAR(128) NOT NULL,
    proposal_id VARCHAR(64),
    request_id VARCHAR(128) NOT NULL,
    assistant_config_id VARCHAR(64) NOT NULL,
    bvent_id VARCHAR(64),
    trace_id VARCHAR(128) NOT NULL,
    turn_id VARCHAR(128),
    trigger_source VARCHAR(48) NOT NULL,
    reason_code VARCHAR(48) NOT NULL,
    status VARCHAR(24) NOT NULL,
    version BIGINT NOT NULL,
    trigger_principal_id VARCHAR(128) NOT NULL,
    requested_at TIMESTAMP(6) NOT NULL,
    CONSTRAINT pk_cs_handoff_episode PRIMARY KEY (tenant_id, top_org_id, episode_id),
    CONSTRAINT uq_cs_handoff_episode_proposal UNIQUE (tenant_id, top_org_id, proposal_id),
    CONSTRAINT uq_cs_handoff_episode_request UNIQUE (
        tenant_id,
        top_org_id,
        conversation_id,
        request_id
    ),
    -- 取消意图的复合外键以此唯一键同时约束租户、组织、会话和周期归属。
    CONSTRAINT uq_cs_handoff_episode_scope UNIQUE (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id
    ),
    CONSTRAINT fk_cs_handoff_episode_proposal FOREIGN KEY (tenant_id, top_org_id, proposal_id) REFERENCES cs_handoff_proposal (tenant_id, top_org_id, proposal_id),
    CONSTRAINT ck_cs_handoff_episode_source CHECK (
        trigger_source IN (
            'AI_RECOMMENDATION_CONFIRMED',
            'VISITOR_DIRECT_REQUEST'
        )
    ),
    CONSTRAINT ck_cs_handoff_episode_source_ref CHECK (
        (
            trigger_source = 'AI_RECOMMENDATION_CONFIRMED'
            AND proposal_id IS NOT NULL
            AND turn_id IS NOT NULL
        )
        OR (
            trigger_source = 'VISITOR_DIRECT_REQUEST'
            AND proposal_id IS NULL
            AND turn_id IS NULL
        )
    ),
    CONSTRAINT ck_cs_handoff_episode_reason CHECK (
        reason_code IN (
            'UNRESOLVED_AFTER_RETRY',
            'USER_DISSATISFIED',
            'REQUIRES_HUMAN_JUDGMENT',
            'USER_REQUESTED'
        )
    ),
    CONSTRAINT ck_cs_handoff_episode_status CHECK (status IN ('REQUESTED')),
    CONSTRAINT ck_cs_handoff_episode_version CHECK (version > 0)
);

CREATE INDEX IF NOT EXISTS idx_cs_handoff_episode_conversation ON cs_handoff_episode (
    tenant_id,
    top_org_id,
    conversation_id,
    requested_at
);

COMMENT ON TABLE cs_handoff_episode IS '客服编排 owner 持有的已确认或访客直接申请转人工周期；不代表外部客服接入成功';

COMMENT ON COLUMN cs_handoff_episode.tenant_id IS '服务端认证得到的租户归属';

COMMENT ON COLUMN cs_handoff_episode.top_org_id IS '服务端解析得到的顶级组织归属';

COMMENT ON COLUMN cs_handoff_episode.episode_id IS '一次访客人工处理周期标识';

COMMENT ON COLUMN cs_handoff_episode.conversation_id IS '关联的既有 AI conversation 标识';

COMMENT ON COLUMN cs_handoff_episode.proposal_id IS 'AI 建议确认来源时的 proposal 标识；访客直接申请为空';

COMMENT ON COLUMN cs_handoff_episode.request_id IS '创建该 episode 的访客命令幂等键';

COMMENT ON COLUMN cs_handoff_episode.assistant_config_id IS '可信上下文绑定的智能体配置';

COMMENT ON COLUMN cs_handoff_episode.bvent_id IS '可信上下文绑定的大会标识';

COMMENT ON COLUMN cs_handoff_episode.trace_id IS '关联 Tool、确认命令和 Outbox 的追踪标识';

COMMENT ON COLUMN cs_handoff_episode.turn_id IS 'AI 建议确认来源的轮次；访客直接申请为空';

COMMENT ON COLUMN cs_handoff_episode.trigger_source IS '转人工触发来源：访客直接申请或 AI 建议确认';

COMMENT ON COLUMN cs_handoff_episode.reason_code IS '来源对应的固定业务原因码，直接申请为 USER_REQUESTED';

COMMENT ON COLUMN cs_handoff_episode.status IS 'REQUESTED 表示本地已记录意图，不表示内部系统已接受或认领';

COMMENT ON COLUMN cs_handoff_episode.version IS 'episode 状态版本，后续状态机转移单调递增';

COMMENT ON COLUMN cs_handoff_episode.trigger_principal_id IS '触发转人工申请的服务端认证访客主体';

COMMENT ON COLUMN cs_handoff_episode.requested_at IS '确认事实提交 UTC 时间';

-- 访客直接申请命令回执；requestId 同参重放返回原 episode，异参重用必须拒绝。
CREATE TABLE IF NOT EXISTS cs_handoff_direct_command (
    tenant_id VARCHAR(64) NOT NULL,
    top_org_id VARCHAR(64) NOT NULL,
    conversation_id VARCHAR(128) NOT NULL,
    request_id VARCHAR(128) NOT NULL,
    principal_id VARCHAR(128) NOT NULL,
    input_fingerprint CHAR(64) NOT NULL,
    episode_id VARCHAR(64) NOT NULL,
    episode_version BIGINT NOT NULL,
    created_at TIMESTAMP(6) NOT NULL,
    -- 唯一键按租户、组织、会话和 requestId 限制重复命令只保留一份回执。
    CONSTRAINT pk_cs_handoff_direct_command PRIMARY KEY (
        tenant_id,
        top_org_id,
        conversation_id,
        request_id
    ),
    -- 外键保证命令回执只能指向同租户和组织下真实创建的 episode。
    CONSTRAINT fk_cs_handoff_direct_command_episode FOREIGN KEY (tenant_id, top_org_id, episode_id) REFERENCES cs_handoff_episode (tenant_id, top_org_id, episode_id),
    -- 初始回执只接受新建 episode 的版本 1。
    CONSTRAINT ck_cs_handoff_direct_command_version CHECK (episode_version = 1)
);

CREATE INDEX IF NOT EXISTS idx_cs_handoff_direct_command_episode ON cs_handoff_direct_command (tenant_id, top_org_id, episode_id);

COMMENT ON INDEX idx_cs_handoff_direct_command_episode IS '按接待周期追溯直接申请回执';

COMMENT ON TABLE cs_handoff_direct_command IS '访客直接转人工命令的稳定幂等回执';

COMMENT ON COLUMN cs_handoff_direct_command.tenant_id IS '服务端认证得到的租户归属';

COMMENT ON COLUMN cs_handoff_direct_command.top_org_id IS '服务端 Conversation owner 核验的顶级组织';

COMMENT ON COLUMN cs_handoff_direct_command.conversation_id IS '已授权 AI conversation 标识';

COMMENT ON COLUMN cs_handoff_direct_command.request_id IS '访客重试复用的直接申请幂等键';

COMMENT ON COLUMN cs_handoff_direct_command.principal_id IS '认证 Adapter 解析的访客主体';

COMMENT ON COLUMN cs_handoff_direct_command.input_fingerprint IS '命令归属摘要，发现同键异参重用';

COMMENT ON COLUMN cs_handoff_direct_command.episode_id IS '首次直接申请创建的唯一接待周期';

COMMENT ON COLUMN cs_handoff_direct_command.episode_version IS '直接申请初始 episode 版本，固定为 1';

COMMENT ON COLUMN cs_handoff_direct_command.created_at IS '幂等回执创建 UTC 时间';

-- 确认/拒绝命令回执；requestId 重用异参时拒绝，重复确认同一 proposal 返回原 episode。
CREATE TABLE IF NOT EXISTS cs_handoff_proposal_command (
    tenant_id VARCHAR(64) NOT NULL,
    top_org_id VARCHAR(64) NOT NULL,
    conversation_id VARCHAR(128) NOT NULL,
    request_id VARCHAR(128) NOT NULL,
    proposal_id VARCHAR(64) NOT NULL,
    principal_id VARCHAR(128) NOT NULL,
    decision VARCHAR(16) NOT NULL,
    input_fingerprint CHAR(64) NOT NULL,
    proposal_status VARCHAR(16) NOT NULL,
    episode_id VARCHAR(64),
    proposal_version BIGINT NOT NULL,
    created_at TIMESTAMP(6) NOT NULL,
    CONSTRAINT pk_cs_handoff_proposal_command PRIMARY KEY (
        tenant_id,
        top_org_id,
        conversation_id,
        request_id
    ),
    CONSTRAINT fk_cs_handoff_proposal_command_proposal FOREIGN KEY (tenant_id, top_org_id, proposal_id) REFERENCES cs_handoff_proposal (tenant_id, top_org_id, proposal_id),
    CONSTRAINT ck_cs_handoff_proposal_command_decision CHECK (decision IN ('CONFIRM', 'DISMISS')),
    CONSTRAINT ck_cs_handoff_proposal_command_status CHECK (
        proposal_status IN ('CONFIRMED', 'DISMISSED', 'EXPIRED')
    ),
    CONSTRAINT ck_cs_handoff_proposal_command_result CHECK (
        (
            proposal_status = 'CONFIRMED'
            AND episode_id IS NOT NULL
        )
        OR (
            proposal_status <> 'CONFIRMED'
            AND episode_id IS NULL
        )
    ),
    CONSTRAINT ck_cs_handoff_proposal_command_version CHECK (proposal_version > 0)
);

CREATE INDEX IF NOT EXISTS idx_cs_handoff_proposal_command_proposal ON cs_handoff_proposal_command (tenant_id, top_org_id, proposal_id);

COMMENT ON TABLE cs_handoff_proposal_command IS '客服建议确认/拒绝的命令幂等回执';

COMMENT ON COLUMN cs_handoff_proposal_command.tenant_id IS '服务端认证得到的租户归属';

COMMENT ON COLUMN cs_handoff_proposal_command.top_org_id IS '服务端解析得到的顶级组织归属';

COMMENT ON COLUMN cs_handoff_proposal_command.conversation_id IS '命令已授权的 AI conversation 标识';

COMMENT ON COLUMN cs_handoff_proposal_command.request_id IS '访客重试复用的命令幂等键';

COMMENT ON COLUMN cs_handoff_proposal_command.proposal_id IS '命令指向的建议卡标识';

COMMENT ON COLUMN cs_handoff_proposal_command.principal_id IS '服务端认证得到的访客主体';

COMMENT ON COLUMN cs_handoff_proposal_command.decision IS 'CONFIRM 或 DISMISS';

COMMENT ON COLUMN cs_handoff_proposal_command.input_fingerprint IS '请求主体、动作和版本的规范化 SHA-256 摘要';

COMMENT ON COLUMN cs_handoff_proposal_command.proposal_status IS '提交后的建议卡状态';

COMMENT ON COLUMN cs_handoff_proposal_command.episode_id IS '确认后唯一 episode 标识，拒绝或过期时为空';

COMMENT ON COLUMN cs_handoff_proposal_command.proposal_version IS '提交后的建议版本';

COMMENT ON COLUMN cs_handoff_proposal_command.created_at IS '命令回执 UTC 创建时间';

-- 独立 handoff Outbox；不引用 cs_event/cs_session，使 AI conversation 未建客服 session 时仍可原子提交。
CREATE TABLE IF NOT EXISTS cs_handoff_orchestration_outbox (
    tenant_id VARCHAR(64) NOT NULL,
    top_org_id VARCHAR(64) NOT NULL,
    outbox_id VARCHAR(64) NOT NULL,
    event_id VARCHAR(64) NOT NULL,
    event_type VARCHAR(64) NOT NULL,
    conversation_id VARCHAR(128) NOT NULL,
    aggregate_id VARCHAR(64) NOT NULL,
    assistant_config_id VARCHAR(64) NOT NULL,
    trace_id VARCHAR(128) NOT NULL,
    turn_id VARCHAR(128),
    bvent_id VARCHAR(64),
    schema_version INT NOT NULL,
    payload_json TEXT NOT NULL,
    status VARCHAR(16) NOT NULL,
    attempt_count INT NOT NULL,
    max_attempts INT NOT NULL,
    next_attempt_at TIMESTAMP(6) NOT NULL,
    lease_until TIMESTAMP(6),
    claimed_by VARCHAR(128),
    last_error VARCHAR(512),
    created_at TIMESTAMP(6) NOT NULL,
    delivered_at TIMESTAMP(6),
    CONSTRAINT pk_cs_handoff_orchestration_outbox PRIMARY KEY (tenant_id, top_org_id, outbox_id),
    CONSTRAINT uq_cs_handoff_orchestration_event UNIQUE (tenant_id, top_org_id, event_id),
    CONSTRAINT ck_cs_handoff_orchestration_attempts CHECK (
        attempt_count >= 0
        AND max_attempts > 0
        AND attempt_count <= max_attempts
    ),
    CONSTRAINT ck_cs_handoff_orchestration_status CHECK (
        status IN ('PENDING', 'CLAIMED', 'DELIVERED', 'DEAD')
    ),
    CONSTRAINT ck_cs_handoff_orchestration_schema CHECK (schema_version = 2)
);

CREATE INDEX IF NOT EXISTS idx_cs_handoff_orchestration_outbox_due ON cs_handoff_orchestration_outbox (status, next_attempt_at, created_at);

COMMENT ON TABLE cs_handoff_orchestration_outbox IS 'AI conversation handoff 事件独立 Outbox，支持持久化后恢复投递';

COMMENT ON COLUMN cs_handoff_orchestration_outbox.tenant_id IS '服务端认证得到的租户归属';

COMMENT ON COLUMN cs_handoff_orchestration_outbox.top_org_id IS '服务端解析得到的顶级组织归属';

COMMENT ON COLUMN cs_handoff_orchestration_outbox.outbox_id IS '可靠投递任务稳定幂等标识';

COMMENT ON COLUMN cs_handoff_orchestration_outbox.event_id IS 'handoff 业务事件唯一标识';

COMMENT ON COLUMN cs_handoff_orchestration_outbox.event_type IS 'handoff.proposed、handoff.requested 或建议决策事件';

COMMENT ON COLUMN cs_handoff_orchestration_outbox.conversation_id IS '关联的 AI conversation，不等同客服 session';

COMMENT ON COLUMN cs_handoff_orchestration_outbox.aggregate_id IS 'proposal 或 handoff episode 关联对象标识';

COMMENT ON COLUMN cs_handoff_orchestration_outbox.assistant_config_id IS '可信上下文智能体配置标识';

COMMENT ON COLUMN cs_handoff_orchestration_outbox.trace_id IS '供异步消费者继续传播的追踪标识';

COMMENT ON COLUMN cs_handoff_orchestration_outbox.turn_id IS '关联 AI 轮次；访客直接申请可为空';

COMMENT ON COLUMN cs_handoff_orchestration_outbox.bvent_id IS '可信上下文大会标识';

COMMENT ON COLUMN cs_handoff_orchestration_outbox.schema_version IS '事件 envelope 契约版本，当前为 2';

COMMENT ON COLUMN cs_handoff_orchestration_outbox.payload_json IS '由服务端序列化校验的脱敏 JSON 事件载荷';

COMMENT ON COLUMN cs_handoff_orchestration_outbox.status IS 'PENDING、CLAIMED、DELIVERED 或 DEAD';

COMMENT ON COLUMN cs_handoff_orchestration_outbox.attempt_count IS '累计投递尝试次数';

COMMENT ON COLUMN cs_handoff_orchestration_outbox.max_attempts IS '有限重试上限，耗尽后进入 DEAD';

COMMENT ON COLUMN cs_handoff_orchestration_outbox.next_attempt_at IS '首次等于创建时间，失败后按有界退避推进';

COMMENT ON COLUMN cs_handoff_orchestration_outbox.lease_until IS '领取租约截止时间，过期后允许恢复领取';

COMMENT ON COLUMN cs_handoff_orchestration_outbox.claimed_by IS '当前 Worker 租约持有者';

COMMENT ON COLUMN cs_handoff_orchestration_outbox.last_error IS '脱敏最近失败摘要，不包含凭据和聊天正文';

COMMENT ON COLUMN cs_handoff_orchestration_outbox.created_at IS '与业务事实同事务创建时间';

COMMENT ON COLUMN cs_handoff_orchestration_outbox.delivered_at IS '下游确认后的 UTC 时间，未送达为空';

-- 来源片段：data/sql/kingbase/ai_20261029_customer_service/formal/create_customer_service_handoff_cancellation.sql
-- ============================================================================
-- 目的：保存访客取消人工的意图和幂等回执，禁止将请求取消冒充外部已撤销。
-- 方言：KingbaseES PostgreSQL 兼容模式；依赖 CHECK、复合唯一键和外键。
-- 前提：先执行 create_customer_service_handoff_proposal.sql，含 uq_cs_handoff_episode_scope。
-- 数据 owner/写入方：客服编排服务；读取方：访客恢复应用服务、未来内部系统取消 Adapter。
-- 生命周期：与 episode 一同保留审计，不做逻辑删除，不单独删除幂等回执；经批准统一归档。
-- 所有非空字段均为重放、授权、审计必要事实；租户/组织/会话用复合外键防止跨范围引用。
-- 初始状态唯一为 PENDING_OWNER_ACK；未来确认契约明确后再扩展终态和回执，不提供旧版双写。
-- 回滚：先关闭取消入口及相关投递，归档取消和 Outbox 事实，再删除本表；不得清除未确认意图。
-- 回滚不恢复 AI，也不撤销外部申请；父周期删除使用默认 RESTRICT，禁止级联丢失取消审计。
-- ============================================================================
CREATE TABLE IF NOT EXISTS cs_handoff_cancellation (
    tenant_id VARCHAR(64) NOT NULL,
    top_org_id VARCHAR(64) NOT NULL,
    conversation_id VARCHAR(128) NOT NULL,
    episode_id VARCHAR(64) NOT NULL,
    cancellation_id VARCHAR(64) NOT NULL,
    request_id VARCHAR(128) NOT NULL,
    principal_id VARCHAR(128) NOT NULL,
    expected_episode_version BIGINT NOT NULL,
    input_fingerprint CHAR(64) NOT NULL,
    trace_id VARCHAR(128) NOT NULL,
    status VARCHAR(24) NOT NULL,
    created_at TIMESTAMP(6) NOT NULL,
    -- 主键按租户和组织隔离稳定取消引用。
    CONSTRAINT pk_cs_handoff_cancellation PRIMARY KEY (tenant_id, top_org_id, cancellation_id),
    -- 一周期至多一个待确认取消意图，避免不同命令重复通知。
    CONSTRAINT uq_cs_handoff_cancellation_episode UNIQUE (tenant_id, top_org_id, episode_id),
    -- 一会话内幂等键不能用于不同周期或期望版本。
    CONSTRAINT uq_cs_handoff_cancellation_request UNIQUE (
        tenant_id,
        top_org_id,
        conversation_id,
        request_id
    ),
    -- 全范围外键禁止将取消意图挂到其他组织或会话的 episode。
    CONSTRAINT fk_cs_handoff_cancellation_episode FOREIGN KEY (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id
    ) REFERENCES cs_handoff_episode (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id
    ),
    -- 未接通取消回执前仅允许持久化待确认意图。
    CONSTRAINT ck_cs_handoff_cancellation_status CHECK (status IN ('PENDING_OWNER_ACK')),
    -- 取消命令必须引用用户确实看到的有效状态版本。
    CONSTRAINT ck_cs_handoff_cancellation_version CHECK (expected_episode_version > 0)
);

COMMENT ON TABLE cs_handoff_cancellation IS '客服编排 owner 保存的取消意图及稳定幂等回执，不代表外部已取消';

COMMENT ON COLUMN cs_handoff_cancellation.tenant_id IS '正式认证与 Conversation owner 核验的租户';

COMMENT ON COLUMN cs_handoff_cancellation.top_org_id IS 'Conversation 持久化顶级组织';

COMMENT ON COLUMN cs_handoff_cancellation.conversation_id IS '取消意图所属的已授权 AI 会话';

COMMENT ON COLUMN cs_handoff_cancellation.episode_id IS '申请撤销的人工周期，必须属于同会话';

COMMENT ON COLUMN cs_handoff_cancellation.cancellation_id IS '服务端生成的取消意图标识及下游幂等引用';

COMMENT ON COLUMN cs_handoff_cancellation.request_id IS '访客重试必须复用的命令幂等键';

COMMENT ON COLUMN cs_handoff_cancellation.principal_id IS '正式认证的原申请访客主体，应用层与周期校验';

COMMENT ON COLUMN cs_handoff_cancellation.expected_episode_version IS '提交取消时访客看到的周期版本，大于零';

COMMENT ON COLUMN cs_handoff_cancellation.input_fingerprint IS '主体、会话路由、周期和期望版本的 SHA-256 摘要';

COMMENT ON COLUMN cs_handoff_cancellation.trace_id IS '首次取消命令的服务端追踪标识，传播至 Outbox';

COMMENT ON COLUMN cs_handoff_cancellation.status IS 'PENDING_OWNER_ACK 仅表示等待外部 owner 确认，AI 不恢复';

COMMENT ON COLUMN cs_handoff_cancellation.created_at IS '取消意图首次持久化 UTC 时间，重放不修改';

-- 来源片段：data/sql/kingbase/ai_20261107_ai_resolution/formal/create_ai_resolution_candidate.sql
-- ============================================================================
-- 目的：保存模型候选、确定性用户明确结束信号；只记录事实，不创建问卷、不关闭会话、不改变回复 owner。
-- 方言：KingbaseES PostgreSQL 兼容模式，依赖 CHECK、复合唯一约束和外键。
-- 执行顺序：所需父表已在本合并脚本前文创建。
-- 数据 owner/唯一写入方：AI Conversation 应用服务；读取方：未来的结束资格判断应用服务。
-- 生命周期：候选与用户明确结束信号为不可变审计事实，不做逻辑删除；新版不创建或处理空闲期限。
-- 新消息/owner 变化使 generation_epoch 失效，但不删除旧候选；读取前必须核验当前 AI 代次。
-- 租户/组织/会话复合外键阻止跨范围引用；assistant、大会、代次由 owner 行锁核验，不由模型指定。
-- 执行方式：本迁移只执行一次，可在变更事务内执行；父表新增约束和建表均无 IF NOT EXISTS。
-- 重复执行前先核查约束和表，发生失败应回滚迁移事务或从已核对的失败步骤修复。
-- 回滚：保持 Tool/Worker 禁用，停止写入并归档事实后先删除三个子表，再移除父表新增唯一约束；
--       不修改 generation 事实，不将删除候选解释为取消人工或恢复 AI。无旧版双写/兼容要求。
-- ============================================================================
-- 由 Conversation owner 增加完整组织范围唯一键，仅用于候选归属外键，不改变原主键。
-- 约束按当前 schema 和所属表核验，缺失才补建。
DO $cs_migration$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.table_constraints WHERE constraint_schema=current_schema() AND table_name='ai_conversation_generation' AND constraint_name='uq_ai_generation_resolution_scope') THEN
    ALTER TABLE ai_conversation_generation
  ADD CONSTRAINT uq_ai_generation_resolution_scope UNIQUE (tenant_id, top_org_id, conversation_id);
  END IF;
END;
$cs_migration$;

CREATE TABLE IF NOT EXISTS ai_resolution_candidate (
    tenant_id VARCHAR(64) NOT NULL,
    top_org_id VARCHAR(64) NOT NULL,
    conversation_id VARCHAR(128) NOT NULL,
    resolution_episode_id VARCHAR(64) NOT NULL,
    assistant_config_id VARCHAR(64) NOT NULL,
    bvent_id VARCHAR(64),
    turn_id VARCHAR(128) NOT NULL,
    generation_epoch BIGINT NOT NULL,
    trace_id VARCHAR(128) NOT NULL,
    signal_type VARCHAR(32) NOT NULL,
    confidence DOUBLE PRECISION NOT NULL,
    policy_version VARCHAR(64) NOT NULL,
    input_fingerprint CHAR(64) NOT NULL,
    proposed_at TIMESTAMP(6) NOT NULL,
    -- 主键隔离租户和组织，稳定引用不能被不同范围复用。
    CONSTRAINT pk_ai_resolution_candidate PRIMARY KEY (tenant_id, top_org_id, resolution_episode_id),
    -- 同轮唯一：重复工具回调不得生成多个候选或覆盖不同置信输入。
    CONSTRAINT uq_ai_resolution_candidate_turn UNIQUE (tenant_id, top_org_id, conversation_id, turn_id),
    -- 禁止引用另一组织的 Conversation；默认 RESTRICT，不级联丢失审计。
    CONSTRAINT fk_ai_resolution_candidate_scope FOREIGN KEY (tenant_id, top_org_id, conversation_id) REFERENCES ai_conversation_generation (tenant_id, top_org_id, conversation_id),
    -- 非空字段均为绑定、重放、时效性或审计必要信息；大会是唯一可空范围字段。
    CONSTRAINT ck_ai_resolution_candidate_epoch CHECK (generation_epoch > 0),
    CONSTRAINT ck_ai_resolution_candidate_confidence CHECK (
        confidence >= 0
        AND confidence <= 1
    ),
    CONSTRAINT ck_ai_resolution_candidate_signal CHECK (signal_type = 'MODEL_POSSIBLE_RESOLUTION')
);

-- 为当前 generation 的候选恢复提供有界定位，不扫描聊天正文或客服表。
CREATE INDEX IF NOT EXISTS idx_ai_resolution_candidate_generation ON ai_resolution_candidate (
    tenant_id,
    top_org_id,
    conversation_id,
    generation_epoch
);

COMMENT ON TABLE ai_resolution_candidate IS 'Conversation owner 的不可变 AI 结束候选；无关闭对话或发送评价副作用';

COMMENT ON COLUMN ai_resolution_candidate.tenant_id IS '可信请求快照中的租户，须匹配 Conversation owner';

COMMENT ON COLUMN ai_resolution_candidate.top_org_id IS '服务端 assistant 解析的顶级组织，须匹配 Conversation owner';

COMMENT ON COLUMN ai_resolution_candidate.conversation_id IS '既有 AI 会话标识，不是独立客服 session';

COMMENT ON COLUMN ai_resolution_candidate.resolution_episode_id IS '服务端生成的稳定结束候选引用，不等于问卷或已解决事实';

COMMENT ON COLUMN ai_resolution_candidate.assistant_config_id IS '可信会话绑定的智能体配置，应用层加 owner 行锁核验';

COMMENT ON COLUMN ai_resolution_candidate.bvent_id IS '可信大会引用，组织级助手无大会时为空';

COMMENT ON COLUMN ai_resolution_candidate.turn_id IS '提出候选的真实 AI 轮次，同轮只允许一条候选';

COMMENT ON COLUMN ai_resolution_candidate.generation_epoch IS '提出候选时的正数 AI 代次，后续代次变化使其失去当前资格';

COMMENT ON COLUMN ai_resolution_candidate.trace_id IS '首次记录的追踪标识，重试不覆盖';

COMMENT ON COLUMN ai_resolution_candidate.signal_type IS '唯一值 MODEL_POSSIBLE_RESOLUTION，不能写用户确认';

COMMENT ON COLUMN ai_resolution_candidate.confidence IS '模型零到一置信度，服务端阈值决定是否记录，不等于用户满意';

COMMENT ON COLUMN ai_resolution_candidate.policy_version IS '服务端候选策略版本，默认 ai-resolution-candidate-v1';

COMMENT ON COLUMN ai_resolution_candidate.input_fingerprint IS '归属、轮次、代次、置信度和策略的 SHA-256，不含 trace';

COMMENT ON COLUMN ai_resolution_candidate.proposed_at IS '首次记录 UTC 时间，幂等重放不修改';

-- 确定性用户结束事实；分类证据正文仅在事务内读取，表中只保留消息引用和摘要。
CREATE TABLE IF NOT EXISTS ai_resolution_user_signal (
    tenant_id VARCHAR(64) NOT NULL,
    top_org_id VARCHAR(64) NOT NULL,
    conversation_id VARCHAR(128) NOT NULL,
    resolution_signal_id VARCHAR(64) NOT NULL,
    assistant_config_id VARCHAR(64) NOT NULL,
    bvent_id VARCHAR(64),
    turn_id VARCHAR(128) NOT NULL,
    generation_epoch BIGINT NOT NULL,
    source_user_detail_id VARCHAR(50) NOT NULL,
    previous_assistant_turn_id VARCHAR(128),
    previous_assistant_detail_id VARCHAR(50),
    trace_id VARCHAR(128) NOT NULL,
    signal_type VARCHAR(48) NOT NULL,
    policy_version VARCHAR(64) NOT NULL,
    evidence_fingerprint CHAR(64) NOT NULL,
    recorded_at TIMESTAMP(6) NOT NULL,
    -- 信号主键包含范围；同 Turn 同类型只允许一个确定性事实，仅由用户明确表达产生。
    CONSTRAINT pk_ai_resolution_user_signal PRIMARY KEY (tenant_id, top_org_id, resolution_signal_id),
    CONSTRAINT uq_ai_resolution_user_signal_turn UNIQUE (
        tenant_id,
        top_org_id,
        conversation_id,
        turn_id,
        signal_type
    ),
    -- Conversation owner 外键阻止跨租户/组织会话引用，默认 RESTRICT 保留审计。
    CONSTRAINT fk_ai_resolution_user_signal_scope FOREIGN KEY (tenant_id, top_org_id, conversation_id) REFERENCES ai_conversation_generation (tenant_id, top_org_id, conversation_id),
    CONSTRAINT ck_ai_resolution_user_signal_epoch CHECK (generation_epoch > 0),
    CONSTRAINT ck_ai_resolution_user_signal_type CHECK (
        signal_type IN (
            'USER_EXPLICIT_END',
            'USER_CONFIRMED_NO_FURTHER_QUESTION'
        )
    ),
    CONSTRAINT ck_ai_resolution_user_signal_previous CHECK (
        (
            signal_type = 'USER_CONFIRMED_NO_FURTHER_QUESTION'
            AND previous_assistant_turn_id IS NOT NULL
            AND previous_assistant_detail_id IS NOT NULL
        )
        OR (
            signal_type = 'USER_EXPLICIT_END'
            AND previous_assistant_turn_id IS NULL
            AND previous_assistant_detail_id IS NULL
        )
    )
);

CREATE INDEX IF NOT EXISTS idx_ai_resolution_user_signal_generation ON ai_resolution_user_signal (
    tenant_id,
    top_org_id,
    conversation_id,
    generation_epoch
);

COMMENT ON TABLE ai_resolution_user_signal IS 'Conversation owner 的不可变用户结束信号；无问卷或人工状态副作用';

COMMENT ON COLUMN ai_resolution_user_signal.tenant_id IS '可信请求快照中的租户，须匹配 Conversation owner';

COMMENT ON COLUMN ai_resolution_user_signal.top_org_id IS '服务端解析的顶级组织，须匹配 Conversation owner';

COMMENT ON COLUMN ai_resolution_user_signal.conversation_id IS '既有 AI conversation 标识';

COMMENT ON COLUMN ai_resolution_user_signal.resolution_signal_id IS '服务端生成的稳定用户结束信号引用';

COMMENT ON COLUMN ai_resolution_user_signal.assistant_config_id IS '可信 conversation 绑定的智能体配置';

COMMENT ON COLUMN ai_resolution_user_signal.bvent_id IS '可信大会范围；组织级助手为空';

COMMENT ON COLUMN ai_resolution_user_signal.turn_id IS '用户表达及成功 AI 回复所属 Turn';

COMMENT ON COLUMN ai_resolution_user_signal.generation_epoch IS '写入时持有的 AI 回复代次；新代次或人工 owner 使其失效';

COMMENT ON COLUMN ai_resolution_user_signal.source_user_detail_id IS '触发分类的已保存用户消息引用，不复制正文';

COMMENT ON COLUMN ai_resolution_user_signal.previous_assistant_turn_id IS '否定短答成立时上一条成功追问的 Turn';

COMMENT ON COLUMN ai_resolution_user_signal.previous_assistant_detail_id IS '否定短答成立时上一条成功追问的消息引用';

COMMENT ON COLUMN ai_resolution_user_signal.trace_id IS '成功 AI Turn 的追踪引用';

COMMENT ON COLUMN ai_resolution_user_signal.signal_type IS '服务端确定性类型：USER_EXPLICIT_END、USER_CONFIRMED_NO_FURTHER_QUESTION';

COMMENT ON COLUMN ai_resolution_user_signal.policy_version IS '服务端确定性结束规则版本';

COMMENT ON COLUMN ai_resolution_user_signal.evidence_fingerprint IS '范围、消息引用、代次、类型和规则的 SHA-256，不含正文';

COMMENT ON COLUMN ai_resolution_user_signal.recorded_at IS '首次记录 UTC 时间，同 Turn 重放不改写';

-- 来源片段：data/sql/kingbase/ai_20261107_ai_resolution/formal/create_ai_resolution_survey.sql
-- ============================================================================
-- 目的：持久化 Conversation owner 每个会话唯一的 AI 满意度调查及可靠 Outbox 状态事件。
-- 方言：KingbaseES PostgreSQL 兼容模式，依赖 CHECK、复合外键和强制唯一约束。
-- 执行顺序：所需父表已在本合并脚本前文创建。
-- 数据 owner/唯一写入方：AI Conversation 应用服务；客服 owner 只消费 handoff opt-in Outbox。
-- 生命周期：survey 保留 OPEN/SATISFIED/DISSATISFIED/EXPIRED 和 opt-in 选择；Outbox 保留投递/死信审计，不逻辑删除。
-- 租户/组织/会话通过 generation owner 复合外键隔离；一会话唯一约束保证调查至多创建一次。
-- 执行方式：本迁移只执行一次，可在迁移事务内运行；重复执行前先核对约束和表。
-- 回滚：停止创建和消费并归档事实后先删除 Outbox 再删除 survey；不改 Conversation owner 或人工事实。
-- ============================================================================
CREATE TABLE IF NOT EXISTS ai_resolution_survey (
    tenant_id VARCHAR(64) NOT NULL,
    top_org_id VARCHAR(64) NOT NULL,
    conversation_id VARCHAR(128) NOT NULL,
    survey_id VARCHAR(64) NOT NULL,
    resolution_episode_id VARCHAR(64) NOT NULL,
    assistant_config_id VARCHAR(64) NOT NULL,
    bvent_id VARCHAR(64),
    generation_epoch BIGINT NOT NULL,
    turn_id VARCHAR(128) NOT NULL,
    source_type VARCHAR(32) NOT NULL,
    source_ref VARCHAR(64) NOT NULL,
    trace_id VARCHAR(128) NOT NULL,
    policy_version VARCHAR(64) NOT NULL,
    status VARCHAR(24) NOT NULL,
    handoff_choice VARCHAR(16) NOT NULL,
    opened_at TIMESTAMP(6) NOT NULL,
    expires_at TIMESTAMP(6) NOT NULL,
    responded_at TIMESTAMP(6),
    version BIGINT NOT NULL,
    CONSTRAINT pk_ai_resolution_survey PRIMARY KEY (tenant_id, top_org_id, survey_id),
    CONSTRAINT uq_ai_resolution_survey_conversation UNIQUE (tenant_id, top_org_id, conversation_id),
    CONSTRAINT uq_ai_resolution_survey_episode UNIQUE (tenant_id, top_org_id, resolution_episode_id),
    CONSTRAINT uq_ai_resolution_survey_source UNIQUE (tenant_id, top_org_id, source_type, source_ref),
    CONSTRAINT fk_ai_resolution_survey_scope FOREIGN KEY (tenant_id, top_org_id, conversation_id) REFERENCES ai_conversation_generation (tenant_id, top_org_id, conversation_id),
    CONSTRAINT ck_ai_resolution_survey_epoch CHECK (generation_epoch > 0),
    CONSTRAINT ck_ai_resolution_survey_version CHECK (version > 0),
    CONSTRAINT ck_ai_resolution_survey_time CHECK (expires_at > opened_at),
    -- 调查响应必须发生在开放时间到截止时间区间内，与回答命令的截止边界一致。
    CONSTRAINT ck_ai_resolution_survey_response_time CHECK (
        responded_at IS NULL
        OR (
            responded_at >= opened_at
            AND responded_at < expires_at
        )
    ),
    CONSTRAINT ck_ai_resolution_survey_source CHECK (
        source_type IN ('MODEL_CANDIDATE', 'CONVERSATION_SIGNAL')
    ),
    CONSTRAINT ck_ai_resolution_survey_status CHECK (
        status IN ('OPEN', 'SATISFIED', 'DISSATISFIED', 'EXPIRED')
    ),
    CONSTRAINT ck_ai_resolution_survey_handoff CHECK (
        handoff_choice IN (
            'NOT_APPLICABLE',
            'PENDING',
            'OPTED_IN',
            'DECLINED'
        )
    ),
    CONSTRAINT ck_ai_resolution_survey_state CHECK (
        (
            status = 'OPEN'
            AND handoff_choice = 'NOT_APPLICABLE'
            AND responded_at IS NULL
        )
        OR (
            status = 'SATISFIED'
            AND handoff_choice = 'NOT_APPLICABLE'
            AND responded_at IS NOT NULL
        )
        OR (
            status = 'DISSATISFIED'
            AND handoff_choice IN ('PENDING', 'OPTED_IN', 'DECLINED')
            AND responded_at IS NOT NULL
        )
        OR (
            status = 'EXPIRED'
            AND handoff_choice = 'NOT_APPLICABLE'
            AND responded_at IS NULL
        )
    )
);

CREATE INDEX IF NOT EXISTS idx_ai_resolution_survey_expiration ON ai_resolution_survey (
    status,
    expires_at,
    tenant_id,
    top_org_id,
    survey_id
);

COMMENT ON TABLE ai_resolution_survey IS 'Conversation owner 的单次 AI 满意度调查状态';

COMMENT ON COLUMN ai_resolution_survey.tenant_id IS '可信 conversation 租户归属';

COMMENT ON COLUMN ai_resolution_survey.top_org_id IS '可信 conversation 顶级组织归属';

COMMENT ON COLUMN ai_resolution_survey.conversation_id IS '调查唯一范围；每个会话最多一份 AI survey';

COMMENT ON COLUMN ai_resolution_survey.survey_id IS '服务端生成且可安全恢复的调查标识';

COMMENT ON COLUMN ai_resolution_survey.resolution_episode_id IS 'Conversation owner 生成的本次解决候选周期标识';

COMMENT ON COLUMN ai_resolution_survey.assistant_config_id IS '当前可信会话智能体绑定';

COMMENT ON COLUMN ai_resolution_survey.bvent_id IS '可信大会范围，组织级助手为空';

COMMENT ON COLUMN ai_resolution_survey.generation_epoch IS '创建调查时的 AI 回复代次，必须由 owner 行锁核验';

COMMENT ON COLUMN ai_resolution_survey.turn_id IS '建立结束候选的成功 AI Turn';

COMMENT ON COLUMN ai_resolution_survey.source_type IS '来源类型：MODEL_CANDIDATE 或 CONVERSATION_SIGNAL';

COMMENT ON COLUMN ai_resolution_survey.source_ref IS '模型候选或确定性信号的稳定来源引用';

COMMENT ON COLUMN ai_resolution_survey.trace_id IS '调查创建链路追踪引用，不含聊天正文';

COMMENT ON COLUMN ai_resolution_survey.policy_version IS '调查资格与期限策略版本';

COMMENT ON COLUMN ai_resolution_survey.status IS '调查状态：OPEN/SATISFIED/DISSATISFIED/EXPIRED';

COMMENT ON COLUMN ai_resolution_survey.handoff_choice IS '不满意后的人工选择：NOT_APPLICABLE/PENDING/OPTED_IN/DECLINED';

COMMENT ON COLUMN ai_resolution_survey.opened_at IS '调查卡首次创建 UTC 时间';

COMMENT ON COLUMN ai_resolution_survey.expires_at IS '调查截止 UTC 时间；到期只标记 EXPIRED';

COMMENT ON COLUMN ai_resolution_survey.responded_at IS '首次满意度回答 UTC 时间';

COMMENT ON COLUMN ai_resolution_survey.version IS 'survey 状态版本，客户端命令必须带 expectedVersion';

-- survey 状态与待投递事件同事务写入；不能以数据库存在事件表示访客或客服 owner 已收到。
CREATE TABLE IF NOT EXISTS ai_resolution_survey_outbox (
    tenant_id VARCHAR(64) NOT NULL,
    top_org_id VARCHAR(64) NOT NULL,
    outbox_id VARCHAR(64) NOT NULL,
    event_id VARCHAR(64) NOT NULL,
    survey_id VARCHAR(64) NOT NULL,
    conversation_id VARCHAR(128) NOT NULL,
    event_type VARCHAR(64) NOT NULL,
    state_version BIGINT NOT NULL,
    payload_json TEXT NOT NULL,
    status VARCHAR(16) NOT NULL,
    attempt_count INT NOT NULL DEFAULT 0,
    max_attempts INT NOT NULL DEFAULT 10,
    next_attempt_at TIMESTAMP(6) NOT NULL,
    lease_until TIMESTAMP(6),
    claimed_by VARCHAR(128),
    delivered_at TIMESTAMP(6),
    last_error VARCHAR(128),
    created_at TIMESTAMP(6) NOT NULL,
    CONSTRAINT pk_ai_resolution_survey_outbox PRIMARY KEY (tenant_id, top_org_id, outbox_id),
    CONSTRAINT uq_ai_resolution_survey_event UNIQUE (tenant_id, top_org_id, event_id),
    CONSTRAINT uq_ai_resolution_survey_transition_event UNIQUE (
        tenant_id,
        top_org_id,
        survey_id,
        event_type,
        state_version
    ),
    CONSTRAINT fk_ai_resolution_survey_outbox_survey FOREIGN KEY (tenant_id, top_org_id, survey_id) REFERENCES ai_resolution_survey (tenant_id, top_org_id, survey_id),
    CONSTRAINT ck_ai_resolution_survey_outbox_version CHECK (state_version > 0),
    CONSTRAINT ck_ai_resolution_survey_outbox_attempts CHECK (
        attempt_count >= 0
        AND max_attempts > 0
        AND attempt_count <= max_attempts
    ),
    CONSTRAINT ck_ai_resolution_survey_outbox_status CHECK (
        status IN ('PENDING', 'CLAIMED', 'DELIVERED', 'DEAD')
    )
);

CREATE INDEX IF NOT EXISTS idx_ai_resolution_survey_outbox_claim ON ai_resolution_survey_outbox (status, next_attempt_at, lease_until, created_at);

COMMENT ON TABLE ai_resolution_survey_outbox IS 'AI 满意度状态迁移的可靠 Outbox；无 Worker 时事件保持 PENDING';

COMMENT ON COLUMN ai_resolution_survey_outbox.tenant_id IS '事件所属可信租户';

COMMENT ON COLUMN ai_resolution_survey_outbox.top_org_id IS '事件所属可信组织';

COMMENT ON COLUMN ai_resolution_survey_outbox.outbox_id IS '内部投递任务标识';

COMMENT ON COLUMN ai_resolution_survey_outbox.event_id IS '跨节点重试保持稳定的事件标识';

COMMENT ON COLUMN ai_resolution_survey_outbox.survey_id IS '关联的单次 AI 满意度调查';

COMMENT ON COLUMN ai_resolution_survey_outbox.conversation_id IS '关联 AI conversation，不是客服 session';

COMMENT ON COLUMN ai_resolution_survey_outbox.event_type IS 'AI survey 状态事件或 handoff opt-in 事件类型';

COMMENT ON COLUMN ai_resolution_survey_outbox.state_version IS '产生该事件的 survey 状态版本';

COMMENT ON COLUMN ai_resolution_survey_outbox.payload_json IS '仅含稳定业务引用、状态和版本，不含聊天正文或联系方式';

COMMENT ON COLUMN ai_resolution_survey_outbox.status IS '投递状态：PENDING/CLAIMED/DELIVERED/DEAD';

COMMENT ON COLUMN ai_resolution_survey_outbox.attempt_count IS '已领取投递次数';

COMMENT ON COLUMN ai_resolution_survey_outbox.max_attempts IS '最多尝试次数，耗尽后保留 DEAD 运维线索';

COMMENT ON COLUMN ai_resolution_survey_outbox.next_attempt_at IS '首次可投递时间及失败后的重试时间 UTC';

COMMENT ON COLUMN ai_resolution_survey_outbox.lease_until IS 'CLAIMED 任务租约截止时间 UTC';

COMMENT ON COLUMN ai_resolution_survey_outbox.claimed_by IS '当前投递 Worker 租约持有人';

COMMENT ON COLUMN ai_resolution_survey_outbox.delivered_at IS '下游确认接收时间，不表示用户已查看';

COMMENT ON COLUMN ai_resolution_survey_outbox.last_error IS '最后一次非敏感错误类型';

COMMENT ON COLUMN ai_resolution_survey_outbox.created_at IS '事件与状态同事务写入时间 UTC';

-- 来源片段：data/sql/kingbase/ai_20261108_ai_resolution_assessment/formal/create_ai_survey_content_assessment.sql
-- ============================================================================
-- 目的：持久化完整对话内容评估的结论和证据引用，供 AI 满意度问卷资格门禁复核。
-- 方言：KingbaseES PostgreSQL 兼容模式，依赖 CHECK、复合外键和唯一约束。
-- 执行顺序：所需父表已在本合并脚本前文创建。
-- 数据 owner/唯一写入方：AI Conversation 应用服务；问卷资格服务只读取，不直接修改评估事实。
-- 生命周期：每个 Conversation generation/成功 Turn 一份不可变评估；新消息或人工接管后旧评估保留审计但不得用于当前问卷。
-- 数据最小化：只保留消息引用、分类结果、策略版本、评估引用和 UTC 时间，不复制聊天正文或评估提示词。
-- 执行前提：评估器须先在行锁事务外完成完整历史判断，再由 owner 在锁内复核 generation 后写入；不得在数据库锁内调用模型。
-- 执行方式：本迁移只执行一次，可在变更事务中执行；重复执行前先核查对象，失败时回滚事务或从已核对步骤修复。
-- 回滚/兼容：保持问卷入口关闭并归档评估事实后删除本表；不修改 generation、问卷或人工 owner 状态，无旧版双写兼容要求。
-- ============================================================================
CREATE TABLE IF NOT EXISTS ai_survey_content_assessment (
    tenant_id VARCHAR(64) NOT NULL,
    top_org_id VARCHAR(64) NOT NULL,
    conversation_id VARCHAR(128) NOT NULL,
    assessment_id VARCHAR(64) NOT NULL,
    assistant_config_id VARCHAR(64) NOT NULL,
    bvent_id VARCHAR(64),
    generation_epoch BIGINT NOT NULL,
    turn_id VARCHAR(128) NOT NULL,
    source_user_detail_id VARCHAR(50) NOT NULL,
    policy_version VARCHAR(64) NOT NULL,
    verdict VARCHAR(24) NOT NULL,
    input_fingerprint CHAR(64) NOT NULL,
    assessed_at TIMESTAMP(6) NOT NULL,
    CONSTRAINT pk_ai_survey_content_assessment PRIMARY KEY (tenant_id, top_org_id, assessment_id),
    -- 同一代次和成功 Turn 只接受一个不可变结论，阻止重试或并发产生冲突资格。
    CONSTRAINT uq_ai_survey_assessment_turn UNIQUE (
        tenant_id,
        top_org_id,
        conversation_id,
        generation_epoch,
        turn_id
    ),
    CONSTRAINT fk_ai_survey_assessment_scope FOREIGN KEY (tenant_id, top_org_id, conversation_id) REFERENCES ai_conversation_generation (tenant_id, top_org_id, conversation_id),
    CONSTRAINT ck_ai_survey_assessment_epoch CHECK (generation_epoch > 0),
    CONSTRAINT ck_ai_survey_assessment_verdict CHECK (
        verdict IN (
            'ELIGIBLE',
            'GREETING_ONLY',
            'SPAM',
            'PROMOTION',
            'UNRESOLVED',
            'UNKNOWN'
        )
    )
);

-- 当前代次资格查询按可信路由精确定位，不扫描聊天正文或客服 owner 表。
CREATE INDEX IF NOT EXISTS idx_ai_survey_assessment_route ON ai_survey_content_assessment (
    tenant_id,
    top_org_id,
    conversation_id,
    assistant_config_id,
    generation_epoch
);

COMMENT ON TABLE ai_survey_content_assessment IS 'Conversation owner 的不可变问卷内容评估结论和非正文证据引用';

COMMENT ON COLUMN ai_survey_content_assessment.tenant_id IS '可信 Conversation 租户归属';

COMMENT ON COLUMN ai_survey_content_assessment.top_org_id IS '服务端解析的顶级组织归属';

COMMENT ON COLUMN ai_survey_content_assessment.conversation_id IS '被评估的稳定 AI conversation 标识';

COMMENT ON COLUMN ai_survey_content_assessment.assessment_id IS '评估器或 owner 生成的稳定结论引用，不是聊天内容';

COMMENT ON COLUMN ai_survey_content_assessment.assistant_config_id IS '可信会话绑定的智能体配置';

COMMENT ON COLUMN ai_survey_content_assessment.bvent_id IS '可信大会范围；组织级助手为空';

COMMENT ON COLUMN ai_survey_content_assessment.generation_epoch IS '评估时持有的 AI generation，新消息或人工接管后失效';

COMMENT ON COLUMN ai_survey_content_assessment.turn_id IS '评估覆盖到的当前成功 AI Turn';

COMMENT ON COLUMN ai_survey_content_assessment.source_user_detail_id IS '当前轮次已保存用户消息引用，不复制正文';

COMMENT ON COLUMN ai_survey_content_assessment.policy_version IS '完整内容评估器的规则或模型策略版本';

COMMENT ON COLUMN ai_survey_content_assessment.verdict IS '完整对话结论：ELIGIBLE/GREETING_ONLY/SPAM/PROMOTION/UNRESOLVED/UNKNOWN';

COMMENT ON COLUMN ai_survey_content_assessment.input_fingerprint IS '可信范围、消息引用、结论与策略的 SHA-256，不含正文';

COMMENT ON COLUMN ai_survey_content_assessment.assessed_at IS '评估完成并由 owner 接受的 UTC 时间';

-- 合并来源文件结束：data/sql/kingbase/ai_20261109_customer_service_and_resolution.sql
-- 合并来源文件：data/sql/kingbase/ai_20261109_customer_service_reception.sql
-- 目的：兑现本项目作为接单/接待 owner、Conversation 作为人工消息 owner 的 v2 接口。
-- 前提：已执行 ai_20261109_customer_service_and_resolution.sql；这是已升级开发库的增量，不要重跑基线。
-- 方言：KingbaseES / PostgreSQL 兼容模式。单次执行，先备份；DDL 应在维护窗口事务中提交，执行失败需定位后续跑，禁止清空已有申请。
-- 回滚：先关闭 customer-service.reception.enabled，再按数据 owner 留存要求导出新增事实；存在新状态时不可直接恢复旧 CHECK。
-- 归属：所有表均以 tenant/topOrg/conversation 隔离，复合外键拒绝跨会话引用；不存客服账号/组主数据。
-- 生命周期：不逻辑删除消息/命令/认领事实，沿用会话审计留存；本脚本不自动清理业务数据。
ALTER TABLE cs_handoff_episode
DROP CONSTRAINT IF EXISTS ck_cs_handoff_episode_status;

ALTER TABLE cs_handoff_episode
ADD CONSTRAINT ck_cs_handoff_episode_status CHECK (
    status IN (
        'REQUESTED',
        'CLAIMED',
        'ACTIVE',
        'RESOLVED',
        'CANCELLED'
    )
);

-- 历史待取消意图仅供审计；本地接单/取消分别原子收敛为 REJECTED/CONFIRMED，不再产生异步取消意图。
ALTER TABLE cs_handoff_cancellation
DROP CONSTRAINT IF EXISTS ck_cs_handoff_cancellation_status;

ALTER TABLE cs_handoff_cancellation
ADD CONSTRAINT ck_cs_handoff_cancellation_status CHECK (
    status IN ('PENDING_OWNER_ACK', 'CONFIRMED', 'REJECTED')
);

-- Customer Service owner：一周期只允许一个客服认领；由接单事务写，授权与恢复只读。
CREATE TABLE IF NOT EXISTS cs_handoff_claim (
    tenant_id VARCHAR(64) NOT NULL,
    top_org_id VARCHAR(64) NOT NULL,
    conversation_id VARCHAR(128) NOT NULL,
    episode_id VARCHAR(64) NOT NULL,
    agent_id VARCHAR(128) NOT NULL,
    group_id VARCHAR(64) NOT NULL,
    claimed_at TIMESTAMP(6) NOT NULL,
    PRIMARY KEY (tenant_id, top_org_id, episode_id),
    CONSTRAINT fk_cs_claim_episode FOREIGN KEY (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id
    ) REFERENCES cs_handoff_episode (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id
    )
);

-- Customer Service owner：命令幂等回执，状态与回执/Outbox 同事务，不允许异参复用幂等键。
CREATE TABLE IF NOT EXISTS cs_reception_command (
    tenant_id VARCHAR(64) NOT NULL,
    top_org_id VARCHAR(64) NOT NULL,
    conversation_id VARCHAR(128) NOT NULL,
    episode_id VARCHAR(64) NOT NULL,
    request_id VARCHAR(128) NOT NULL,
    actor_id VARCHAR(128) NOT NULL,
    action VARCHAR(24) NOT NULL,
    fingerprint CHAR(64) NOT NULL,
    result_status VARCHAR(24) NOT NULL,
    result_version BIGINT NOT NULL,
    created_at TIMESTAMP(6) NOT NULL,
    trace_id VARCHAR(128) NOT NULL,
    PRIMARY KEY (
        tenant_id,
        top_org_id,
        conversation_id,
        request_id
    ),
    CONSTRAINT fk_cs_reception_command_episode FOREIGN KEY (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id
    ) REFERENCES cs_handoff_episode (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id
    ),
    CONSTRAINT ck_cs_reception_command_action CHECK (action IN ('CLAIM', 'RESOLVE', 'CANCEL')),
    CONSTRAINT ck_cs_reception_command_version CHECK (result_version > 0)
);

-- Conversation owner：人工模式双方消息及可重放投递日志；应用锁 generation 后分配 seq，同事务写正文。
CREATE TABLE IF NOT EXISTS ai_human_message (
    tenant_id VARCHAR(64) NOT NULL,
    top_org_id VARCHAR(64) NOT NULL,
    conversation_id VARCHAR(128) NOT NULL,
    seq BIGINT NOT NULL,
    message_id VARCHAR(64) NOT NULL,
    episode_id VARCHAR(64) NOT NULL,
    sender_role VARCHAR(16) NOT NULL,
    sender_id VARCHAR(128) NOT NULL,
    client_msg_id VARCHAR(128) NOT NULL,
    content VARCHAR(4000) NOT NULL,
    trace_id VARCHAR(128) NOT NULL,
    created_at TIMESTAMP(6) NOT NULL,
    PRIMARY KEY (tenant_id, top_org_id, conversation_id, seq),
    UNIQUE (tenant_id, top_org_id, message_id),
    UNIQUE (
        tenant_id,
        top_org_id,
        conversation_id,
        sender_id,
        client_msg_id
    ),
    CONSTRAINT fk_ai_human_message_scope FOREIGN KEY (tenant_id, top_org_id, conversation_id) REFERENCES ai_conversation_generation (tenant_id, top_org_id, conversation_id),
    CONSTRAINT fk_ai_human_message_episode FOREIGN KEY (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id
    ) REFERENCES cs_handoff_episode (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id
    ),
    CONSTRAINT ck_ai_human_message_role CHECK (sender_role IN ('VISITOR', 'AGENT')),
    CONSTRAINT ck_ai_human_message_seq CHECK (seq > 0),
    CONSTRAINT ck_ai_human_message_content CHECK (CHAR_LENGTH(content) BETWEEN 1 AND 4000)
);

-- Conversation owner：每个参与者在同会话的接收/已读水位，只能单调推进，不以 HTTP 发送成功冒充对端收到。
CREATE TABLE IF NOT EXISTS ai_human_receipt (
    tenant_id VARCHAR(64) NOT NULL,
    top_org_id VARCHAR(64) NOT NULL,
    conversation_id VARCHAR(128) NOT NULL,
    principal_id VARCHAR(128) NOT NULL,
    delivered_seq BIGINT NOT NULL,
    read_seq BIGINT NOT NULL,
    updated_at TIMESTAMP(6) NOT NULL,
    PRIMARY KEY (
        tenant_id,
        top_org_id,
        conversation_id,
        principal_id
    ),
    CONSTRAINT fk_ai_human_receipt_scope FOREIGN KEY (tenant_id, top_org_id, conversation_id) REFERENCES ai_conversation_generation (tenant_id, top_org_id, conversation_id),
    CONSTRAINT ck_ai_human_receipt_watermark CHECK (
        read_seq >= 0
        AND delivered_seq >= read_seq
    )
);

COMMENT ON TABLE cs_handoff_claim IS '客服接待 owner 的唯一认领事实，不维护账号主数据';

COMMENT ON COLUMN cs_handoff_claim.tenant_id IS '认证租户';

COMMENT ON COLUMN cs_handoff_claim.top_org_id IS '可信组织';

COMMENT ON COLUMN cs_handoff_claim.conversation_id IS '原 AI 会话引用';

COMMENT ON COLUMN cs_handoff_claim.episode_id IS '唯一接待周期';

COMMENT ON COLUMN cs_handoff_claim.agent_id IS '经用户中心核验的登录账号稳定引用';

COMMENT ON COLUMN cs_handoff_claim.group_id IS '服务器路由且已核验成员资格的外部分组';

COMMENT ON COLUMN cs_handoff_claim.claimed_at IS '首次原子接单 UTC 时间';

COMMENT ON TABLE cs_reception_command IS '接待状态命令不可变幂等回执';

COMMENT ON COLUMN cs_reception_command.tenant_id IS '认证租户';

COMMENT ON COLUMN cs_reception_command.top_org_id IS '可信组织';

COMMENT ON COLUMN cs_reception_command.conversation_id IS '原 AI 会话';

COMMENT ON COLUMN cs_reception_command.episode_id IS '目标周期';

COMMENT ON COLUMN cs_reception_command.request_id IS '调用者稳定幂等键';

COMMENT ON COLUMN cs_reception_command.actor_id IS '认证主体';

COMMENT ON COLUMN cs_reception_command.action IS 'CLAIM/RESOLVE/CANCEL';

COMMENT ON COLUMN cs_reception_command.fingerprint IS '身份、参数和版本 SHA256';

COMMENT ON COLUMN cs_reception_command.result_status IS '首次命令返回的状态';

COMMENT ON COLUMN cs_reception_command.result_version IS '首次命令返回版本';

COMMENT ON COLUMN cs_reception_command.created_at IS '首次写入 UTC 时间';

COMMENT ON COLUMN cs_reception_command.trace_id IS '完整业务链路追踪';

COMMENT ON TABLE ai_human_message IS 'Conversation owner 人工模式消息及断线恢复日志';

COMMENT ON COLUMN ai_human_message.tenant_id IS '可信租户';

COMMENT ON COLUMN ai_human_message.top_org_id IS '可信组织';

COMMENT ON COLUMN ai_human_message.conversation_id IS '同一 AI 会话';

COMMENT ON COLUMN ai_human_message.seq IS '同会话单调递增消息水位';

COMMENT ON COLUMN ai_human_message.message_id IS '服务器生成稳定消息 ID';

COMMENT ON COLUMN ai_human_message.episode_id IS '消息所属接待周期引用';

COMMENT ON COLUMN ai_human_message.sender_role IS 'VISITOR 或 AGENT，服务端决定';

COMMENT ON COLUMN ai_human_message.sender_id IS '认证发送主体';

COMMENT ON COLUMN ai_human_message.client_msg_id IS '发送端重试复用幂等键';

COMMENT ON COLUMN ai_human_message.content IS '纯文本正文，禁止作为 HTML 执行';

COMMENT ON COLUMN ai_human_message.trace_id IS '消息链路追踪';

COMMENT ON COLUMN ai_human_message.created_at IS '持久化 UTC 时间';

COMMENT ON TABLE ai_human_receipt IS 'Conversation owner 参与者接收与已读水位';

COMMENT ON COLUMN ai_human_receipt.tenant_id IS '可信租户';

COMMENT ON COLUMN ai_human_receipt.top_org_id IS '可信组织';

COMMENT ON COLUMN ai_human_receipt.conversation_id IS '原 AI 会话';

COMMENT ON COLUMN ai_human_receipt.principal_id IS '认证回执提交者';

COMMENT ON COLUMN ai_human_receipt.delivered_seq IS '已确认收到的最大消息序列，缺省 0';

COMMENT ON COLUMN ai_human_receipt.read_seq IS '已读最大序列，缺省 0，不能超过接收水位';

COMMENT ON COLUMN ai_human_receipt.updated_at IS '最近实际推进 UTC 时间';

-- 合并来源文件结束：data/sql/kingbase/ai_20261109_customer_service_reception.sql
-- 合并来源文件：data/sql/kingbase/ai_20261109_customer_service_followup.sql
-- 目的：客服等待/邮件跟进与可靠通知增量；方言 Kingbase PostgreSQL 兼容模式。
-- 前提：已执行 20261109 customer_service_and_resolution 与 reception；部署前备份，脚本一次执行。
-- owner：客服上下文唯一写 workflow/delivery；Conversation 只经应用端口恢复回复权；通知网关只受理。
-- 生命周期：随接待审计保留，无自动物理删除/逻辑删除；联系方式按既定策略人工脱敏，回滚先停 Worker，再导出后删新表。
-- 所有必填字段均 NOT NULL；主键/外键防止跨租户组织挂接；SENT 不证明邮件最终到达或已读。
CREATE TABLE IF NOT EXISTS cs_waiting_workflow (
    -- 认证租户，隔离主键
    tenant_id VARCHAR(64) NOT NULL,
    -- 可信顶级组织，隔离主键
    top_org_id VARCHAR(64) NOT NULL,
    -- Conversation owner 会话引用
    conversation_id VARCHAR(128) NOT NULL,
    -- 客服周期引用，一周期一等待事实
    episode_id VARCHAR(64) NOT NULL,
    -- 不可变助手路由引用
    assistant_config_id VARCHAR(64) NOT NULL,
    -- 可空大会引用
    bvent_id VARCHAR(64),
    -- WAITING/BUSY/CONTINUING/NEED_EMAIL/FOLLOW_UP/ACCEPTED/CANCELLED
    state VARCHAR(20) NOT NULL,
    -- 正数 CAS 版本
    version BIGINT NOT NULL,
    -- UTC T+5 内部提醒期限
    remind_at TIMESTAMP NOT NULL,
    -- UTC T+10 访客选择期限
    busy_at TIMESTAMP NOT NULL,
    -- 申请时日历的营业结束 UTC 期限
    closes_at TIMESTAMP NOT NULL,
    -- 下次营业 UTC 提示
    next_opens_at TIMESTAMP NOT NULL,
    -- 申请时冻结的营业策略版本
    calendar_version VARCHAR(64) NOT NULL,
    -- 内部提醒已入 Outbox，不代表受理
    reminded BOOLEAN NOT NULL,
    -- 访客选择邮件时校验的收件地址，仅通知 Adapter 可读
    email VARCHAR(254),
    -- 有效邮箱确认后生成的唯一跟进工单引用
    ticket_id VARCHAR(64),
    -- 同周期唯一等待聚合，删除前须先处理通知。
    PRIMARY KEY (tenant_id, top_org_id, episode_id),
    -- 通知复合外键核对会话，不允许跨会话引用。
    CONSTRAINT uq_cs_waiting_scope UNIQUE (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id
    ),
    -- 接待 owner 引用完整组织范围，不级联删除业务审计。
    CONSTRAINT fk_cs_waiting_episode FOREIGN KEY (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id
    ) REFERENCES cs_handoff_episode (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id
    ),
    CONSTRAINT ck_cs_waiting_state CHECK (
        state IN (
            'WAITING',
            'BUSY',
            'CONTINUING',
            'NEED_EMAIL',
            'FOLLOW_UP',
            'ACCEPTED',
            'CANCELLED'
        )
    ),
    CONSTRAINT ck_cs_waiting_version CHECK (version > 0),
    -- 邮件跟进必须具有校验后的联系方式与唯一工单引用。
    CONSTRAINT ck_cs_waiting_followup CHECK (
        state <> 'FOLLOW_UP'
        OR (
            email IS NOT NULL
            AND ticket_id IS NOT NULL
        )
    )
);

CREATE TABLE IF NOT EXISTS cs_followup_delivery (
    -- 周期与通知类型组成的幂等键，通知网关必须持久去重
    event_key VARCHAR(100) NOT NULL,
    -- 租户隔离
    tenant_id VARCHAR(64) NOT NULL,
    -- 组织隔离
    top_org_id VARCHAR(64) NOT NULL,
    -- 审计会话引用
    conversation_id VARCHAR(128) NOT NULL,
    -- 所属等待周期
    episode_id VARCHAR(64) NOT NULL,
    -- INTERNAL 内部提醒 / EMAIL 访客跟进
    kind VARCHAR(16) NOT NULL,
    -- PENDING/CLAIMED/SENT/DEAD；SENT 仅网关受理
    status VARCHAR(16) NOT NULL,
    -- 已领取次数，自动投递最多五次
    attempts INTEGER NOT NULL,
    -- UTC 退避重试期限
    next_attempt_at TIMESTAMP NOT NULL,
    -- 投递租约随机标识
    lease_id VARCHAR(64),
    -- UTC 租约期限，崩溃可接管
    lease_until TIMESTAMP,
    -- 全局事件键便于网关持久去重，周期 ID 为服务端随机引用。
    PRIMARY KEY (event_key),
    -- 同一业务周期和通知类型只能登记一次。
    CONSTRAINT uq_cs_followup_kind UNIQUE (tenant_id, top_org_id, episode_id, kind),
    -- 完整组织归属约束，不允许向其他会话投递邮件。
    CONSTRAINT fk_cs_delivery_workflow FOREIGN KEY (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id
    ) REFERENCES cs_waiting_workflow (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id
    ),
    CONSTRAINT ck_cs_delivery_kind CHECK (kind IN ('INTERNAL', 'EMAIL')),
    CONSTRAINT ck_cs_delivery_status CHECK (status IN ('PENDING', 'CLAIMED', 'SENT', 'DEAD')),
    CONSTRAINT ck_cs_delivery_attempts CHECK (attempts >= 0),
    -- 已领取任务必须持有可过期接管的完整租约。
    CONSTRAINT ck_cs_delivery_lease CHECK (
        status <> 'CLAIMED'
        OR (
            lease_id IS NOT NULL
            AND lease_until IS NOT NULL
        )
    )
);

-- 仅服务于到期投递扫描，不改变业务唯一性。
CREATE INDEX IF NOT EXISTS ix_cs_delivery_due ON cs_followup_delivery (status, next_attempt_at, lease_until);

-- 待推进状态索引，避免重复扫描已接单/结束工单。
CREATE INDEX IF NOT EXISTS ix_cs_waiting_due ON cs_waiting_workflow (state, closes_at, busy_at);

-- 合并来源文件结束：data/sql/kingbase/ai_20261109_customer_service_followup.sql
-- 合并来源文件：data/sql/kingbase/ai_20261109_customer_service_survey_runtime.sql
-- 目的：记录新会话调查历史起点，并允许用户明确同意后的调查转人工来源；Kingbase PostgreSQL 兼容方言。
-- 前提：先执行 customer_service_and_resolution 和 reception；部署前备份，脚本只执行一次。
-- owner：Conversation 创建时写起点，客服 owner 维护接待周期；旧会话不得回填起点。
-- 起点与会话审计同生命周期，不逻辑删除；删除/回滚前先关闭调查并按 owner 要求归档。
-- 恢复旧约束前必须确认无 SURVEY_DISSATISFIED_OPT_IN 数据，避免丢失真实业务事实。
CREATE TABLE IF NOT EXISTS ai_survey_conversation_origin (
    tenant_id VARCHAR(64) NOT NULL,
    top_org_id VARCHAR(64) NOT NULL,
    conversation_id VARCHAR(128) NOT NULL,
    assistant_config_id VARCHAR(64) NOT NULL,
    bvent_id VARCHAR(64),
    registered_at TIMESTAMP(6) NOT NULL,
    PRIMARY KEY (tenant_id, top_org_id, conversation_id)
);

COMMENT ON TABLE ai_survey_conversation_origin IS 'Conversation owner 的调查资格起点；本项目接待 owner 的历史覆盖完整';

COMMENT ON COLUMN ai_survey_conversation_origin.tenant_id IS '可信租户归属';

COMMENT ON COLUMN ai_survey_conversation_origin.top_org_id IS '可信顶级组织归属';

COMMENT ON COLUMN ai_survey_conversation_origin.conversation_id IS '新建会话引用，不为旧会话回填';

COMMENT ON COLUMN ai_survey_conversation_origin.assistant_config_id IS '首次绑定的智能体配置';

COMMENT ON COLUMN ai_survey_conversation_origin.bvent_id IS '可信大会引用';

COMMENT ON COLUMN ai_survey_conversation_origin.registered_at IS '新会话起点登记 UTC 时间';

-- 客服 owner 唯一写入调查引用；读取方为审计和接待查询，随周期保留，不级联删除调查。
ALTER TABLE cs_handoff_episode
ADD COLUMN IF NOT EXISTS survey_id VARCHAR(64);

COMMENT ON COLUMN cs_handoff_episode.survey_id IS '不满意明确同意人工的调查引用，其他来源为空';

-- 一调查最多一人工周期，NULL 允许其他来源多次申请。
CREATE UNIQUE INDEX IF NOT EXISTS uq_cs_episode_survey ON cs_handoff_episode (tenant_id, top_org_id, survey_id);

ALTER TABLE cs_handoff_episode
DROP CONSTRAINT IF EXISTS ck_cs_handoff_episode_source;

ALTER TABLE cs_handoff_episode
ADD CONSTRAINT ck_cs_handoff_episode_source CHECK (
    trigger_source IN (
        'AI_RECOMMENDATION_CONFIRMED',
        'VISITOR_DIRECT_REQUEST',
        'SURVEY_DISSATISFIED_OPT_IN',
        'SYSTEM_POLICY'
    )
);

ALTER TABLE cs_handoff_episode
DROP CONSTRAINT IF EXISTS ck_cs_handoff_episode_source_ref;

ALTER TABLE cs_handoff_episode
ADD CONSTRAINT ck_cs_handoff_episode_source_ref CHECK (
    (
        trigger_source = 'AI_RECOMMENDATION_CONFIRMED'
        AND proposal_id IS NOT NULL
        AND turn_id IS NOT NULL
        AND survey_id IS NULL
    )
    OR (
        trigger_source = 'VISITOR_DIRECT_REQUEST'
        AND proposal_id IS NULL
        AND turn_id IS NULL
        AND survey_id IS NULL
        AND reason_code = 'USER_REQUESTED'
    )
    OR (
        trigger_source = 'SURVEY_DISSATISFIED_OPT_IN'
        AND proposal_id IS NULL
        AND turn_id IS NOT NULL
        AND survey_id IS NOT NULL
        AND reason_code = 'USER_DISSATISFIED'
    )
    OR (
        trigger_source = 'SYSTEM_POLICY'
        AND proposal_id IS NULL
        AND turn_id IS NOT NULL
        AND survey_id IS NULL
        AND reason_code IN ('SYSTEM_FAILURE', 'SAFETY_OR_URGENT')
    )
);

-- 合并来源文件结束：data/sql/kingbase/ai_20261109_customer_service_survey_runtime.sql
-- 合并来源文件：data/sql/kingbase/ai_20261109_customer_service_followup_response.sql
-- 目的：工单最终邮件回复、可靠投递与显式结案；方言 Kingbase PostgreSQL兼容模式，一次执行，不保留旧协议兼容。
-- 前提：customer_service_followup 已部署；执行前保存结构快照，不删除或回填原业务数据。
-- owner：客服上下文唯一写入；客服 API 写正文，Worker 推进受理状态；邮箱只从原 workflow 查询。
-- 生命周期：不可变最终回复及关闭记录按客服审计保留，无逻辑删除或级联删除；关闭不代表送达/已读。
-- 回滚：先关闭 followup-response API/Worker 与通知，再导出正文和未投递任务，按 owner 审核移除新对象。
-- 工单引用连同完整组织/会话/周期成为外键目标，禁止回复绑定其他工单。
-- 约束按当前 schema 和所属表核验，缺失才补建。
DO $cs_migration$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.table_constraints WHERE constraint_schema=current_schema() AND table_name='cs_waiting_workflow' AND constraint_name='uq_cs_waiting_ticket_scope') THEN
    ALTER TABLE cs_waiting_workflow ADD CONSTRAINT uq_cs_waiting_ticket_scope UNIQUE(tenant_id,top_org_id,conversation_id,episode_id,ticket_id);
  END IF;
END;
$cs_migration$;

CREATE TABLE IF NOT EXISTS cs_followup_response (
    -- 服务端认证租户；必填，隔离主键。
    tenant_id VARCHAR(64) NOT NULL,
    -- 原工单可信顶级组织；必填，隔离主键。
    top_org_id VARCHAR(64) NOT NULL,
    -- Conversation owner 会话引用；必填，复合外键归属。
    conversation_id VARCHAR(128) NOT NULL,
    -- 原接待周期；必填，一周期至多一份最终回复。
    episode_id VARCHAR(64) NOT NULL,
    -- 已确认邮箱后生成的工单号；必填，不接受客户端覆盖。
    ticket_id VARCHAR(64) NOT NULL,
    -- 原工单助手路由，应用层持锁核验；必填。
    assistant_config_id VARCHAR(64) NOT NULL,
    -- 原工单大会，组织级助手可空。
    bvent_id VARCHAR(64),
    -- 首次提交最终回复的稳定请求键；同键同内容重放，必填。
    request_id VARCHAR(100) NOT NULL,
    -- 经实时组授权的首次回复客服账号；必填审计。
    actor_id VARCHAR(128) NOT NULL,
    -- 最终邮件主题，最多200字符、应用层拒绝换行；必填。
    subject VARCHAR(200) NOT NULL,
    -- 最终邮件纯文本正文，应用层最多10000字符；必填，禁止普通日志输出。
    content TEXT NOT NULL,
    -- PENDING待发送、CLAIMED租约中、SENT网关受理、DEAD耗尽、CLOSED客服结案；必填。
    state VARCHAR(16) NOT NULL,
    -- 正数CAS版本，提交、领取、结算、重试及关闭递增；必填。
    version BIGINT NOT NULL,
    -- 当前投递周期领取次数，五次失败转DEAD，人工重试归零；必填非负。
    attempts INTEGER NOT NULL,
    -- UTC下次投递时刻，退避不超过300秒；必填。
    next_attempt_at TIMESTAMP(3) NOT NULL,
    -- 当前Worker随机租约，仅CLAIMED必填。
    lease_id VARCHAR(64),
    -- UTC租约到期，默认30秒，崩溃可接管。
    lease_until TIMESTAMP(3),
    -- 首次显式结案请求键，仅CLOSED必填，重试沿用。
    close_request_id VARCHAR(100),
    -- 首次回复HTTP追踪标识，Worker与网关沿用；必填，不含凭据。
    trace_id VARCHAR(128) NOT NULL,
    -- 首次结案的授权客服账号，仅CLOSED必填，不冒用首次回复者。
    close_actor_id VARCHAR(128),
    -- 首次提交UTC时间，不因重试改写；必填。
    created_at TIMESTAMP(3) NOT NULL,
    -- 组织隔离的一周期一份最终回复，不自动删除。
    PRIMARY KEY (tenant_id, top_org_id, episode_id),
    -- 同会话请求键不得用于不同工单。
    CONSTRAINT uq_cs_response_request UNIQUE (
        tenant_id,
        top_org_id,
        conversation_id,
        request_id
    ),
    -- 完整工单外键，不级联删除，防止跨组织/会话/周期或错ticket引用。
    CONSTRAINT fk_cs_response_ticket FOREIGN KEY (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id,
        ticket_id
    ) REFERENCES cs_waiting_workflow (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id,
        ticket_id
    ),
    CONSTRAINT ck_cs_response_state CHECK (
        state IN ('PENDING', 'CLAIMED', 'SENT', 'DEAD', 'CLOSED')
    ),
    CONSTRAINT ck_cs_response_version CHECK (
        version > 0
        AND attempts >= 0
    ),
    CONSTRAINT ck_cs_response_lease CHECK (
        state <> 'CLAIMED'
        OR (
            lease_id IS NOT NULL
            AND lease_until IS NOT NULL
        )
    ),
    CONSTRAINT ck_cs_response_close CHECK (
        (
            state = 'CLOSED'
            AND close_request_id IS NOT NULL
            AND close_actor_id IS NOT NULL
        )
        OR (
            state <> 'CLOSED'
            AND close_request_id IS NULL
            AND close_actor_id IS NULL
        )
    )
);

-- Worker到期/崩溃恢复扫描索引，不影响业务唯一性。
CREATE INDEX IF NOT EXISTS ix_cs_response_due ON cs_followup_response (state, next_attempt_at, lease_until);

-- Kingbase直接执行CHECK兜底值域与租约/结案约束，与MySQL业务语义一致。
-- 合并来源文件结束：data/sql/kingbase/ai_20261109_customer_service_followup_response.sql
-- 合并来源文件：data/sql/kingbase/ai_20261109_customer_service_availability.sql
-- 目的：客服在线租约、组织级容量串行锁及不可变改派审计；用户中心唯一管理账号/组。
-- 前提：基线及reception增量已部署；KingbaseES PostgreSQL模式，单次事务执行，先备份结构。
-- Owner：Customer Service；授权客服编排写、接待及指标只读；无本地账号或组主表。
-- 生命周期：运行行45秒失联按OFFLINE，不自动结束接待；改派审计随会话留存，不逻辑删除、不自动清记录。
-- 隔离：主键含tenant/topOrg，改派复合外键拒绝跨组织周期。非空约束要求完整首次事实。
-- 回滚：先停availability及改派入口，导出审计再人工决定保留；不提供开发期旧协议兼容。
BEGIN;

CREATE TABLE IF NOT EXISTS cs_agent_availability (
    tenant_id VARCHAR(64) NOT NULL,
    top_org_id VARCHAR(64) NOT NULL,
    agent_id VARCHAR(128) NOT NULL,
    status VARCHAR(16) NOT NULL,
    expires_at TIMESTAMP(6) NOT NULL,
    updated_at TIMESTAMP(6) NOT NULL,
    PRIMARY KEY (tenant_id, top_org_id, agent_id),
    CONSTRAINT ck_cs_availability_status CHECK (status IN ('ONLINE', 'AWAY', 'OFFLINE'))
);

COMMENT ON TABLE cs_agent_availability IS '客服运行状态及容量串行锁；主键保证组织内一客服一行，不是账号主表';

COMMENT ON COLUMN cs_agent_availability.tenant_id IS '认证租户';

COMMENT ON COLUMN cs_agent_availability.top_org_id IS '实时核验启用的可信组织';

COMMENT ON COLUMN cs_agent_availability.agent_id IS '外部账号稳定引用';

COMMENT ON COLUMN cs_agent_availability.status IS 'ONLINE接新单/AWAY暂离/OFFLINE离线，均不结束已有接待';

COMMENT ON COLUMN cs_agent_availability.expires_at IS '服务器45秒UTC租约，到期按离线';

COMMENT ON COLUMN cs_agent_availability.updated_at IS '最近明确选择或心跳UTC时间';

CREATE INDEX IF NOT EXISTS ix_cs_claim_agent_capacity ON cs_handoff_claim (tenant_id, top_org_id, agent_id);

COMMENT ON INDEX ix_cs_claim_agent_capacity IS '组织内所有助手/组有效认领容量查询';

CREATE TABLE IF NOT EXISTS cs_reception_transfer (
    tenant_id VARCHAR(64) NOT NULL,
    top_org_id VARCHAR(64) NOT NULL,
    conversation_id VARCHAR(128) NOT NULL,
    episode_id VARCHAR(64) NOT NULL,
    request_id VARCHAR(128) NOT NULL,
    actor_id VARCHAR(128) NOT NULL,
    from_agent_id VARCHAR(128) NOT NULL,
    to_agent_id VARCHAR(128) NOT NULL,
    group_id VARCHAR(64) NOT NULL,
    fingerprint VARCHAR(64) NOT NULL,
    result_version BIGINT NOT NULL,
    trace_id VARCHAR(128) NOT NULL,
    created_at TIMESTAMP(6) NOT NULL,
    PRIMARY KEY (
        tenant_id,
        top_org_id,
        conversation_id,
        request_id
    ),
    CONSTRAINT fk_cs_transfer_episode FOREIGN KEY (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id
    ) REFERENCES cs_handoff_episode (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id
    ),
    CONSTRAINT ck_cs_transfer_valid CHECK (
        result_version > 1
        AND actor_id = from_agent_id
        AND from_agent_id <> to_agent_id
    )
);

COMMENT ON TABLE cs_reception_transfer IS '不可变改派事实；主键防重复，复合外键防跨组织，必填完整首次事实';

COMMENT ON COLUMN cs_reception_transfer.tenant_id IS '认证租户';

COMMENT ON COLUMN cs_reception_transfer.top_org_id IS '可信组织';

COMMENT ON COLUMN cs_reception_transfer.conversation_id IS '原会话，不创建新访客意图';

COMMENT ON COLUMN cs_reception_transfer.episode_id IS '原人工周期，保持HUMAN';

COMMENT ON COLUMN cs_reception_transfer.request_id IS '同会话稳定幂等键';

COMMENT ON COLUMN cs_reception_transfer.actor_id IS '正式认证操作者，等于原认领人';

COMMENT ON COLUMN cs_reception_transfer.from_agent_id IS '首次原客服引用';

COMMENT ON COLUMN cs_reception_transfer.to_agent_id IS '实时授权且在线有容量的同组目标';

COMMENT ON COLUMN cs_reception_transfer.group_id IS '服务器assistant路由组';

COMMENT ON COLUMN cs_reception_transfer.fingerprint IS '主体/目标/周期/版本SHA256，异参不可覆盖';

COMMENT ON COLUMN cs_reception_transfer.result_version IS '改派后版本，大于1';

COMMENT ON COLUMN cs_reception_transfer.trace_id IS '首次trace，重试不覆盖';

COMMENT ON COLUMN cs_reception_transfer.created_at IS '原子改派UTC时间';

-- 函数和触发器禁止UPDATE首次事实，重试只读原回执。
CREATE OR REPLACE FUNCTION reject_cs_transfer_update () RETURNS trigger AS $$ BEGIN RAISE EXCEPTION '客服改派审计不允许修改'; END; $$ LANGUAGE plpgsql;

COMMENT ON FUNCTION reject_cs_transfer_update () IS '拒绝修改首次改派事实';

-- 维护窗口刷新同名审计触发器，不修改既有审计事实。
DROP TRIGGER IF EXISTS trg_cs_transfer_immutable ON cs_reception_transfer;

CREATE TRIGGER trg_cs_transfer_immutable BEFORE
UPDATE ON cs_reception_transfer FOR EACH ROW
EXECUTE PROCEDURE reject_cs_transfer_update ();

COMMIT;

-- 合并来源文件结束：data/sql/kingbase/ai_20261109_customer_service_availability.sql
-- 合并来源文件：data/sql/kingbase/ai_20261109_customer_service_availability_lease.sql
-- 目的：为在线选择增加服务端代次，防止其他标签页的旧心跳覆盖明确下线/暂离。
-- 前提：已执行availability增量；KingbaseES PostgreSQL兼容；单次执行，先保存结构快照。
-- Owner：Customer Service唯一写；已授权客服明确选择递增，心跳只读代次并比较续约。
-- 隔离/生命周期：沿用tenant/topOrg/agent复合主键，运行行不删除；现有行从0开始，须明确重新上线。
-- 回滚：先关闭availability.enabled、停止心跳入口并回退应用；保留代次列，不重跑既有迁移。
ALTER TABLE cs_agent_availability
ADD COLUMN IF NOT EXISTS version BIGINT NOT NULL DEFAULT 0;

COMMENT ON COLUMN cs_agent_availability.version IS '明确状态选择递增代次；0表示尚未新协议上线，心跳不得改变该值';

-- 非空和非负约束拒绝无效代次，不影响组织复合主键和既有接待。
-- 约束按当前 schema 和所属表核验，缺失才补建。
DO $cs_migration$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.table_constraints WHERE constraint_schema=current_schema() AND table_name='cs_agent_availability' AND constraint_name='ck_cs_availability_version') THEN
    ALTER TABLE cs_agent_availability ADD CONSTRAINT ck_cs_availability_version CHECK(version>=0);
  END IF;
END;
$cs_migration$;

-- 合并来源文件结束：data/sql/kingbase/ai_20261109_customer_service_availability_lease.sql
-- 合并来源文件：data/sql/kingbase/ai_20261109_customer_service_system.sql
-- 目的：可信系统故障/安全护栏可靠转人工；保留来源轮次/epoch，旧代次失败不得覆盖新回复权。
-- 前提：已部署基线、reception、survey-runtime；单次执行并先备份。方言：KingbaseES PostgreSQL模式，事务DDL。
-- Owner：Customer Service；可信服务端写任务、Worker写申请/Outbox/HUMAN同事务；无HTTP/Tool创建入口。
-- 生命周期：PENDING待消费/COMPLETED已原子创建/SUPERSEDED旧代次失效/DEAD五次失败；死信人工重试，随会话留存不逻辑删除。
-- 隔离：tenant/topOrg主键与复合generation外键；主体/路由/trace来自服务端不可变快照，全部必填。
-- 回滚：先关闭system-handoff.enabled，导出任务与系统来源episode；存在系统来源事实时不得恢复旧约束。不删记录。
BEGIN;

ALTER TABLE cs_handoff_episode
DROP CONSTRAINT IF EXISTS ck_cs_handoff_episode_source;

ALTER TABLE cs_handoff_episode
ADD CONSTRAINT ck_cs_handoff_episode_source CHECK (
    trigger_source IN (
        'AI_RECOMMENDATION_CONFIRMED',
        'VISITOR_DIRECT_REQUEST',
        'SURVEY_DISSATISFIED_OPT_IN',
        'SYSTEM_POLICY'
    )
);

ALTER TABLE cs_handoff_episode
DROP CONSTRAINT IF EXISTS ck_cs_handoff_episode_source_ref;

ALTER TABLE cs_handoff_episode
ADD CONSTRAINT ck_cs_handoff_episode_source_ref CHECK (
    (
        trigger_source = 'AI_RECOMMENDATION_CONFIRMED'
        AND proposal_id IS NOT NULL
        AND turn_id IS NOT NULL
        AND survey_id IS NULL
    )
    OR (
        trigger_source = 'VISITOR_DIRECT_REQUEST'
        AND proposal_id IS NULL
        AND turn_id IS NULL
        AND survey_id IS NULL
        AND reason_code = 'USER_REQUESTED'
    )
    OR (
        trigger_source = 'SURVEY_DISSATISFIED_OPT_IN'
        AND proposal_id IS NULL
        AND turn_id IS NOT NULL
        AND survey_id IS NOT NULL
        AND reason_code = 'USER_DISSATISFIED'
    )
    OR (
        trigger_source = 'SYSTEM_POLICY'
        AND proposal_id IS NULL
        AND turn_id IS NOT NULL
        AND survey_id IS NULL
        AND reason_code IN ('SYSTEM_FAILURE', 'SAFETY_OR_URGENT')
    )
);

ALTER TABLE cs_handoff_episode
DROP CONSTRAINT IF EXISTS ck_cs_handoff_episode_reason;

ALTER TABLE cs_handoff_episode
ADD CONSTRAINT ck_cs_handoff_episode_reason CHECK (
    reason_code IN (
        'UNRESOLVED_AFTER_RETRY',
        'USER_DISSATISFIED',
        'REQUIRES_HUMAN_JUDGMENT',
        'USER_REQUESTED',
        'SYSTEM_FAILURE',
        'SAFETY_OR_URGENT'
    )
);

CREATE TABLE IF NOT EXISTS cs_system_handoff (
    tenant_id VARCHAR(64) NOT NULL,
    top_org_id VARCHAR(64) NOT NULL,
    task_id VARCHAR(64) NOT NULL,
    conversation_id VARCHAR(128) NOT NULL,
    assistant_config_id VARCHAR(64) NOT NULL,
    bvent_id VARCHAR(64) NULL,
    principal_id VARCHAR(128) NOT NULL,
    turn_id VARCHAR(128) NOT NULL,
    generation_epoch BIGINT NOT NULL,
    reason_code VARCHAR(32) NOT NULL,
    trace_id VARCHAR(128) NOT NULL,
    state VARCHAR(16) NOT NULL,
    attempts INT NOT NULL,
    next_attempt_at TIMESTAMP(6) NOT NULL,
    episode_id VARCHAR(64) NULL,
    created_at TIMESTAMP(6) NOT NULL,
    retry_actor_id VARCHAR(128) NULL,
    retry_at TIMESTAMP(6) NULL,
    retry_count INT NOT NULL DEFAULT 0,
    PRIMARY KEY (tenant_id, top_org_id, task_id),
    CONSTRAINT fk_cs_system_generation FOREIGN KEY (tenant_id, top_org_id, conversation_id) REFERENCES ai_conversation_generation (tenant_id, top_org_id, conversation_id),
    CONSTRAINT fk_cs_system_episode FOREIGN KEY (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id
    ) REFERENCES cs_handoff_episode (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id
    ),
    CONSTRAINT ck_cs_system_state CHECK (
        state IN ('PENDING', 'COMPLETED', 'SUPERSEDED', 'DEAD')
    ),
    CONSTRAINT ck_cs_system_reason CHECK (
        reason_code IN ('SYSTEM_FAILURE', 'SAFETY_OR_URGENT')
    ),
    CONSTRAINT ck_cs_system_attempt CHECK (
        attempts BETWEEN 0 AND 5
        AND generation_epoch > 0
    ),
    CONSTRAINT ck_cs_system_retry CHECK (
        retry_count >= 0
        AND (
            (
                retry_count = 0
                AND retry_actor_id IS NULL
                AND retry_at IS NULL
            )
            OR (
                retry_count > 0
                AND retry_actor_id IS NOT NULL
                AND retry_at IS NOT NULL
            )
        )
    ),
    CONSTRAINT ck_cs_system_result CHECK (
        (
            state = 'COMPLETED'
            AND episode_id IS NOT NULL
        )
        OR (
            state <> 'COMPLETED'
            AND episode_id IS NULL
        )
    )
);

-- 到期索引用于多节点恢复，消费持generation和任务锁，不执行网络调用。
CREATE INDEX IF NOT EXISTS ix_cs_system_due ON cs_system_handoff (state, next_attempt_at);

COMMENT ON COLUMN cs_system_handoff.tenant_id IS '认证租户';

COMMENT ON COLUMN cs_system_handoff.top_org_id IS '可信顶级组织';

COMMENT ON COLUMN cs_system_handoff.task_id IS '完整范围/轮次/代次/原因SHA256稳定键';

COMMENT ON COLUMN cs_system_handoff.conversation_id IS '原AI会话引用';

COMMENT ON COLUMN cs_system_handoff.assistant_config_id IS '持久智能体配置';

COMMENT ON COLUMN cs_system_handoff.bvent_id IS '大会引用，组织级为空';

COMMENT ON COLUMN cs_system_handoff.principal_id IS '原认证访客，不使用系统账号替换';

COMMENT ON COLUMN cs_system_handoff.turn_id IS '真实失败轮次引用';

COMMENT ON COLUMN cs_system_handoff.generation_epoch IS '失败开始时AI代次，大于0';

COMMENT ON COLUMN cs_system_handoff.reason_code IS '服务器SYSTEM_FAILURE或SAFETY_OR_URGENT';

COMMENT ON COLUMN cs_system_handoff.trace_id IS '首次故障trace，重试不覆盖';

COMMENT ON COLUMN cs_system_handoff.state IS 'PENDING/COMPLETED/SUPERSEDED/DEAD集中状态';

COMMENT ON COLUMN cs_system_handoff.attempts IS '消费失败次数0—5，人工重试重置预算';

COMMENT ON COLUMN cs_system_handoff.next_attempt_at IS 'UTC恢复期限，退避5秒乘次数，最多300秒';

COMMENT ON COLUMN cs_system_handoff.episode_id IS '仅COMPLETED持有真实同组织会话的人工周期';

COMMENT ON COLUMN cs_system_handoff.created_at IS '首次登记UTC时间';

COMMENT ON TABLE cs_system_handoff IS '可信系统可靠任务；主键防重、复合外键隔离，随会话留存';

COMMENT ON INDEX ix_cs_system_due IS '多节点到期恢复扫描';

COMMENT ON COLUMN cs_system_handoff.retry_actor_id IS '最近一次正式客服恢复预算的主体';

COMMENT ON COLUMN cs_system_handoff.retry_at IS '最近人工重试UTC时间';

COMMENT ON COLUMN cs_system_handoff.retry_count IS '累计人工恢复次数，不随自动预算重置';

COMMIT;

-- 合并来源文件结束：data/sql/kingbase/ai_20261109_customer_service_system.sql
-- 合并来源文件：data/sql/kingbase/ai_20261110_customer_service_routing.sql
-- 目的：由客服上下文唯一维护业务组、成员关联、助手分类路由及申请时冻结的接待归属。
-- 前提：已执行 20261109 客服接待、跟进及系统增量；开发阶段无外部组回填，不迁移旧申请。
-- 执行：一次性维护窗口增量，先备份；MySQL DDL 自动提交，Kingbase 应由部署事务提交。
-- 回滚：先停用客服入口，导出新增配置/路由审计事实后由运维按外键逆序处理；禁止清理旧业务记录。
-- owner/生命周期：仅客服配置应用写组及关联，申请编排写冻结路由；组和路由不逻辑/物理删除。
-- 账号由用户中心拥有，仅保存引用；成员关联可撤销，历史快照保留，租户/组织复合主外键防止串组。
-- 方言：KingbaseES PostgreSQL兼容模式，CHECK强制执行。
-- 组织配置聚合根；主键保证一个版本源，非空归属及写入者用于授权审计。
CREATE TABLE IF NOT EXISTS cs_reception_configuration (
    tenant_id VARCHAR(64) NOT NULL, -- 认证租户
    top_org_id VARCHAR(64) NOT NULL, -- 核验后的目标组织
    version BIGINT NOT NULL, -- 从1递增的配置CAS版本
    updated_by VARCHAR(128) NOT NULL, -- 最近管理账号引用，不保存凭据
    updated_at TIMESTAMP(6) NOT NULL, -- 最近提交UTC时间
    PRIMARY KEY (tenant_id, top_org_id),
    CONSTRAINT ck_cs_routing_version CHECK (version > 0) -- 防止无版本配置被发布
);

-- 四个稳定业务组；组ID及类别组织内唯一，永不改写历史引用。
CREATE TABLE IF NOT EXISTS cs_reception_group (
    tenant_id VARCHAR(64) NOT NULL, -- 认证租户
    top_org_id VARCHAR(64) NOT NULL, -- 所属组织
    group_id VARCHAR(64) NOT NULL, -- 本项目稳定业务组ID
    category VARCHAR(16) NOT NULL, -- EXHIBITOR/VISITOR/MEDIA/OTHER默认组类别
    display_name VARCHAR(64) NOT NULL, -- 管理员维护的展示名称
    PRIMARY KEY (tenant_id, top_org_id, group_id),
    CONSTRAINT uq_cs_reception_category UNIQUE (tenant_id, top_org_id, category), -- 每类别默认一组
    CONSTRAINT fk_cs_group_config FOREIGN KEY (tenant_id, top_org_id) REFERENCES cs_reception_configuration (tenant_id, top_org_id),
    CONSTRAINT ck_cs_group_category CHECK (
        category IN ('EXHIBITOR', 'VISITOR', 'MEDIA', 'OTHER')
    )
);

-- 成员关联可撤销；不是用户账号主表，账号启用必须实时向用户中心核验。
CREATE TABLE IF NOT EXISTS cs_reception_member (
    tenant_id VARCHAR(64) NOT NULL, -- 认证租户
    top_org_id VARCHAR(64) NOT NULL, -- 所属组织
    group_id VARCHAR(64) NOT NULL, -- 本地业务组引用
    account_id VARCHAR(128) NOT NULL, -- 用户中心稳定账号ID
    display_name VARCHAR(128) NOT NULL, -- 最近核验名称，仅用于展示
    PRIMARY KEY (tenant_id, top_org_id, group_id, account_id), -- 同账号可属于多组，同组禁止重复
    CONSTRAINT fk_cs_member_group FOREIGN KEY (tenant_id, top_org_id, group_id) REFERENCES cs_reception_group (tenant_id, top_org_id, group_id)
);

-- 账号反查索引服务实时授权，不存独立账号状态。
CREATE INDEX IF NOT EXISTS ix_cs_member_account ON cs_reception_member (tenant_id, top_org_id, account_id);

-- 助手每类咨询绑定一个组；助手归属通过助手应用端口校验，禁止跨上下文直接写助手表。
CREATE TABLE IF NOT EXISTS cs_reception_route (
    tenant_id VARCHAR(64) NOT NULL, -- 认证租户
    top_org_id VARCHAR(64) NOT NULL, -- 所属组织
    assistant_config_id VARCHAR(64) NOT NULL, -- 已核验助手引用
    category VARCHAR(16) NOT NULL, -- 咨询语义类别
    group_id VARCHAR(64) NOT NULL, -- 目标本地业务组
    PRIMARY KEY (
        tenant_id,
        top_org_id,
        assistant_config_id,
        category
    ), -- 防止同一类别配置冲突
    CONSTRAINT fk_cs_route_group FOREIGN KEY (tenant_id, top_org_id, group_id) REFERENCES cs_reception_group (tenant_id, top_org_id, group_id),
    CONSTRAINT ck_cs_route_category CHECK (
        category IN ('EXHIBITOR', 'VISITOR', 'MEDIA', 'OTHER')
    )
);

-- 申请时唯一冻结，配置变更不改写；与episode、命令回执和HUMAN owner同事务写入。
CREATE TABLE IF NOT EXISTS cs_handoff_route (
    tenant_id VARCHAR(64) NOT NULL, -- episode认证租户
    top_org_id VARCHAR(64) NOT NULL, -- episode可信组织
    conversation_id VARCHAR(128) NOT NULL, -- 原会话引用
    episode_id VARCHAR(64) NOT NULL, -- 唯一人工申请周期
    assistant_config_id VARCHAR(64) NOT NULL, -- 原助手引用
    category VARCHAR(16) NOT NULL, -- 本次咨询类别，不是用户身份
    group_id VARCHAR(64) NOT NULL, -- 首次分配组，后续配置不影响
    config_version BIGINT NOT NULL, -- 首次使用的配置版本
    confidence DOUBLE PRECISION NOT NULL, -- 0..1，降级为0
    classification_source VARCHAR(64) NOT NULL, -- MODEL或明确降级原因
    policy_version VARCHAR(64) NOT NULL, -- 语义策略版本
    trace_id VARCHAR(128) NOT NULL, -- 原申请追踪引用，不保存对话正文
    created_at TIMESTAMP(6) NOT NULL, -- 首次分配UTC时间
    PRIMARY KEY (tenant_id, top_org_id, episode_id),
    CONSTRAINT fk_cs_frozen_episode FOREIGN KEY (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id
    ) REFERENCES cs_handoff_episode (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id
    ),
    CONSTRAINT fk_cs_frozen_group FOREIGN KEY (tenant_id, top_org_id, group_id) REFERENCES cs_reception_group (tenant_id, top_org_id, group_id),
    CONSTRAINT ck_cs_frozen_category CHECK (
        category IN ('EXHIBITOR', 'VISITOR', 'MEDIA', 'OTHER')
    ),
    CONSTRAINT ck_cs_frozen_confidence CHECK (
        confidence >= 0
        AND confidence <= 1
        AND config_version > 0
    )
);

-- 分组队列索引避免跨组织全表扫描。
CREATE INDEX IF NOT EXISTS ix_cs_frozen_queue ON cs_handoff_route (
    tenant_id,
    top_org_id,
    group_id,
    assistant_config_id
);

-- 合并来源文件结束：data/sql/kingbase/ai_20261110_customer_service_routing.sql
-- 2026-11-11 完整需求验收增量：组织营业时间与离线留言认领。
-- 方言：Kingbase/PostgreSQL兼容DDL；前提：已执行上文接待、等待与路由结构，先备份并停止相关写入。
-- owner：Customer Service唯一主写；配置应用写日历，访客提交原文，原路由组客服认领，用户中心仍拥有账号及组织。
-- 生命周期：规则只影响新周期；留言随原工单保留审计，无逻辑删除或级联删除。回滚须先导出并人工审核。
-- 兼容：仅新增表，IF NOT EXISTS支持开发库重跑，不更改既有工单、状态或通知。
CREATE TABLE IF NOT EXISTS cs_reception_calendar (
    tenant_id VARCHAR(64) NOT NULL, -- 认证租户，组织隔离复合主键
    top_org_id VARCHAR(64) NOT NULL, -- 已核验组织
    version BIGINT NOT NULL CHECK (version > 0), -- 从1递增的CAS版本，数据库拒绝非正值
    calendar_version VARCHAR(64) NOT NULL, -- 业务日历版本，历史期限追溯
    zone VARCHAR(64) NOT NULL, -- 合法IANA时区
    opens VARCHAR(8) NOT NULL, -- 当地营业起点，左闭
    closes VARCHAR(8) NOT NULL, -- 当地营业终点，右开
    overrides_json TEXT NOT NULL, -- ISO日期到营业布尔值JSON，缺省周一到周五
    updated_by VARCHAR(128) NOT NULL, -- 最近保存的认证账号
    updated_at TIMESTAMP(6) NOT NULL, -- 最近提交UTC时间
    PRIMARY KEY (tenant_id, top_org_id) -- 每租户组织唯一营业策略
);

CREATE TABLE IF NOT EXISTS cs_followup_form (
    tenant_id VARCHAR(64) NOT NULL, -- 认证租户
    top_org_id VARCHAR(64) NOT NULL, -- 原接待组织
    conversation_id VARCHAR(128) NOT NULL, -- 原AI和人工会话
    episode_id VARCHAR(64) NOT NULL, -- 原周期，不漂移到后续周期
    ticket_id VARCHAR(64) NOT NULL, -- 原等待owner的稳定工单号
    request_id VARCHAR(100) NOT NULL, -- 首次提交幂等键
    contact_name VARCHAR(100) NOT NULL, -- 访客自填称呼，可空串，不用于认证
    content TEXT NOT NULL, -- 访客主动填写的1到4000字纯文本留言
    state VARCHAR(16) NOT NULL, -- OPEN待认领或CLAIMED已认领，应用显式转换
    version BIGINT NOT NULL CHECK (version > 0), -- 从1递增的认领CAS版本
    assigned_to VARCHAR(128) NULL, -- 唯一认领客服，未认领为空
    claim_request_id VARCHAR(100) NULL, -- 首次认领幂等键
    submitted_at TIMESTAMP(6) NOT NULL, -- 首次提交UTC时间
    claimed_at TIMESTAMP(6) NULL, -- 首次认领UTC时间
    PRIMARY KEY (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id
    ), -- 一原周期一份表单
    CONSTRAINT ck_cs_form_assignment CHECK (
        (
            state = 'OPEN'
            AND assigned_to IS NULL
            AND claim_request_id IS NULL
            AND claimed_at IS NULL
        )
        OR (
            state = 'CLAIMED'
            AND assigned_to IS NOT NULL
            AND claim_request_id IS NOT NULL
            AND claimed_at IS NOT NULL
        )
    ), -- 认领必须包含唯一主体和时间
    CONSTRAINT fk_cs_form_workflow FOREIGN KEY (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id
    ) REFERENCES cs_waiting_workflow (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id
    ) -- 数据库防跨租户组织关联
);