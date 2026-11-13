-- 2026-11-11 简化：取消用户沉默触发调查，不再创建空闲期限表或信号来源列；已有历史记录保留，应用忽略旧空闲信号。
-- ============================================================================
-- 2026-11-11 客服及关联 AI 流程全量迭代 SQL（本方言唯一部署入口）。
-- 目的：按依赖顺序汇总 generation、转人工、AI 结束判断、调查、接待、等待、跟进、在线及路由。
-- 数据库方言：MySQL / InnoDB；保留原脚本的 5.7 触发器和 8.x CHECK 分支。
-- 版本前提：基础段原要求 MySQL 8.0.16+；5.7 的既有开发记录不代表整份约束已经完整验收。
-- 执行器必须支持 DELIMITER 和 PREPARE；DDL 自动提交，中途失败须盘点已提交对象后处理。
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
-- 合并来源文件：data/sql/mysql/ai_20261109_customer_service_and_resolution.sql
-- ============================================================================
-- 2026-11-09 handoff 与 AI Conversation 结束评价开发库结构
-- 目的：合并当前已明确 owner 的 generation、handoff episode、结束信号、survey 和内容评估 DDL。
-- 数据 owner：handoff 表由客服编排应用写入；generation、用户明确结束信号、survey 与内容评估由 AI Conversation owner 写入。
-- 执行前提：先确认开发库使用 MySQL 8.0.16+/InnoDB、备份并核对本批核心表尚未部分执行；跨 owner 原子事务须共用数据源。
-- 边界：不创建内部系统拥有的账号/组/成员，也不创建 owner 未确认的客服在线、容量、claim、客服会话或独立消息表。
-- 方言：MySQL 8.0.16+/InnoDB；Kingbase 使用项目部署版本。两个方言必须分别执行，不可交叉执行。
-- 执行方式：一次性按本文件顺序执行；不支持并行、跳步或盲目重跑。MySQL DDL 失败可能留下已提交对象，应先盘点失败点。
-- 回滚：本文件不附自动回滚；停用相关写入并归档业务事实后，按对象依赖逆序删除本文件创建的表和 generation 唯一约束。删除会丢失转人工、审计和评价事实。
-- 兼容：仅面向开发阶段当前结构，不保留旧迁移顺序或旧 owner 的双写兼容。
-- ============================================================================
-- 来源片段：data/sql/mysql/ai_20261106_conversation_generation/formal/add_ai_conversation_generation.sql
-- ============================================================================
-- AI Conversation 回复 owner 与 generation epoch 控制事实。
-- 执行前提：客服建议 v2 与 AI conversation 使用同一个 MySQL 8.0+/InnoDB 主数据源，
--          应用已完成共享事务配置；部署前备份数据库并确认 conversation_id 全局租户内唯一。
-- 数据 owner：AI conversation 上下文唯一写入租户/组织/assistant 绑定、reply_owner 和 generation_epoch；
--             客服应用只能在确认/结束事务内调用 owner 端口变更 AI/HUMAN 状态。
-- 方言：MySQL 8.0+ / InnoDB；依赖复合主键、CHECK（8.0.16+ 强制）和 DATETIME(6)。
-- 回滚：先关闭转人工确认与 AI generation 写入，完成运行中流的收敛并归档客服 episode/Turn；
--       再 DROP TABLE ai_conversation_generation。回滚会失去当前 owner 与 epoch，禁止在线直接删除。
-- 兼容：本迁移是新契约，不双写旧客服 session mode；表中不保存聊天正文或客服账号/组主数据。
-- 删除策略：与 AI conversation 生命周期一致；会话归档/清理作业须同事务或在归档后删除本控制行。
-- ============================================================================
-- 每个租户下的 Conversation 唯一控制行；HUMAN owner_ref 指向当前 handoff episode。
CREATE TABLE IF NOT EXISTS `ai_conversation_generation` (
    `tenant_id` VARCHAR(64) NOT NULL COMMENT '认证上下文解析得到的租户归属',
    `top_org_id` VARCHAR(64) NOT NULL COMMENT '可信 assistant 上下文解析得到的顶级组织归属',
    `conversation_id` VARCHAR(128) NOT NULL COMMENT '稳定 AI conversation 标识，不是客服 session 主数据',
    `assistant_config_id` VARCHAR(64) NOT NULL COMMENT '该 Conversation 首次可信绑定的 assistant 配置',
    `bvent_id` VARCHAR(64) NULL COMMENT '可信大会范围，非大会智能体为空且后续不可改写',
    `reply_owner` VARCHAR(16) NOT NULL COMMENT '当前唯一回复 owner：AI 或 HUMAN',
    `generation_epoch` BIGINT NOT NULL COMMENT '每次开始 AI、转人工或人工结束时单调递增的回复代次',
    `owner_ref` VARCHAR(64) NULL COMMENT 'HUMAN 时为当前 handoff episode ID；AI 时必须为空',
    `created_at` DATETIME(6) NOT NULL COMMENT '控制行首次建立的 UTC 时间',
    `updated_at` DATETIME(6) NOT NULL COMMENT '最近一次 owner 或 epoch 变化的 UTC 时间',
    PRIMARY KEY (`tenant_id`, `conversation_id`),
    KEY `idx_ai_conversation_generation_owner` (
        `tenant_id`,
        `top_org_id`,
        `reply_owner`,
        `updated_at`
    ),
    CONSTRAINT `ck_ai_conversation_generation_owner` CHECK (`reply_owner` IN ('AI', 'HUMAN')),
    CONSTRAINT `ck_ai_conversation_generation_epoch` CHECK (`generation_epoch` >= 0),
    CONSTRAINT `ck_ai_conversation_generation_ref` CHECK (
        (
            `reply_owner` = 'AI'
            AND `owner_ref` IS NULL
        )
        OR (
            `reply_owner` = 'HUMAN'
            AND `owner_ref` IS NOT NULL
        )
    )
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COMMENT = 'AI Conversation 回复 owner 与跨节点 generation fencing 控制事实';

-- 来源片段：data/sql/mysql/ai_20261029_customer_service/formal/create_customer_service_handoff_proposal.sql
-- ============================================================================
-- AI conversation 转人工建议、访客决策、handoff episode 与独立可靠 Outbox。
-- 执行前提：本文件先创建 Conversation generation；handoff proposal 不依赖 cs_session 或本地客服目录表。
-- 数据 owner：客服编排服务唯一写入 proposal、episode、决策回执和 Outbox；AI conversation owner
-- 仍独占聊天记录/generation，内部客服系统仍独占客服账号、组、成员和真实 claim。
-- 方言：MySQL 8.0+/InnoDB；CHECK 约束在 8.0.16+ 强制执行，低版本不得开放正式写入。
-- 回滚：关闭建议与确认入口并排空/归档 Outbox 后，依次删除 command、episode、Outbox、proposal；
-- 回滚会丢失访客选择和转接审计事实，必须先归档。此迁移不提供旧版双写兼容。
-- ============================================================================
-- 一条 AI 轮次最多一条待确认建议；同轮重复 Tool 调用由应用层指纹验证后返回原 proposal。
CREATE TABLE IF NOT EXISTS `cs_handoff_proposal` (
    `tenant_id` VARCHAR(64) NOT NULL COMMENT '服务端认证得到的租户归属',
    `top_org_id` VARCHAR(64) NOT NULL COMMENT '服务端解析得到的顶级组织归属',
    `proposal_id` VARCHAR(64) NOT NULL COMMENT '服务端生成的建议卡稳定标识',
    `conversation_id` VARCHAR(128) NOT NULL COMMENT '已有 AI conversation 标识，不复制 conversation 消息',
    `turn_id` VARCHAR(128) NOT NULL COMMENT '触发建议的 AI 轮次标识',
    `assistant_config_id` VARCHAR(64) NOT NULL COMMENT '可信请求上下文绑定的智能体配置',
    `bvent_id` VARCHAR(64) NULL COMMENT '可信上下文绑定的大会标识，非大会场景为空',
    `trace_id` VARCHAR(128) NOT NULL COMMENT '跨异步边界传播的服务端追踪标识',
    `reason_code` VARCHAR(48) NOT NULL COMMENT '服务端白名单校验后的建议原因',
    `confidence` DECIMAL(5, 4) NOT NULL COMMENT '通过阈值的模型置信度，范围 0.0000 到 1.0000',
    `user_facing_reason` VARCHAR(640) NOT NULL COMMENT '由服务端原因码生成的访客说明，不保存模型自由文本',
    `policy_version` VARCHAR(64) NOT NULL COMMENT '生成建议时使用的服务端策略版本',
    `status` VARCHAR(16) NOT NULL COMMENT 'PENDING、CONFIRMED、DISMISSED 或 EXPIRED',
    `version` BIGINT NOT NULL COMMENT '每次状态转移递增的乐观锁版本',
    `input_fingerprint` CHAR(64) NOT NULL COMMENT '同一 AI 轮次建议内容摘要，发现异参重试',
    `created_at` DATETIME(6) NOT NULL COMMENT '建议事实创建 UTC 时间',
    `expires_at` DATETIME(6) NOT NULL COMMENT '访客确认截止 UTC 时间',
    `updated_at` DATETIME(6) NULL COMMENT '最近一次建议状态转移 UTC 时间',
    PRIMARY KEY (`tenant_id`, `top_org_id`, `proposal_id`),
    UNIQUE KEY `uq_cs_handoff_proposal_turn` (
        `tenant_id`,
        `top_org_id`,
        `conversation_id`,
        `turn_id`
    ),
    KEY `idx_cs_handoff_proposal_conversation` (
        `tenant_id`,
        `top_org_id`,
        `conversation_id`,
        `status`,
        `created_at`
    ),
    CONSTRAINT `ck_cs_handoff_proposal_reason` CHECK (
        `reason_code` IN (
            'UNRESOLVED_AFTER_RETRY',
            'USER_DISSATISFIED',
            'REQUIRES_HUMAN_JUDGMENT'
        )
    ),
    CONSTRAINT `ck_cs_handoff_proposal_confidence` CHECK (
        `confidence` >= 0
        AND `confidence` <= 1
    ),
    CONSTRAINT `ck_cs_handoff_proposal_status` CHECK (
        `status` IN ('PENDING', 'CONFIRMED', 'DISMISSED', 'EXPIRED')
    ),
    CONSTRAINT `ck_cs_handoff_proposal_version` CHECK (
        `version` > 0
        AND `expires_at` > `created_at`
    )
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COMMENT = '客服编排 owner 持有的 AI 转人工建议，不依赖已建立的客服 session';

-- AI 建议确认或访客直接申请均创建 REQUESTED；内部系统接受或认领前不得显示客服已接入。
CREATE TABLE IF NOT EXISTS `cs_handoff_episode` (
    `tenant_id` VARCHAR(64) NOT NULL COMMENT '服务端认证得到的租户归属',
    `top_org_id` VARCHAR(64) NOT NULL COMMENT '服务端解析得到的顶级组织归属',
    `episode_id` VARCHAR(64) NOT NULL COMMENT '一次访客人工处理周期标识',
    `conversation_id` VARCHAR(128) NOT NULL COMMENT '关联的既有 AI conversation 标识',
    `proposal_id` VARCHAR(64) NULL COMMENT 'AI 建议确认来源时的 proposal 标识；访客直接申请为空',
    `request_id` VARCHAR(128) NOT NULL COMMENT '创建该 episode 的访客命令幂等键',
    `assistant_config_id` VARCHAR(64) NOT NULL COMMENT '可信上下文绑定的智能体配置',
    `bvent_id` VARCHAR(64) NULL COMMENT '可信上下文绑定的大会标识',
    `trace_id` VARCHAR(128) NOT NULL COMMENT '关联 Tool、确认命令和 Outbox 的追踪标识',
    `turn_id` VARCHAR(128) NULL COMMENT 'AI 建议确认来源的轮次；访客直接申请为空',
    `trigger_source` VARCHAR(48) NOT NULL COMMENT '转人工触发来源：访客直接申请或 AI 建议确认',
    `reason_code` VARCHAR(48) NOT NULL COMMENT '来源对应的固定业务原因码，直接申请为 USER_REQUESTED',
    `status` VARCHAR(24) NOT NULL COMMENT 'REQUESTED 表示本地已记录意图，不表示内部系统已接受或认领',
    `version` BIGINT NOT NULL COMMENT 'episode 状态版本，后续状态机转移单调递增',
    `trigger_principal_id` VARCHAR(128) NOT NULL COMMENT '触发转人工申请的服务端认证访客主体',
    `requested_at` DATETIME(6) NOT NULL COMMENT '确认事实提交 UTC 时间',
    PRIMARY KEY (`tenant_id`, `top_org_id`, `episode_id`),
    UNIQUE KEY `uq_cs_handoff_episode_proposal` (`tenant_id`, `top_org_id`, `proposal_id`),
    UNIQUE KEY `uq_cs_handoff_episode_request` (
        `tenant_id`,
        `top_org_id`,
        `conversation_id`,
        `request_id`
    ),
    -- 取消意图的复合外键以此唯一键同时约束租户、组织、会话和周期归属。
    UNIQUE KEY `uq_cs_handoff_episode_scope` (
        `tenant_id`,
        `top_org_id`,
        `conversation_id`,
        `episode_id`
    ),
    KEY `idx_cs_handoff_episode_conversation` (
        `tenant_id`,
        `top_org_id`,
        `conversation_id`,
        `requested_at`
    ),
    CONSTRAINT `fk_cs_handoff_episode_proposal` FOREIGN KEY (`tenant_id`, `top_org_id`, `proposal_id`) REFERENCES `cs_handoff_proposal` (`tenant_id`, `top_org_id`, `proposal_id`),
    CONSTRAINT `ck_cs_handoff_episode_source` CHECK (
        `trigger_source` IN (
            'AI_RECOMMENDATION_CONFIRMED',
            'VISITOR_DIRECT_REQUEST'
        )
    ),
    CONSTRAINT `ck_cs_handoff_episode_source_ref` CHECK (
        (
            `trigger_source` = 'AI_RECOMMENDATION_CONFIRMED'
            AND `proposal_id` IS NOT NULL
            AND `turn_id` IS NOT NULL
        )
        OR (
            `trigger_source` = 'VISITOR_DIRECT_REQUEST'
            AND `proposal_id` IS NULL
            AND `turn_id` IS NULL
        )
    ),
    CONSTRAINT `ck_cs_handoff_episode_reason` CHECK (
        `reason_code` IN (
            'UNRESOLVED_AFTER_RETRY',
            'USER_DISSATISFIED',
            'REQUIRES_HUMAN_JUDGMENT',
            'USER_REQUESTED'
        )
    ),
    CONSTRAINT `ck_cs_handoff_episode_status` CHECK (`status` IN ('REQUESTED')),
    CONSTRAINT `ck_cs_handoff_episode_version` CHECK (`version` > 0)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COMMENT = '客服编排 owner 持有的已确认转人工周期；不代表外部客服接入成功';

-- 访客直接申请命令回执；requestId 同参重放返回原 episode，异参重用必须拒绝。
CREATE TABLE IF NOT EXISTS `cs_handoff_direct_command` (
    `tenant_id` VARCHAR(64) NOT NULL COMMENT '服务端认证得到的租户归属',
    `top_org_id` VARCHAR(64) NOT NULL COMMENT '服务端 Conversation owner 核验的顶级组织',
    `conversation_id` VARCHAR(128) NOT NULL COMMENT '已授权 AI conversation 标识',
    `request_id` VARCHAR(128) NOT NULL COMMENT '访客重试复用的直接申请幂等键',
    `principal_id` VARCHAR(128) NOT NULL COMMENT '认证 Adapter 解析的访客主体',
    `input_fingerprint` CHAR(64) NOT NULL COMMENT '命令归属摘要，发现同键异参重用',
    `episode_id` VARCHAR(64) NOT NULL COMMENT '首次直接申请创建的唯一接待周期',
    `episode_version` BIGINT NOT NULL COMMENT '直接申请初始 episode 版本，固定为 1',
    `created_at` DATETIME(6) NOT NULL COMMENT '幂等回执创建 UTC 时间',
    -- 唯一键按租户、组织、会话和 requestId 限制重复命令只保留一份回执。
    PRIMARY KEY (
        `tenant_id`,
        `top_org_id`,
        `conversation_id`,
        `request_id`
    ),
    -- 二级索引支持按 episode 追溯该访客命令回执。
    KEY `idx_cs_handoff_direct_command_episode` (`tenant_id`, `top_org_id`, `episode_id`) COMMENT '按接待周期追溯直接申请回执',
    -- 外键保证命令回执只能指向同租户和组织下真实创建的 episode。
    CONSTRAINT `fk_cs_handoff_direct_command_episode` FOREIGN KEY (`tenant_id`, `top_org_id`, `episode_id`) REFERENCES `cs_handoff_episode` (`tenant_id`, `top_org_id`, `episode_id`),
    -- 初始回执只接受新建 episode 的版本 1。
    CONSTRAINT `ck_cs_handoff_direct_command_version` CHECK (`episode_version` = 1)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COMMENT = '访客直接转人工命令的稳定幂等回执';

-- 确认/拒绝命令回执；requestId 重用异参时拒绝，重复确认同一 proposal 由唯一约束返回原 episode。
CREATE TABLE IF NOT EXISTS `cs_handoff_proposal_command` (
    `tenant_id` VARCHAR(64) NOT NULL COMMENT '服务端认证得到的租户归属',
    `top_org_id` VARCHAR(64) NOT NULL COMMENT '服务端解析得到的顶级组织归属',
    `conversation_id` VARCHAR(128) NOT NULL COMMENT '命令已授权的 AI conversation 标识',
    `request_id` VARCHAR(128) NOT NULL COMMENT '访客重试复用的命令幂等键',
    `proposal_id` VARCHAR(64) NOT NULL COMMENT '命令指向的建议卡标识',
    `principal_id` VARCHAR(128) NOT NULL COMMENT '服务端认证得到的访客主体',
    `decision` VARCHAR(16) NOT NULL COMMENT 'CONFIRM 或 DISMISS',
    `input_fingerprint` CHAR(64) NOT NULL COMMENT '请求主体、动作和版本的规范化 SHA-256 摘要',
    `proposal_status` VARCHAR(16) NOT NULL COMMENT '提交后的建议卡状态',
    `episode_id` VARCHAR(64) NULL COMMENT '确认后唯一 episode 标识，拒绝或过期时为空',
    `proposal_version` BIGINT NOT NULL COMMENT '提交后的建议版本',
    `created_at` DATETIME(6) NOT NULL COMMENT '命令回执 UTC 创建时间',
    PRIMARY KEY (
        `tenant_id`,
        `top_org_id`,
        `conversation_id`,
        `request_id`
    ),
    KEY `idx_cs_handoff_proposal_command_proposal` (`tenant_id`, `top_org_id`, `proposal_id`),
    CONSTRAINT `fk_cs_handoff_proposal_command_proposal` FOREIGN KEY (`tenant_id`, `top_org_id`, `proposal_id`) REFERENCES `cs_handoff_proposal` (`tenant_id`, `top_org_id`, `proposal_id`),
    CONSTRAINT `ck_cs_handoff_proposal_command_decision` CHECK (`decision` IN ('CONFIRM', 'DISMISS')),
    CONSTRAINT `ck_cs_handoff_proposal_command_status` CHECK (
        `proposal_status` IN ('CONFIRMED', 'DISMISSED', 'EXPIRED')
    ),
    CONSTRAINT `ck_cs_handoff_proposal_command_result` CHECK (
        (
            proposal_status = 'CONFIRMED'
            AND episode_id IS NOT NULL
        )
        OR (
            proposal_status <> 'CONFIRMED'
            AND episode_id IS NULL
        )
    ),
    CONSTRAINT `ck_cs_handoff_proposal_command_version` CHECK (`proposal_version` > 0)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COMMENT = '客服建议确认/拒绝的命令幂等回执';

-- 独立 handoff Outbox；不引用 cs_event/cs_session，使 AI conversation 未建客服 session 时仍可原子提交。
CREATE TABLE IF NOT EXISTS `cs_handoff_orchestration_outbox` (
    `tenant_id` VARCHAR(64) NOT NULL COMMENT '服务端认证得到的租户归属',
    `top_org_id` VARCHAR(64) NOT NULL COMMENT '服务端解析得到的顶级组织归属',
    `outbox_id` VARCHAR(64) NOT NULL COMMENT '可靠投递任务稳定幂等标识',
    `event_id` VARCHAR(64) NOT NULL COMMENT 'handoff 业务事件唯一标识',
    `event_type` VARCHAR(64) NOT NULL COMMENT 'handoff.proposed、handoff.requested 或建议决策事件',
    `conversation_id` VARCHAR(128) NOT NULL COMMENT '关联的 AI conversation，不等同客服 session',
    `aggregate_id` VARCHAR(64) NOT NULL COMMENT 'proposal 或 handoff episode 关联对象标识',
    `assistant_config_id` VARCHAR(64) NOT NULL COMMENT '可信上下文智能体配置标识',
    `trace_id` VARCHAR(128) NOT NULL COMMENT '供异步消费者继续传播的追踪标识',
    `turn_id` VARCHAR(128) NULL COMMENT '关联 AI 轮次；访客直接申请可为空',
    `bvent_id` VARCHAR(64) NULL COMMENT '可信上下文大会标识',
    `schema_version` INT NOT NULL COMMENT '事件 envelope 契约版本，当前为 2',
    `payload_json` TEXT NOT NULL COMMENT '由服务端序列化校验的脱敏 JSON 事件载荷',
    `status` VARCHAR(16) NOT NULL COMMENT 'PENDING、CLAIMED、DELIVERED 或 DEAD',
    `attempt_count` INT NOT NULL COMMENT '累计投递尝试次数',
    `max_attempts` INT NOT NULL COMMENT '有限重试上限，耗尽后进入 DEAD',
    `next_attempt_at` DATETIME(6) NOT NULL COMMENT '首次等于创建时间，失败后按有界退避推进',
    `lease_until` DATETIME(6) NULL COMMENT '领取租约截止时间，过期后允许恢复领取',
    `claimed_by` VARCHAR(128) NULL COMMENT '当前 Worker 租约持有者',
    `last_error` VARCHAR(512) NULL COMMENT '脱敏最近失败摘要，不包含凭据和聊天正文',
    `created_at` DATETIME(6) NOT NULL COMMENT '与业务事实同事务创建时间',
    `delivered_at` DATETIME(6) NULL COMMENT '下游确认后的 UTC 时间，未送达为空',
    PRIMARY KEY (`tenant_id`, `top_org_id`, `outbox_id`),
    UNIQUE KEY `uq_cs_handoff_outbox_event` (`tenant_id`, `top_org_id`, `event_id`),
    KEY `idx_cs_handoff_outbox_due` (`status`, `next_attempt_at`, `created_at`),
    CONSTRAINT `ck_cs_handoff_outbox_attempts` CHECK (
        `attempt_count` >= 0
        AND `max_attempts` > 0
        AND `attempt_count` <= `max_attempts`
    ),
    CONSTRAINT `ck_cs_handoff_outbox_status` CHECK (
        `status` IN ('PENDING', 'CLAIMED', 'DELIVERED', 'DEAD')
    ),
    CONSTRAINT `ck_cs_handoff_outbox_schema` CHECK (`schema_version` = 2)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COMMENT = 'AI conversation handoff 事件独立 Outbox，支持持久化后恢复投递';

-- 来源片段：data/sql/mysql/ai_20261029_customer_service/formal/create_customer_service_handoff_cancellation.sql
-- ============================================================================
-- 目的：保存访客取消人工的意图和幂等回执，禁止将请求取消冒充外部已撤销。
-- 方言：MySQL 8.0.16+/InnoDB；依赖强制执行 CHECK 与复合外键。
-- 前提：先执行 create_customer_service_handoff_proposal.sql，含 uq_cs_handoff_episode_scope。
-- 数据 owner/写入方：客服编排服务；读取方：访客恢复应用服务、未来内部系统取消 Adapter。
-- 生命周期：与 episode 一同保留审计，不做逻辑删除，不单独删除幂等回执；经批准统一归档。
-- 所有非空字段均为重放、授权、审计必要事实；租户/组织/会话用复合外键防止跨范围引用。
-- 初始状态唯一为 PENDING_OWNER_ACK；未来确认契约明确后再扩展终态和回执，不提供旧版双写。
-- 回滚：先关闭取消入口及相关投递，归档取消和 Outbox 事实，再删除本表；不得清除未确认意图。
-- 回滚不恢复 AI，也不撤销外部申请；父周期删除使用默认 RESTRICT，禁止级联丢失取消审计。
-- ============================================================================
CREATE TABLE IF NOT EXISTS `cs_handoff_cancellation` (
    `tenant_id` VARCHAR(64) NOT NULL COMMENT '正式认证与 Conversation owner 核验的租户',
    `top_org_id` VARCHAR(64) NOT NULL COMMENT 'Conversation 持久化顶级组织',
    `conversation_id` VARCHAR(128) NOT NULL COMMENT '取消意图所属的已授权 AI 会话',
    `episode_id` VARCHAR(64) NOT NULL COMMENT '申请撤销的人工周期，必须属于同会话',
    `cancellation_id` VARCHAR(64) NOT NULL COMMENT '服务端生成的取消意图标识及下游幂等引用',
    `request_id` VARCHAR(128) NOT NULL COMMENT '访客重试必须复用的命令幂等键',
    `principal_id` VARCHAR(128) NOT NULL COMMENT '正式认证的原申请访客主体，应用层与周期校验',
    `expected_episode_version` BIGINT NOT NULL COMMENT '提交取消时访客看到的周期版本，大于零',
    `input_fingerprint` CHAR(64) NOT NULL COMMENT '主体、会话路由、周期和期望版本的 SHA-256 摘要',
    `trace_id` VARCHAR(128) NOT NULL COMMENT '首次取消命令的服务端追踪标识，传播至 Outbox',
    `status` VARCHAR(24) NOT NULL COMMENT 'PENDING_OWNER_ACK 仅表示等待外部 owner 确认，AI 不恢复',
    `created_at` DATETIME(6) NOT NULL COMMENT '取消意图首次持久化 UTC 时间，重放不修改',
    -- 主键按租户和组织隔离稳定取消引用。
    PRIMARY KEY (`tenant_id`, `top_org_id`, `cancellation_id`),
    -- 一周期至多一个待确认取消意图，避免不同命令重复通知。
    UNIQUE KEY `uq_cs_handoff_cancellation_episode` (`tenant_id`, `top_org_id`, `episode_id`),
    -- 一会话内幂等键不能用于不同周期或期望版本。
    UNIQUE KEY `uq_cs_handoff_cancellation_request` (
        `tenant_id`,
        `top_org_id`,
        `conversation_id`,
        `request_id`
    ),
    -- 全范围外键禁止将取消意图挂到其他组织或会话的 episode。
    CONSTRAINT `fk_cs_handoff_cancellation_episode` FOREIGN KEY (
        `tenant_id`,
        `top_org_id`,
        `conversation_id`,
        `episode_id`
    ) REFERENCES `cs_handoff_episode` (
        `tenant_id`,
        `top_org_id`,
        `conversation_id`,
        `episode_id`
    ),
    -- 未接通取消回执前仅允许持久化待确认意图。
    CONSTRAINT `ck_cs_handoff_cancellation_status` CHECK (`status` IN ('PENDING_OWNER_ACK')),
    -- 取消命令必须引用用户确实看到的有效状态版本。
    CONSTRAINT `ck_cs_handoff_cancellation_version` CHECK (`expected_episode_version` > 0)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COMMENT = '客服编排 owner 保存的取消意图及稳定幂等回执，不代表外部已取消';

-- 来源片段：data/sql/mysql/ai_20261107_ai_resolution/formal/create_ai_resolution_candidate.sql
-- ============================================================================
-- 目的：保存模型候选、确定性用户明确结束信号；只记录事实，不创建问卷、不关闭会话、不改变回复 owner。
-- 方言：MySQL 8.0.16+/InnoDB，依赖强制 CHECK 和复合外键。
-- 执行顺序：所需父表已在本合并脚本前文创建。
-- 数据 owner/唯一写入方：AI Conversation 应用服务；读取方：未来的结束资格判断应用服务。
-- 生命周期：候选与用户明确结束信号为不可变审计事实，不做逻辑删除；新版不创建或处理空闲期限。
-- 新消息/owner 变化使 generation_epoch 失效，但不删除旧候选；读取前必须核验当前 AI 代次。
-- 租户/组织/会话复合外键阻止跨范围引用；assistant、大会、代次由 owner 行锁核验，不由模型指定。
-- 执行方式：本迁移只执行一次，父表新增唯一约束不是 IF NOT EXISTS；重复执行前先核查约束。
-- MySQL DDL 非整体事务：若中途失败，应核对已建约束和表，再从失败步骤修复，不能盲目整脚本重跑。
-- 回滚：保持 Tool/Worker 禁用，停止写入并归档事实后先删除三个子表，再移除父表新增唯一约束；
--       不修改 generation 事实，不将删除候选解释为取消人工或恢复 AI。无旧版双写/兼容要求。
-- ============================================================================
-- 由 Conversation owner 增加完整组织范围唯一键，仅用于候选归属外键，不改变原主键。
-- 当前库已存在该对象时跳过；缺失才补建，不删除或重写业务记录。
SET
    @cs_migration_ddl = IF(
        EXISTS (
            SELECT
                1
            FROM
                information_schema.statistics
            WHERE
                table_schema = DATABASE()
                AND table_name = 'ai_conversation_generation'
                AND index_name = 'uq_ai_generation_resolution_scope'
        ),
        'SELECT 1',
        'ALTER TABLE `ai_conversation_generation`
  ADD CONSTRAINT `uq_ai_generation_resolution_scope` UNIQUE (`tenant_id`, `top_org_id`, `conversation_id`)'
    );

PREPARE cs_migration_statement
FROM
    @cs_migration_ddl;

EXECUTE cs_migration_statement;

DEALLOCATE PREPARE cs_migration_statement;

CREATE TABLE IF NOT EXISTS `ai_resolution_candidate` (
    `tenant_id` VARCHAR(64) NOT NULL COMMENT '可信请求快照中的租户，须匹配 Conversation owner',
    `top_org_id` VARCHAR(64) NOT NULL COMMENT '服务端 assistant 解析的顶级组织，须匹配 Conversation owner',
    `conversation_id` VARCHAR(128) NOT NULL COMMENT '既有 AI 会话标识，不是独立客服 session',
    `resolution_episode_id` VARCHAR(64) NOT NULL COMMENT '服务端生成的稳定结束候选引用，不等于问卷或已解决事实',
    `assistant_config_id` VARCHAR(64) NOT NULL COMMENT '可信会话绑定的智能体配置，应用层加 owner 行锁核验',
    `bvent_id` VARCHAR(64) NULL COMMENT '可信大会引用，组织级助手无大会时为空',
    `turn_id` VARCHAR(128) NOT NULL COMMENT '提出候选的真实 AI 轮次，同轮只允许一条候选',
    `generation_epoch` BIGINT NOT NULL COMMENT '提出候选时的正数 AI 代次，后续代次变化使其失去当前资格',
    `trace_id` VARCHAR(128) NOT NULL COMMENT '首次记录的追踪标识，重试不覆盖',
    `signal_type` VARCHAR(32) NOT NULL COMMENT '唯一值 MODEL_POSSIBLE_RESOLUTION，不能写用户确认',
    `confidence` DOUBLE NOT NULL COMMENT '模型零到一置信度，服务端阈值决定是否记录，不等于用户满意',
    `policy_version` VARCHAR(64) NOT NULL COMMENT '服务端候选策略版本，默认 ai-resolution-candidate-v1',
    `input_fingerprint` CHAR(64) NOT NULL COMMENT '归属、轮次、代次、置信度和策略的 SHA-256，不含 trace',
    `proposed_at` DATETIME(6) NOT NULL COMMENT '首次记录 UTC 时间，幂等重放不修改',
    -- 主键隔离租户和组织，稳定引用不能被不同范围复用。
    PRIMARY KEY (
        `tenant_id`,
        `top_org_id`,
        `resolution_episode_id`
    ),
    -- 同轮唯一：重复工具回调不得生成多个候选或覆盖不同置信输入。
    UNIQUE KEY `uq_ai_resolution_candidate_turn` (
        `tenant_id`,
        `top_org_id`,
        `conversation_id`,
        `turn_id`
    ),
    -- 为当前 generation 的候选恢复提供有界定位，不扫描聊天正文或客服表。
    KEY `idx_ai_resolution_candidate_generation` (
        `tenant_id`,
        `top_org_id`,
        `conversation_id`,
        `generation_epoch`
    ),
    -- 禁止引用另一组织的 Conversation；默认 RESTRICT，不级联丢失审计。
    CONSTRAINT `fk_ai_resolution_candidate_scope` FOREIGN KEY (`tenant_id`, `top_org_id`, `conversation_id`) REFERENCES `ai_conversation_generation` (`tenant_id`, `top_org_id`, `conversation_id`),
    -- 非空字段均为绑定、重放、时效性或审计必要信息；大会是唯一可空范围字段。
    CONSTRAINT `ck_ai_resolution_candidate_epoch` CHECK (`generation_epoch` > 0),
    CONSTRAINT `ck_ai_resolution_candidate_confidence` CHECK (
        `confidence` >= 0
        AND `confidence` <= 1
    ),
    CONSTRAINT `ck_ai_resolution_candidate_signal` CHECK (`signal_type` = 'MODEL_POSSIBLE_RESOLUTION')
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COMMENT = 'Conversation owner 的不可变 AI 结束候选；无关闭对话或发送评价副作用';

-- 确定性用户结束事实；分类证据正文仅在事务内读取，表中只保留消息引用和摘要。
CREATE TABLE IF NOT EXISTS `ai_resolution_user_signal` (
    `tenant_id` VARCHAR(64) NOT NULL COMMENT '可信请求快照中的租户，须匹配 Conversation owner',
    `top_org_id` VARCHAR(64) NOT NULL COMMENT '服务端解析的顶级组织，须匹配 Conversation owner',
    `conversation_id` VARCHAR(128) NOT NULL COMMENT '既有 AI conversation 标识',
    `resolution_signal_id` VARCHAR(64) NOT NULL COMMENT '服务端生成的稳定用户结束信号引用',
    `assistant_config_id` VARCHAR(64) NOT NULL COMMENT '可信 conversation 绑定的智能体配置',
    `bvent_id` VARCHAR(64) NULL COMMENT '可信大会范围；组织级助手为空',
    `turn_id` VARCHAR(128) NOT NULL COMMENT '用户表达及成功 AI 回复所属 Turn',
    `generation_epoch` BIGINT NOT NULL COMMENT '写入时持有的 AI 回复代次；新代次或人工 owner 使其失效',
    `source_user_detail_id` VARCHAR(50) NOT NULL COMMENT '触发分类的已保存用户消息引用，不复制正文',
    `previous_assistant_turn_id` VARCHAR(128) NULL COMMENT '否定短答成立时上一条成功追问的 Turn',
    `previous_assistant_detail_id` VARCHAR(50) NULL COMMENT '否定短答成立时上一条成功追问的消息引用',
    `trace_id` VARCHAR(128) NOT NULL COMMENT '成功 AI Turn 的追踪引用',
    `signal_type` VARCHAR(48) NOT NULL COMMENT '服务端确定性类型：USER_EXPLICIT_END、USER_CONFIRMED_NO_FURTHER_QUESTION',
    `policy_version` VARCHAR(64) NOT NULL COMMENT '服务端确定性结束规则版本',
    `evidence_fingerprint` CHAR(64) NOT NULL COMMENT '范围、消息引用、代次、类型和规则的 SHA-256，不含正文',
    `recorded_at` DATETIME(6) NOT NULL COMMENT '首次记录 UTC 时间，同 Turn 重放不改写',
    -- 信号主键包含范围；同 Turn 同类型只允许一个确定性事实，仅由用户明确表达产生。
    PRIMARY KEY (`tenant_id`, `top_org_id`, `resolution_signal_id`),
    UNIQUE KEY `uq_ai_resolution_user_signal_turn` (
        `tenant_id`,
        `top_org_id`,
        `conversation_id`,
        `turn_id`,
        `signal_type`
    ),
    KEY `idx_ai_resolution_user_signal_generation` (
        `tenant_id`,
        `top_org_id`,
        `conversation_id`,
        `generation_epoch`
    ),
    -- Conversation owner 外键阻止跨租户/组织会话引用，默认 RESTRICT 保留审计。
    CONSTRAINT `fk_ai_resolution_user_signal_scope` FOREIGN KEY (`tenant_id`, `top_org_id`, `conversation_id`) REFERENCES `ai_conversation_generation` (`tenant_id`, `top_org_id`, `conversation_id`),
    CONSTRAINT `ck_ai_resolution_user_signal_epoch` CHECK (`generation_epoch` > 0),
    CONSTRAINT `ck_ai_resolution_user_signal_type` CHECK (
        `signal_type` IN (
            'USER_EXPLICIT_END',
            'USER_CONFIRMED_NO_FURTHER_QUESTION'
        )
    ),
    CONSTRAINT `ck_ai_resolution_user_signal_previous` CHECK (
        (
            `signal_type` = 'USER_CONFIRMED_NO_FURTHER_QUESTION'
            AND `previous_assistant_turn_id` IS NOT NULL
            AND `previous_assistant_detail_id` IS NOT NULL
        )
        OR (
            `signal_type` = 'USER_EXPLICIT_END'
            AND `previous_assistant_turn_id` IS NULL
            AND `previous_assistant_detail_id` IS NULL
        )
    )
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COMMENT = 'Conversation owner 的不可变用户结束信号；无问卷或人工状态副作用';

-- 来源片段：data/sql/mysql/ai_20261107_ai_resolution/formal/create_ai_resolution_survey.sql
-- ============================================================================
-- 目的：持久化 Conversation owner 每个会话唯一的 AI 满意度调查及可靠 Outbox 状态事件。
-- 方言：MySQL 8.0.16+/InnoDB，依赖 CHECK、复合外键和强制唯一约束。
-- 执行顺序：所需父表已在本合并脚本前文创建。
-- 数据 owner/唯一写入方：AI Conversation 应用服务；客服 owner 只消费 handoff opt-in Outbox。
-- 生命周期：survey 保留 OPEN/SATISFIED/DISSATISFIED/EXPIRED 和 opt-in 选择；Outbox 保留投递/死信审计，不逻辑删除。
-- 租户/组织/会话通过 generation owner 复合外键隔离；一会话唯一约束保证调查至多创建一次。
-- 执行方式：本迁移只执行一次；MySQL DDL 可能逐条提交，失败后核对已建对象再续修，不盲目重跑。
-- 回滚：先保持所有创建入口关闭，停止消费并归档 survey/Outbox，再删除 Outbox 和 survey 表；不改 conversation owner。
-- ============================================================================
CREATE TABLE IF NOT EXISTS `ai_resolution_survey` (
    `tenant_id` VARCHAR(64) NOT NULL COMMENT '可信 conversation 租户归属',
    `top_org_id` VARCHAR(64) NOT NULL COMMENT '可信 conversation 顶级组织归属',
    `conversation_id` VARCHAR(128) NOT NULL COMMENT '调查唯一范围；每个会话最多一份 AI survey',
    `survey_id` VARCHAR(64) NOT NULL COMMENT '服务端生成且可安全恢复的调查标识',
    `resolution_episode_id` VARCHAR(64) NOT NULL COMMENT 'Conversation owner 生成的本次解决候选周期标识',
    `assistant_config_id` VARCHAR(64) NOT NULL COMMENT '当前可信会话智能体绑定',
    `bvent_id` VARCHAR(64) NULL COMMENT '可信大会范围，组织级助手为空',
    `generation_epoch` BIGINT NOT NULL COMMENT '创建调查时的 AI 回复代次，必须由 owner 行锁核验',
    `turn_id` VARCHAR(128) NOT NULL COMMENT '建立结束候选的成功 AI Turn',
    `source_type` VARCHAR(32) NOT NULL COMMENT '来源类型：MODEL_CANDIDATE 或 CONVERSATION_SIGNAL',
    `source_ref` VARCHAR(64) NOT NULL COMMENT '模型候选或确定性信号的稳定来源引用',
    `trace_id` VARCHAR(128) NOT NULL COMMENT '调查创建链路追踪引用，不含聊天正文',
    `policy_version` VARCHAR(64) NOT NULL COMMENT '调查资格与期限策略版本',
    `status` VARCHAR(24) NOT NULL COMMENT '调查状态：OPEN/SATISFIED/DISSATISFIED/EXPIRED',
    `handoff_choice` VARCHAR(16) NOT NULL COMMENT '不满意后的人工选择：NOT_APPLICABLE/PENDING/OPTED_IN/DECLINED',
    `opened_at` DATETIME(6) NOT NULL COMMENT '调查卡首次创建 UTC 时间',
    `expires_at` DATETIME(6) NOT NULL COMMENT '调查截止 UTC 时间；到期只标记 EXPIRED',
    `responded_at` DATETIME(6) NULL COMMENT '首次满意度回答 UTC 时间，不满意的后续 opt-in 单独版本化',
    `version` BIGINT NOT NULL COMMENT 'survey 状态版本，客户端命令必须带 expectedVersion',
    PRIMARY KEY (`tenant_id`, `top_org_id`, `survey_id`),
    UNIQUE KEY `uq_ai_resolution_survey_conversation` (`tenant_id`, `top_org_id`, `conversation_id`),
    UNIQUE KEY `uq_ai_resolution_survey_episode` (
        `tenant_id`,
        `top_org_id`,
        `resolution_episode_id`
    ),
    UNIQUE KEY `uq_ai_resolution_survey_source` (
        `tenant_id`,
        `top_org_id`,
        `source_type`,
        `source_ref`
    ),
    KEY `idx_ai_resolution_survey_expiration` (
        `status`,
        `expires_at`,
        `tenant_id`,
        `top_org_id`,
        `survey_id`
    ),
    CONSTRAINT `fk_ai_resolution_survey_scope` FOREIGN KEY (`tenant_id`, `top_org_id`, `conversation_id`) REFERENCES `ai_conversation_generation` (`tenant_id`, `top_org_id`, `conversation_id`),
    CONSTRAINT `ck_ai_resolution_survey_epoch` CHECK (`generation_epoch` > 0),
    CONSTRAINT `ck_ai_resolution_survey_version` CHECK (`version` > 0),
    CONSTRAINT `ck_ai_resolution_survey_time` CHECK (`expires_at` > `opened_at`),
    -- 调查响应必须发生在开放时间到截止时间区间内，与回答命令的截止边界一致。
    CONSTRAINT `ck_ai_resolution_survey_response_time` CHECK (
        `responded_at` IS NULL
        OR (
            `responded_at` >= `opened_at`
            AND `responded_at` < `expires_at`
        )
    ),
    CONSTRAINT `ck_ai_resolution_survey_source` CHECK (
        `source_type` IN ('MODEL_CANDIDATE', 'CONVERSATION_SIGNAL')
    ),
    CONSTRAINT `ck_ai_resolution_survey_status` CHECK (
        `status` IN ('OPEN', 'SATISFIED', 'DISSATISFIED', 'EXPIRED')
    ),
    CONSTRAINT `ck_ai_resolution_survey_handoff` CHECK (
        `handoff_choice` IN (
            'NOT_APPLICABLE',
            'PENDING',
            'OPTED_IN',
            'DECLINED'
        )
    ),
    CONSTRAINT `ck_ai_resolution_survey_state` CHECK (
        (
            `status` = 'OPEN'
            AND `handoff_choice` = 'NOT_APPLICABLE'
            AND `responded_at` IS NULL
        )
        OR (
            `status` = 'SATISFIED'
            AND `handoff_choice` = 'NOT_APPLICABLE'
            AND `responded_at` IS NOT NULL
        )
        OR (
            `status` = 'DISSATISFIED'
            AND `handoff_choice` IN ('PENDING', 'OPTED_IN', 'DECLINED')
            AND `responded_at` IS NOT NULL
        )
        OR (
            `status` = 'EXPIRED'
            AND `handoff_choice` = 'NOT_APPLICABLE'
            AND `responded_at` IS NULL
        )
    )
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COMMENT = 'Conversation owner 的单次 AI 满意度调查状态';

-- survey 状态与待投递事件同事务写入；不能以数据库存在事件表示访客或客服 owner 已收到。
CREATE TABLE IF NOT EXISTS `ai_resolution_survey_outbox` (
    `tenant_id` VARCHAR(64) NOT NULL COMMENT '事件所属可信租户',
    `top_org_id` VARCHAR(64) NOT NULL COMMENT '事件所属可信组织',
    `outbox_id` VARCHAR(64) NOT NULL COMMENT '内部投递任务标识',
    `event_id` VARCHAR(64) NOT NULL COMMENT '跨节点重试保持稳定的事件标识',
    `survey_id` VARCHAR(64) NOT NULL COMMENT '关联的单次 AI 满意度调查',
    `conversation_id` VARCHAR(128) NOT NULL COMMENT '关联 AI conversation，不是客服 session',
    `event_type` VARCHAR(64) NOT NULL COMMENT 'ai.survey.offered/answered/expired/handoff-choice 或 handoff opt-in 事件',
    `state_version` BIGINT NOT NULL COMMENT '产生该事件的 survey 状态版本',
    `payload_json` JSON NOT NULL COMMENT '仅含稳定业务引用、状态和版本，不含聊天正文/联系方式',
    `status` VARCHAR(16) NOT NULL COMMENT '投递状态：PENDING/CLAIMED/DELIVERED/DEAD',
    `attempt_count` INT NOT NULL DEFAULT 0 COMMENT '已领取投递次数',
    `max_attempts` INT NOT NULL DEFAULT 10 COMMENT '最多尝试次数，耗尽后保留 DEAD 运维线索',
    `next_attempt_at` DATETIME(6) NOT NULL COMMENT '首次可投递时间及失败后的重试时间 UTC',
    `lease_until` DATETIME(6) NULL COMMENT 'CLAIMED 任务租约截止时间 UTC',
    `claimed_by` VARCHAR(128) NULL COMMENT '当前投递 Worker 租约持有人',
    `delivered_at` DATETIME(6) NULL COMMENT '下游确认接收时间，不表示用户已查看',
    `last_error` VARCHAR(128) NULL COMMENT '最后一次非敏感错误类型',
    `created_at` DATETIME(6) NOT NULL COMMENT '事件与状态同事务写入时间 UTC',
    PRIMARY KEY (`tenant_id`, `top_org_id`, `outbox_id`),
    UNIQUE KEY `uq_ai_resolution_survey_event` (`tenant_id`, `top_org_id`, `event_id`),
    UNIQUE KEY `uq_ai_resolution_survey_transition_event` (
        `tenant_id`,
        `top_org_id`,
        `survey_id`,
        `event_type`,
        `state_version`
    ),
    KEY `idx_ai_resolution_survey_outbox_claim` (
        `status`,
        `next_attempt_at`,
        `lease_until`,
        `created_at`
    ),
    CONSTRAINT `fk_ai_resolution_survey_outbox_survey` FOREIGN KEY (`tenant_id`, `top_org_id`, `survey_id`) REFERENCES `ai_resolution_survey` (`tenant_id`, `top_org_id`, `survey_id`),
    CONSTRAINT `ck_ai_resolution_survey_outbox_version` CHECK (`state_version` > 0),
    CONSTRAINT `ck_ai_resolution_survey_outbox_attempts` CHECK (
        `attempt_count` >= 0
        AND `max_attempts` > 0
        AND `attempt_count` <= `max_attempts`
    ),
    CONSTRAINT `ck_ai_resolution_survey_outbox_status` CHECK (
        `status` IN ('PENDING', 'CLAIMED', 'DELIVERED', 'DEAD')
    )
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COMMENT = 'AI 满意度状态迁移的可靠 Outbox；无 Worker 时事件保持 PENDING';

-- 来源片段：data/sql/mysql/ai_20261108_ai_resolution_assessment/formal/create_ai_survey_content_assessment.sql
-- ============================================================================
-- 目的：持久化完整对话内容评估的结论和证据引用，供 AI 满意度问卷资格门禁复核。
-- 方言：MySQL 8.0.16+/InnoDB，依赖强制 CHECK、复合外键和唯一约束。
-- 执行顺序：所需父表已在本合并脚本前文创建。
-- 数据 owner/唯一写入方：AI Conversation 应用服务；问卷资格服务只读取，不直接修改评估事实。
-- 生命周期：每个 Conversation generation/成功 Turn 一份不可变评估；新消息或人工接管后旧评估保留审计但不得用于当前问卷。
-- 数据最小化：只保留消息引用、分类结果、策略版本、评估引用和 UTC 时间，不复制聊天正文或评估提示词。
-- 执行前提：评估器须先在行锁事务外完成完整历史判断，再由 owner 在锁内复核 generation 后写入；不得在数据库锁内调用模型。
-- 执行方式：只执行一次；MySQL DDL 可能逐条提交，失败后核对对象再修复，不盲目重跑。
-- 回滚/兼容：保持问卷入口关闭并归档评估事实后删除本表；不修改 generation、问卷或人工 owner 状态，无旧版双写兼容要求。
-- ============================================================================
CREATE TABLE IF NOT EXISTS `ai_survey_content_assessment` (
    `tenant_id` VARCHAR(64) NOT NULL COMMENT '可信 Conversation 租户归属',
    `top_org_id` VARCHAR(64) NOT NULL COMMENT '服务端解析的顶级组织归属',
    `conversation_id` VARCHAR(128) NOT NULL COMMENT '被评估的稳定 AI conversation 标识',
    `assessment_id` VARCHAR(64) NOT NULL COMMENT '评估器或 owner 生成的稳定结论引用，不是聊天内容',
    `assistant_config_id` VARCHAR(64) NOT NULL COMMENT '可信会话绑定的智能体配置',
    `bvent_id` VARCHAR(64) NULL COMMENT '可信大会范围；组织级助手为空',
    `generation_epoch` BIGINT NOT NULL COMMENT '评估时持有的 AI generation，新消息或人工接管后失效',
    `turn_id` VARCHAR(128) NOT NULL COMMENT '评估覆盖到的当前成功 AI Turn',
    `source_user_detail_id` VARCHAR(50) NOT NULL COMMENT '当前轮次已保存用户消息引用，不复制正文',
    `policy_version` VARCHAR(64) NOT NULL COMMENT '完整内容评估器的规则或模型策略版本',
    `verdict` VARCHAR(24) NOT NULL COMMENT '完整对话结论：ELIGIBLE/GREETING_ONLY/SPAM/PROMOTION/UNRESOLVED/UNKNOWN',
    `input_fingerprint` CHAR(64) NOT NULL COMMENT '可信范围、消息引用、结论与策略的 SHA-256，不含正文',
    `assessed_at` DATETIME(6) NOT NULL COMMENT '评估完成并由 owner 接受的 UTC 时间',
    PRIMARY KEY (`tenant_id`, `top_org_id`, `assessment_id`),
    -- 同一代次和成功 Turn 只接受一个不可变结论，阻止重试或并发产生冲突资格。
    UNIQUE KEY `uq_ai_survey_assessment_turn` (
        `tenant_id`,
        `top_org_id`,
        `conversation_id`,
        `generation_epoch`,
        `turn_id`
    ),
    -- 当前代次资格查询按可信路由精确定位，不扫描聊天正文或客服 owner 表。
    KEY `idx_ai_survey_assessment_route` (
        `tenant_id`,
        `top_org_id`,
        `conversation_id`,
        `assistant_config_id`,
        `generation_epoch`
    ),
    -- Conversation owner 复合外键阻止跨租户/组织引用，保留审计事实，不级联删除。
    CONSTRAINT `fk_ai_survey_assessment_scope` FOREIGN KEY (`tenant_id`, `top_org_id`, `conversation_id`) REFERENCES `ai_conversation_generation` (`tenant_id`, `top_org_id`, `conversation_id`),
    CONSTRAINT `ck_ai_survey_assessment_epoch` CHECK (`generation_epoch` > 0),
    CONSTRAINT `ck_ai_survey_assessment_verdict` CHECK (
        `verdict` IN (
            'ELIGIBLE',
            'GREETING_ONLY',
            'SPAM',
            'PROMOTION',
            'UNRESOLVED',
            'UNKNOWN'
        )
    )
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COMMENT = 'Conversation owner 的不可变问卷内容评估结论和非正文证据引用';

-- 合并来源文件结束：data/sql/mysql/ai_20261109_customer_service_and_resolution.sql
-- 合并来源文件：data/sql/mysql/ai_20261109_customer_service_reception.sql
-- 目的：兑现本项目作为接单/接待 owner、Conversation 作为人工消息 owner 的 v2 接口。
-- 前提：已执行 ai_20261109_customer_service_and_resolution.sql；这是已升级开发库的增量，不要重跑基线。
-- 方言：MySQL 5.7+ / InnoDB；8.0.16+ 强制 CHECK，5.7 使用脚本末尾触发器执行同等值域约束。单次执行，先备份；DDL 自动提交，执行失败需定位后续跑，禁止清空已有申请。
-- 回滚：先关闭 customer-service.reception.enabled，再按数据 owner 留存要求导出新增事实；存在新状态时不可直接恢复旧 CHECK。
-- 归属：所有表均以 tenant/topOrg/conversation 隔离，复合外键拒绝跨会话引用；不存客服账号/组主数据。
-- 生命周期：不逻辑删除消息/命令/认领事实，沿用会话审计留存；本脚本不自动清理业务数据。
SET
    @reception_ddl = IF(
        CAST(SUBSTRING_INDEX(VERSION(), '.', 1) AS UNSIGNED) >= 8
        AND EXISTS (
            SELECT
                1
            FROM
                information_schema.table_constraints
            WHERE
                constraint_schema = DATABASE()
                AND table_name = 'cs_handoff_episode'
                AND constraint_name = 'ck_cs_handoff_episode_status'
                AND constraint_type = 'CHECK'
        ),
        'ALTER TABLE cs_handoff_episode DROP CHECK ck_cs_handoff_episode_status',
        'SELECT 1'
    );

PREPARE reception_statement
FROM
    @reception_ddl;

EXECUTE reception_statement;

DEALLOCATE PREPARE reception_statement;

SET
    @reception_ddl = IF(
        CAST(SUBSTRING_INDEX(VERSION(), '.', 1) AS UNSIGNED) >= 8,
        'ALTER TABLE cs_handoff_episode ADD CONSTRAINT ck_cs_handoff_episode_status CHECK (status IN (''REQUESTED'',''CLAIMED'',''ACTIVE'',''RESOLVED'',''CANCELLED''))',
        'SELECT 1'
    );

PREPARE reception_statement
FROM
    @reception_ddl;

EXECUTE reception_statement;

DEALLOCATE PREPARE reception_statement;

-- 历史待取消意图仅供审计；本地接单/取消分别原子收敛为 REJECTED/CONFIRMED，不再产生异步取消意图。
SET
    @reception_ddl = IF(
        CAST(SUBSTRING_INDEX(VERSION(), '.', 1) AS UNSIGNED) >= 8
        AND EXISTS (
            SELECT
                1
            FROM
                information_schema.table_constraints
            WHERE
                constraint_schema = DATABASE()
                AND table_name = 'cs_handoff_cancellation'
                AND constraint_name = 'ck_cs_handoff_cancellation_status'
                AND constraint_type = 'CHECK'
        ),
        'ALTER TABLE cs_handoff_cancellation DROP CHECK ck_cs_handoff_cancellation_status',
        'SELECT 1'
    );

PREPARE reception_statement
FROM
    @reception_ddl;

EXECUTE reception_statement;

DEALLOCATE PREPARE reception_statement;

SET
    @reception_ddl = IF(
        CAST(SUBSTRING_INDEX(VERSION(), '.', 1) AS UNSIGNED) >= 8,
        'ALTER TABLE cs_handoff_cancellation ADD CONSTRAINT ck_cs_handoff_cancellation_status CHECK (status IN (''PENDING_OWNER_ACK'',''CONFIRMED'',''REJECTED''))',
        'SELECT 1'
    );

PREPARE reception_statement
FROM
    @reception_ddl;

EXECUTE reception_statement;

DEALLOCATE PREPARE reception_statement;

-- Customer Service owner：一周期只允许一个客服认领；由接单事务写，授权与恢复只读。
CREATE TABLE IF NOT EXISTS cs_handoff_claim (
    tenant_id VARCHAR(64) NOT NULL COMMENT '认证租户',
    top_org_id VARCHAR(64) NOT NULL COMMENT '可信组织',
    conversation_id VARCHAR(128) NOT NULL COMMENT '原 AI 会话引用',
    episode_id VARCHAR(64) NOT NULL COMMENT '唯一接待周期',
    agent_id VARCHAR(128) NOT NULL COMMENT '经用户中心核验的登录账号稳定引用',
    group_id VARCHAR(64) NOT NULL COMMENT '服务器路由且已核验成员资格的外部分组',
    claimed_at DATETIME(6) NOT NULL COMMENT '首次原子接单 UTC 时间',
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
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COMMENT = '客服接待 owner 的唯一认领事实，不维护账号主数据';

-- Customer Service owner：命令幂等回执，状态与回执/Outbox 同事务，不允许异参复用幂等键。
CREATE TABLE IF NOT EXISTS cs_reception_command (
    tenant_id VARCHAR(64) NOT NULL COMMENT '认证租户',
    top_org_id VARCHAR(64) NOT NULL COMMENT '可信组织',
    conversation_id VARCHAR(128) NOT NULL COMMENT '原 AI 会话',
    episode_id VARCHAR(64) NOT NULL COMMENT '目标周期',
    request_id VARCHAR(128) NOT NULL COMMENT '调用者稳定幂等键',
    actor_id VARCHAR(128) NOT NULL COMMENT '认证主体',
    action VARCHAR(24) NOT NULL COMMENT 'CLAIM/RESOLVE/CANCEL',
    fingerprint CHAR(64) NOT NULL COMMENT '身份、参数和版本 SHA256',
    result_status VARCHAR(24) NOT NULL COMMENT '首次命令返回的状态',
    result_version BIGINT NOT NULL COMMENT '首次命令返回版本',
    created_at DATETIME(6) NOT NULL COMMENT '首次写入 UTC 时间',
    trace_id VARCHAR(128) NOT NULL COMMENT '完整业务链路追踪',
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
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COMMENT = '接待状态命令不可变幂等回执';

-- Conversation owner：人工模式双方消息及可重放投递日志；应用锁 generation 后分配 seq，同事务写正文。
CREATE TABLE IF NOT EXISTS ai_human_message (
    tenant_id VARCHAR(64) NOT NULL COMMENT '可信租户',
    top_org_id VARCHAR(64) NOT NULL COMMENT '可信组织',
    conversation_id VARCHAR(128) NOT NULL COMMENT '同一 AI 会话',
    seq BIGINT NOT NULL COMMENT '同会话单调递增消息水位',
    message_id VARCHAR(64) NOT NULL COMMENT '服务器生成稳定消息 ID',
    episode_id VARCHAR(64) NOT NULL COMMENT '消息所属接待周期引用',
    sender_role VARCHAR(16) NOT NULL COMMENT 'VISITOR 或 AGENT，服务端决定',
    sender_id VARCHAR(128) NOT NULL COMMENT '认证发送主体',
    client_msg_id VARCHAR(128) NOT NULL COMMENT '发送端重试复用幂等键',
    content VARCHAR(4000) NOT NULL COMMENT '纯文本正文，禁止作为 HTML 执行',
    trace_id VARCHAR(128) NOT NULL COMMENT '消息链路追踪',
    created_at DATETIME(6) NOT NULL COMMENT '持久化 UTC 时间',
    PRIMARY KEY (tenant_id, top_org_id, conversation_id, seq),
    UNIQUE KEY uq_ai_human_message_id (tenant_id, top_org_id, message_id),
    UNIQUE KEY uq_ai_human_message_retry (
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
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COMMENT = 'Conversation owner 人工模式消息及断线恢复日志';

-- Conversation owner：每个参与者在同会话的接收/已读水位，只能单调推进，不以 HTTP 发送成功冒充对端收到。
CREATE TABLE IF NOT EXISTS ai_human_receipt (
    tenant_id VARCHAR(64) NOT NULL COMMENT '可信租户',
    top_org_id VARCHAR(64) NOT NULL COMMENT '可信组织',
    conversation_id VARCHAR(128) NOT NULL COMMENT '原 AI 会话',
    principal_id VARCHAR(128) NOT NULL COMMENT '认证回执提交者',
    delivered_seq BIGINT NOT NULL COMMENT '已确认收到的最大消息序列，缺省 0',
    read_seq BIGINT NOT NULL COMMENT '已读最大序列，缺省 0，不能超过接收水位',
    updated_at DATETIME(6) NOT NULL COMMENT '最近实际推进 UTC 时间',
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
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COMMENT = 'Conversation owner 参与者接收与已读水位';

-- 数据库值域兜底：CHECK 在 5.7 不生效，触发器用非空列赋 NULL 明确拒绝非法状态/水位。
-- 不调用远程系统、不改业务 owner；更新和插入均校验，合法状态转换仍由应用集中状态机控制。
-- cs_handoff_episode INSERT：非法业务值令非空字段校验失败，不静默截断。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_handoff_episode_insert_valid;

CREATE TRIGGER trg_cs_handoff_episode_insert_valid BEFORE
INSERT
    ON cs_handoff_episode FOR EACH ROW
SET
    NEW.status = IF(
        NEW.status IN (
            'REQUESTED',
            'CLAIMED',
            'ACTIVE',
            'RESOLVED',
            'CANCELLED'
        ),
        NEW.status,
        NULL
    );

-- cs_handoff_episode UPDATE：非法业务值令非空字段校验失败，不静默截断。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_handoff_episode_update_valid;

CREATE TRIGGER trg_cs_handoff_episode_update_valid BEFORE
UPDATE ON cs_handoff_episode FOR EACH ROW
SET
    NEW.status = IF(
        NEW.status IN (
            'REQUESTED',
            'CLAIMED',
            'ACTIVE',
            'RESOLVED',
            'CANCELLED'
        ),
        NEW.status,
        NULL
    );

-- cs_handoff_cancellation INSERT：非法业务值令非空字段校验失败，不静默截断。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_handoff_cancellation_insert_valid;

CREATE TRIGGER trg_cs_handoff_cancellation_insert_valid BEFORE
INSERT
    ON cs_handoff_cancellation FOR EACH ROW
SET
    NEW.status = IF(
        NEW.status IN ('PENDING_OWNER_ACK', 'CONFIRMED', 'REJECTED'),
        NEW.status,
        NULL
    );

-- cs_handoff_cancellation UPDATE：非法业务值令非空字段校验失败，不静默截断。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_handoff_cancellation_update_valid;

CREATE TRIGGER trg_cs_handoff_cancellation_update_valid BEFORE
UPDATE ON cs_handoff_cancellation FOR EACH ROW
SET
    NEW.status = IF(
        NEW.status IN ('PENDING_OWNER_ACK', 'CONFIRMED', 'REJECTED'),
        NEW.status,
        NULL
    );

-- cs_reception_command INSERT：非法业务值令非空字段校验失败，不静默截断。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_reception_command_insert_valid;

CREATE TRIGGER trg_cs_reception_command_insert_valid BEFORE
INSERT
    ON cs_reception_command FOR EACH ROW
SET
    NEW.result_version = IF(
        NEW.action IN ('CLAIM', 'RESOLVE', 'CANCEL')
        AND NEW.result_status IN ('CLAIMED', 'RESOLVED', 'CANCELLED')
        AND NEW.result_version > 0,
        NEW.result_version,
        NULL
    );

-- cs_reception_command UPDATE：非法业务值令非空字段校验失败，不静默截断。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_reception_command_update_valid;

CREATE TRIGGER trg_cs_reception_command_update_valid BEFORE
UPDATE ON cs_reception_command FOR EACH ROW
SET
    NEW.result_version = IF(
        NEW.action IN ('CLAIM', 'RESOLVE', 'CANCEL')
        AND NEW.result_status IN ('CLAIMED', 'RESOLVED', 'CANCELLED')
        AND NEW.result_version > 0,
        NEW.result_version,
        NULL
    );

-- ai_human_message INSERT：非法业务值令非空字段校验失败，不静默截断。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_ai_human_message_insert_valid;

CREATE TRIGGER trg_ai_human_message_insert_valid BEFORE
INSERT
    ON ai_human_message FOR EACH ROW
SET
    NEW.seq = IF(
        NEW.sender_role IN ('VISITOR', 'AGENT')
        AND NEW.seq > 0
        AND CHAR_LENGTH(TRIM(NEW.content)) BETWEEN 1 AND 4000,
        NEW.seq,
        NULL
    );

-- ai_human_message UPDATE：非法业务值令非空字段校验失败，不静默截断。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_ai_human_message_update_valid;

CREATE TRIGGER trg_ai_human_message_update_valid BEFORE
UPDATE ON ai_human_message FOR EACH ROW
SET
    NEW.seq = IF(
        NEW.sender_role IN ('VISITOR', 'AGENT')
        AND NEW.seq > 0
        AND CHAR_LENGTH(TRIM(NEW.content)) BETWEEN 1 AND 4000,
        NEW.seq,
        NULL
    );

-- ai_human_receipt INSERT：非法业务值令非空字段校验失败，不静默截断。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_ai_human_receipt_insert_valid;

CREATE TRIGGER trg_ai_human_receipt_insert_valid BEFORE
INSERT
    ON ai_human_receipt FOR EACH ROW
SET
    NEW.read_seq = IF(
        NEW.read_seq >= 0
        AND NEW.delivered_seq >= NEW.read_seq,
        NEW.read_seq,
        NULL
    );

-- ai_human_receipt UPDATE：非法业务值令非空字段校验失败，不静默截断。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_ai_human_receipt_update_valid;

CREATE TRIGGER trg_ai_human_receipt_update_valid BEFORE
UPDATE ON ai_human_receipt FOR EACH ROW
SET
    NEW.read_seq = IF(
        NEW.read_seq >= 0
        AND NEW.delivered_seq >= NEW.read_seq,
        NEW.read_seq,
        NULL
    );

-- 合并来源文件结束：data/sql/mysql/ai_20261109_customer_service_reception.sql
-- 合并来源文件：data/sql/mysql/ai_20261109_customer_service_followup.sql
-- 目的：客服等待/邮件跟进与可靠通知增量；方言 MySQL 5.7+。
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
    remind_at DATETIME(3) NOT NULL,
    -- UTC T+10 访客选择期限
    busy_at DATETIME(3) NOT NULL,
    -- 申请时日历的营业结束 UTC 期限
    closes_at DATETIME(3) NOT NULL,
    -- 下次营业 UTC 提示
    next_opens_at DATETIME(3) NOT NULL,
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
    -- 邮件跟进必须具有校验后的联系方式与唯一工单引用，5.7 由下方触发器兜底。
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
    next_attempt_at DATETIME(3) NOT NULL,
    -- 投递租约随机标识
    lease_id VARCHAR(64),
    -- UTC 租约期限，崩溃可接管
    lease_until DATETIME(3),
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
    -- 已领取任务必须持有可过期接管的完整租约，5.7 由下方触发器兜底。
    CONSTRAINT ck_cs_delivery_lease CHECK (
        status <> 'CLAIMED'
        OR (
            lease_id IS NOT NULL
            AND lease_until IS NOT NULL
        )
    )
);

-- 仅服务于到期投递扫描，不改变业务唯一性。
-- 当前库已存在该对象时跳过；缺失才补建，不删除或重写业务记录。
SET
    @cs_migration_ddl = IF(
        EXISTS (
            SELECT
                1
            FROM
                information_schema.statistics
            WHERE
                table_schema = DATABASE()
                AND table_name = 'cs_followup_delivery'
                AND index_name = 'ix_cs_delivery_due'
        ),
        'SELECT 1',
        'CREATE INDEX ix_cs_delivery_due ON cs_followup_delivery(status,next_attempt_at,lease_until)'
    );

PREPARE cs_migration_statement
FROM
    @cs_migration_ddl;

EXECUTE cs_migration_statement;

DEALLOCATE PREPARE cs_migration_statement;

-- 待推进状态索引，避免重复扫描已接单/结束工单。
-- 当前库已存在该对象时跳过；缺失才补建，不删除或重写业务记录。
SET
    @cs_migration_ddl = IF(
        EXISTS (
            SELECT
                1
            FROM
                information_schema.statistics
            WHERE
                table_schema = DATABASE()
                AND table_name = 'cs_waiting_workflow'
                AND index_name = 'ix_cs_waiting_due'
        ),
        'SELECT 1',
        'CREATE INDEX ix_cs_waiting_due ON cs_waiting_workflow(state,closes_at,busy_at)'
    );

PREPARE cs_migration_statement
FROM
    @cs_migration_ddl;

EXECUTE cs_migration_statement;

DEALLOCATE PREPARE cs_migration_statement;

-- MySQL 5.7 值域兜底：CHECK 不执行，非法状态/版本令必填 state 为空并由 NOT NULL 拒绝。
-- 不代替应用状态机；INSERT/UPDATE 均检查，不能借更新绕过版本和跟进引用约束。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_waiting_insert;

CREATE TRIGGER trg_cs_waiting_insert BEFORE
INSERT
    ON cs_waiting_workflow FOR EACH ROW
SET
    NEW.state = IF(
        NEW.state IN (
            'WAITING',
            'BUSY',
            'CONTINUING',
            'NEED_EMAIL',
            'FOLLOW_UP',
            'ACCEPTED',
            'CANCELLED'
        )
        AND NEW.version > 0
        AND (
            NEW.state <> 'FOLLOW_UP'
            OR (
                NEW.email IS NOT NULL
                AND NEW.ticket_id IS NOT NULL
            )
        ),
        NEW.state,
        NULL
    );

-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_waiting_update;

CREATE TRIGGER trg_cs_waiting_update BEFORE
UPDATE ON cs_waiting_workflow FOR EACH ROW
SET
    NEW.state = IF(
        NEW.state IN (
            'WAITING',
            'BUSY',
            'CONTINUING',
            'NEED_EMAIL',
            'FOLLOW_UP',
            'ACCEPTED',
            'CANCELLED'
        )
        AND NEW.version > 0
        AND (
            NEW.state <> 'FOLLOW_UP'
            OR (
                NEW.email IS NOT NULL
                AND NEW.ticket_id IS NOT NULL
            )
        ),
        NEW.state,
        NULL
    );

-- 通知值域和租约完整性兜底；SENT 只代表网关受理，CLAIMED 必须有可恢复租约。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_delivery_insert;

CREATE TRIGGER trg_cs_delivery_insert BEFORE
INSERT
    ON cs_followup_delivery FOR EACH ROW
SET
    NEW.status = IF(
        NEW.kind IN ('INTERNAL', 'EMAIL')
        AND NEW.status IN ('PENDING', 'CLAIMED', 'SENT', 'DEAD')
        AND NEW.attempts >= 0
        AND (
            NEW.status <> 'CLAIMED'
            OR (
                NEW.lease_id IS NOT NULL
                AND NEW.lease_until IS NOT NULL
            )
        ),
        NEW.status,
        NULL
    );

-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_delivery_update;

CREATE TRIGGER trg_cs_delivery_update BEFORE
UPDATE ON cs_followup_delivery FOR EACH ROW
SET
    NEW.status = IF(
        NEW.kind IN ('INTERNAL', 'EMAIL')
        AND NEW.status IN ('PENDING', 'CLAIMED', 'SENT', 'DEAD')
        AND NEW.attempts >= 0
        AND (
            NEW.status <> 'CLAIMED'
            OR (
                NEW.lease_id IS NOT NULL
                AND NEW.lease_until IS NOT NULL
            )
        ),
        NEW.status,
        NULL
    );

-- 合并来源文件结束：data/sql/mysql/ai_20261109_customer_service_followup.sql
-- 合并来源文件：data/sql/mysql/ai_20261109_customer_service_survey_runtime.sql
-- 目的：记录新会话调查历史起点，并允许用户明确同意后的调查转人工来源；MySQL 5.7+。
-- 前提：先执行 customer_service_and_resolution 和 reception；部署前备份，脚本只执行一次。
-- owner：Conversation 创建时写起点，客服 owner 维护接待周期；开发阶段不兼容旧外部接待历史。
-- 起点与会话审计同生命周期，不逻辑删除；删除/回滚前先关闭调查并按 owner 要求归档。
-- 先检查无 SURVEY_DISSATISFIED_OPT_IN 数据，再恢复旧约束；MySQL DDL 可能逐条提交。
CREATE TABLE IF NOT EXISTS ai_survey_conversation_origin (
    tenant_id VARCHAR(64) NOT NULL COMMENT '可信租户归属',
    top_org_id VARCHAR(64) NOT NULL COMMENT '可信顶级组织归属',
    conversation_id VARCHAR(128) NOT NULL COMMENT '新建会话引用，不为旧会话回填',
    assistant_config_id VARCHAR(64) NOT NULL COMMENT '首次绑定的智能体配置',
    bvent_id VARCHAR(64) NULL COMMENT '可信大会引用',
    registered_at DATETIME(6) NOT NULL COMMENT '新会话起点登记 UTC 时间',
    PRIMARY KEY (tenant_id, top_org_id, conversation_id)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COMMENT = 'Conversation owner 的调查资格起点；本项目接待 owner 的历史覆盖完整';

-- 客服 owner 保存调查只读引用及来源轮次；仅调查应用服务可写入，随接待审计保留。
-- 当前库已存在该对象时跳过；缺失才补建，不删除或重写业务记录。
SET
    @cs_migration_ddl = IF(
        EXISTS (
            SELECT
                1
            FROM
                information_schema.columns
            WHERE
                table_schema = DATABASE()
                AND table_name = 'cs_handoff_episode'
                AND column_name = 'survey_id'
        ),
        'SELECT 1',
        'ALTER TABLE cs_handoff_episode ADD COLUMN survey_id VARCHAR(64) NULL COMMENT ''不满意明确同意人工的调查引用，其他来源为空'''
    );

PREPARE cs_migration_statement
FROM
    @cs_migration_ddl;

EXECUTE cs_migration_statement;

DEALLOCATE PREPARE cs_migration_statement;

-- 同租户组织的一份调查最多创建一个接待周期，NULL 不限制其他来源。
-- 当前库已存在该对象时跳过；缺失才补建，不删除或重写业务记录。
SET
    @cs_migration_ddl = IF(
        EXISTS (
            SELECT
                1
            FROM
                information_schema.statistics
            WHERE
                table_schema = DATABASE()
                AND table_name = 'cs_handoff_episode'
                AND index_name = 'uq_cs_episode_survey'
        ),
        'SELECT 1',
        'CREATE UNIQUE INDEX uq_cs_episode_survey ON cs_handoff_episode(tenant_id,top_org_id,survey_id)'
    );

PREPARE cs_migration_statement
FROM
    @cs_migration_ddl;

EXECUTE cs_migration_statement;

DEALLOCATE PREPARE cs_migration_statement;

-- MySQL 5.7 不执行 CHECK；8.0 需要显式扩展已有约束，不能影响其他来源的引用规则。
SET
    @survey_ddl = IF(
        CAST(SUBSTRING_INDEX(VERSION(), '.', 1) AS UNSIGNED) >= 8
        AND EXISTS (
            SELECT
                1
            FROM
                information_schema.table_constraints
            WHERE
                constraint_schema = DATABASE()
                AND table_name = 'cs_handoff_episode'
                AND constraint_name = 'ck_cs_handoff_episode_source'
                AND constraint_type = 'CHECK'
        ),
        'ALTER TABLE cs_handoff_episode DROP CHECK ck_cs_handoff_episode_source',
        'SELECT 1'
    );

PREPARE survey_statement
FROM
    @survey_ddl;

EXECUTE survey_statement;

DEALLOCATE PREPARE survey_statement;

SET
    @survey_ddl = IF(
        CAST(SUBSTRING_INDEX(VERSION(), '.', 1) AS UNSIGNED) >= 8,
        'ALTER TABLE cs_handoff_episode ADD CONSTRAINT ck_cs_handoff_episode_source CHECK (trigger_source IN (''AI_RECOMMENDATION_CONFIRMED'',''VISITOR_DIRECT_REQUEST'',''SURVEY_DISSATISFIED_OPT_IN'',''SYSTEM_POLICY''))',
        'SELECT 1'
    );

PREPARE survey_statement
FROM
    @survey_ddl;

EXECUTE survey_statement;

DEALLOCATE PREPARE survey_statement;

SET
    @survey_ddl = IF(
        CAST(SUBSTRING_INDEX(VERSION(), '.', 1) AS UNSIGNED) >= 8
        AND EXISTS (
            SELECT
                1
            FROM
                information_schema.table_constraints
            WHERE
                constraint_schema = DATABASE()
                AND table_name = 'cs_handoff_episode'
                AND constraint_name = 'ck_cs_handoff_episode_source_ref'
                AND constraint_type = 'CHECK'
        ),
        'ALTER TABLE cs_handoff_episode DROP CHECK ck_cs_handoff_episode_source_ref',
        'SELECT 1'
    );

PREPARE survey_statement
FROM
    @survey_ddl;

EXECUTE survey_statement;

DEALLOCATE PREPARE survey_statement;

SET
    @survey_ddl = IF(
        CAST(SUBSTRING_INDEX(VERSION(), '.', 1) AS UNSIGNED) >= 8,
        'ALTER TABLE cs_handoff_episode ADD CONSTRAINT ck_cs_handoff_episode_source_ref CHECK ((trigger_source = ''AI_RECOMMENDATION_CONFIRMED'' AND proposal_id IS NOT NULL AND turn_id IS NOT NULL AND survey_id IS NULL) OR (trigger_source = ''VISITOR_DIRECT_REQUEST'' AND proposal_id IS NULL AND turn_id IS NULL AND survey_id IS NULL AND reason_code = ''USER_REQUESTED'') OR (trigger_source = ''SURVEY_DISSATISFIED_OPT_IN'' AND proposal_id IS NULL AND turn_id IS NOT NULL AND survey_id IS NOT NULL AND reason_code = ''USER_DISSATISFIED'') OR (trigger_source = ''SYSTEM_POLICY'' AND proposal_id IS NULL AND turn_id IS NOT NULL AND survey_id IS NULL AND reason_code IN (''SYSTEM_FAILURE'',''SAFETY_OR_URGENT'')))',
        'SELECT 1'
    );

PREPARE survey_statement
FROM
    @survey_ddl;

EXECUTE survey_statement;

DEALLOCATE PREPARE survey_statement;

-- MySQL 5.7 的 CHECK 不执行；独立触发器同时校验来源、引用及原因，不改写合法业务状态。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_episode_survey_insert;

CREATE TRIGGER trg_cs_episode_survey_insert BEFORE
INSERT
    ON cs_handoff_episode FOR EACH ROW
SET
    NEW.trigger_source = IF(
        (
            NEW.trigger_source = 'AI_RECOMMENDATION_CONFIRMED'
            AND NEW.proposal_id IS NOT NULL
            AND NEW.turn_id IS NOT NULL
            AND NEW.survey_id IS NULL
            AND NEW.reason_code IN (
                'UNRESOLVED_AFTER_RETRY',
                'USER_DISSATISFIED',
                'REQUIRES_HUMAN_JUDGMENT'
            )
        )
        OR (
            NEW.trigger_source = 'VISITOR_DIRECT_REQUEST'
            AND NEW.proposal_id IS NULL
            AND NEW.turn_id IS NULL
            AND NEW.survey_id IS NULL
            AND NEW.reason_code = 'USER_REQUESTED'
        )
        OR (
            NEW.trigger_source = 'SURVEY_DISSATISFIED_OPT_IN'
            AND NEW.proposal_id IS NULL
            AND NEW.turn_id IS NOT NULL
            AND NEW.survey_id IS NOT NULL
            AND NEW.reason_code = 'USER_DISSATISFIED'
        )
        OR (
            NEW.trigger_source = 'SYSTEM_POLICY'
            AND NEW.proposal_id IS NULL
            AND NEW.turn_id IS NOT NULL
            AND NEW.survey_id IS NULL
            AND NEW.reason_code IN ('SYSTEM_FAILURE', 'SAFETY_OR_URGENT')
        ),
        NEW.trigger_source,
        NULL
    );

-- 更新必须维持相同来源引用不变量，不能把直接申请伪装为调查申请。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_episode_survey_update;

CREATE TRIGGER trg_cs_episode_survey_update BEFORE
UPDATE ON cs_handoff_episode FOR EACH ROW
SET
    NEW.trigger_source = IF(
        (
            NEW.trigger_source = 'AI_RECOMMENDATION_CONFIRMED'
            AND NEW.proposal_id IS NOT NULL
            AND NEW.turn_id IS NOT NULL
            AND NEW.survey_id IS NULL
            AND NEW.reason_code IN (
                'UNRESOLVED_AFTER_RETRY',
                'USER_DISSATISFIED',
                'REQUIRES_HUMAN_JUDGMENT'
            )
        )
        OR (
            NEW.trigger_source = 'VISITOR_DIRECT_REQUEST'
            AND NEW.proposal_id IS NULL
            AND NEW.turn_id IS NULL
            AND NEW.survey_id IS NULL
            AND NEW.reason_code = 'USER_REQUESTED'
        )
        OR (
            NEW.trigger_source = 'SURVEY_DISSATISFIED_OPT_IN'
            AND NEW.proposal_id IS NULL
            AND NEW.turn_id IS NOT NULL
            AND NEW.survey_id IS NOT NULL
            AND NEW.reason_code = 'USER_DISSATISFIED'
        )
        OR (
            NEW.trigger_source = 'SYSTEM_POLICY'
            AND NEW.proposal_id IS NULL
            AND NEW.turn_id IS NOT NULL
            AND NEW.survey_id IS NULL
            AND NEW.reason_code IN ('SYSTEM_FAILURE', 'SAFETY_OR_URGENT')
        ),
        NEW.trigger_source,
        NULL
    );

-- 合并来源文件结束：data/sql/mysql/ai_20261109_customer_service_survey_runtime.sql
-- 合并来源文件：data/sql/mysql/ai_20261109_customer_service_followup_response.sql
-- 目的：工单最终邮件回复、可靠投递与显式结案；方言 MySQL 5.7+，一次执行，不保留旧协议兼容。
-- 前提：customer_service_followup 已部署；执行前保存结构快照，不删除或回填原业务数据。
-- owner：客服上下文唯一写入；客服 API 写正文，Worker 推进受理状态；邮箱只从原 workflow 查询。
-- 生命周期：不可变最终回复及关闭记录按客服审计保留，无逻辑删除或级联删除；关闭不代表送达/已读。
-- 回滚：先关闭 followup-response API/Worker 与通知，再导出正文和未投递任务，按 owner 审核移除新对象。
-- 工单引用连同完整组织/会话/周期成为外键目标，禁止回复绑定其他工单。
-- 当前库已存在该对象时跳过；缺失才补建，不删除或重写业务记录。
SET
    @cs_migration_ddl = IF(
        EXISTS (
            SELECT
                1
            FROM
                information_schema.statistics
            WHERE
                table_schema = DATABASE()
                AND table_name = 'cs_waiting_workflow'
                AND index_name = 'uq_cs_waiting_ticket_scope'
        ),
        'SELECT 1',
        'ALTER TABLE cs_waiting_workflow ADD CONSTRAINT uq_cs_waiting_ticket_scope UNIQUE(tenant_id,top_org_id,conversation_id,episode_id,ticket_id)'
    );

PREPARE cs_migration_statement
FROM
    @cs_migration_ddl;

EXECUTE cs_migration_statement;

DEALLOCATE PREPARE cs_migration_statement;

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
    next_attempt_at DATETIME(3) NOT NULL,
    -- 当前Worker随机租约，仅CLAIMED必填。
    lease_id VARCHAR(64),
    -- UTC租约到期，默认30秒，崩溃可接管。
    lease_until DATETIME(3),
    -- 首次显式结案请求键，仅CLOSED必填，重试沿用。
    close_request_id VARCHAR(100),
    -- 首次回复HTTP追踪标识，Worker与网关沿用；必填，不含凭据。
    trace_id VARCHAR(128) NOT NULL,
    -- 首次结案的授权客服账号，仅CLOSED必填，不冒用首次回复者。
    close_actor_id VARCHAR(128),
    -- 首次提交UTC时间，不因重试改写；必填。
    created_at DATETIME(3) NOT NULL,
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
-- 当前库已存在该对象时跳过；缺失才补建，不删除或重写业务记录。
SET
    @cs_migration_ddl = IF(
        EXISTS (
            SELECT
                1
            FROM
                information_schema.statistics
            WHERE
                table_schema = DATABASE()
                AND table_name = 'cs_followup_response'
                AND index_name = 'ix_cs_response_due'
        ),
        'SELECT 1',
        'CREATE INDEX ix_cs_response_due ON cs_followup_response(state,next_attempt_at,lease_until)'
    );

PREPARE cs_migration_statement
FROM
    @cs_migration_ddl;

EXECUTE cs_migration_statement;

DEALLOCATE PREPARE cs_migration_statement;

-- MySQL 5.7不执行CHECK，以NOT NULL触发器兜底值域和租约/结案完整性，不能代替应用状态机。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_response_insert;

CREATE TRIGGER trg_cs_response_insert BEFORE
INSERT
    ON cs_followup_response FOR EACH ROW
SET
    NEW.state = IF(
        NEW.state IN ('PENDING', 'CLAIMED', 'SENT', 'DEAD', 'CLOSED')
        AND NEW.version > 0
        AND NEW.attempts >= 0
        AND (
            NEW.state <> 'CLAIMED'
            OR (
                NEW.lease_id IS NOT NULL
                AND NEW.lease_until IS NOT NULL
            )
        )
        AND (
            (
                NEW.state = 'CLOSED'
                AND NEW.close_request_id IS NOT NULL
                AND NEW.close_actor_id IS NOT NULL
            )
            OR (
                NEW.state <> 'CLOSED'
                AND NEW.close_request_id IS NULL
                AND NEW.close_actor_id IS NULL
            )
        ),
        NEW.state,
        NULL
    );

-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_response_update;

CREATE TRIGGER trg_cs_response_update BEFORE
UPDATE ON cs_followup_response FOR EACH ROW
SET
    NEW.state = IF(
        NEW.state IN ('PENDING', 'CLAIMED', 'SENT', 'DEAD', 'CLOSED')
        AND NEW.version > 0
        AND NEW.attempts >= 0
        AND (
            NEW.state <> 'CLAIMED'
            OR (
                NEW.lease_id IS NOT NULL
                AND NEW.lease_until IS NOT NULL
            )
        )
        AND (
            NEW.state <> 'CLOSED'
            OR NEW.close_request_id IS NOT NULL
        ),
        NEW.state,
        NULL
    );

-- 合并来源文件结束：data/sql/mysql/ai_20261109_customer_service_followup_response.sql
-- 合并来源文件：data/sql/mysql/ai_20261109_customer_service_availability.sql
-- 目的：客服在线租约、组织级接待容量锁及不可变改派审计；账号/组仍由用户中心唯一管理。
-- 前提：已执行基线及reception增量；MySQL5.7+/InnoDB，单次执行，先备份结构。DDL自动提交，失败后定位续跑。
-- Owner：Customer Service；仅已授权客服心跳/接单/改派编排写入，接待服务与指标只读。
-- 生命周期：运行行保留最后选择，45秒无心跳投影为OFFLINE；审计随会话留存，不逻辑删除、不自动清记录。
-- 隔离：所有键包含tenant/topOrg；改派复合外键绑定原会话周期，防止跨组织挂接。
-- 回滚：先关闭availability.enabled并停改派入口，导出新增审计后人工决定保留；开发阶段不保留旧协议。
CREATE TABLE IF NOT EXISTS cs_agent_availability (
    tenant_id VARCHAR(64) NOT NULL COMMENT '认证租户，非客户端可覆盖',
    top_org_id VARCHAR(64) NOT NULL COMMENT '经用户中心启用校验的组织',
    agent_id VARCHAR(128) NOT NULL COMMENT '外部账号稳定引用，无账号资料主写',
    status VARCHAR(16) NOT NULL COMMENT 'ONLINE接新单/AWAY暂离/OFFLINE离线；均不结束已有接待',
    expires_at DATETIME(6) NOT NULL COMMENT '服务器计算的45秒UTC租约，过期按离线',
    updated_at DATETIME(6) NOT NULL COMMENT '最近明确选择或心跳UTC时间',
    PRIMARY KEY (tenant_id, top_org_id, agent_id),
    CONSTRAINT ck_cs_availability_status CHECK (status IN ('ONLINE', 'AWAY', 'OFFLINE'))
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COMMENT = '客服运行状态及跨助手容量串行锁；主键保证组织内一客服一行';

-- 容量按组织所有助手/组有效claim计数；此索引避免心跳及接单扫描其他组织。
-- 当前库已存在该对象时跳过；缺失才补建，不删除或重写业务记录。
SET
    @cs_migration_ddl = IF(
        EXISTS (
            SELECT
                1
            FROM
                information_schema.statistics
            WHERE
                table_schema = DATABASE()
                AND table_name = 'cs_handoff_claim'
                AND index_name = 'ix_cs_claim_agent_capacity'
        ),
        'SELECT 1',
        'CREATE INDEX ix_cs_claim_agent_capacity ON cs_handoff_claim(tenant_id,top_org_id,agent_id)'
    );

PREPARE cs_migration_statement
FROM
    @cs_migration_ddl;

EXECUTE cs_migration_statement;

DEALLOCATE PREPARE cs_migration_statement;

CREATE TABLE IF NOT EXISTS cs_reception_transfer (
    tenant_id VARCHAR(64) NOT NULL COMMENT '认证租户',
    top_org_id VARCHAR(64) NOT NULL COMMENT '可信组织',
    conversation_id VARCHAR(128) NOT NULL COMMENT '原会话引用，改派不创建新访客意图',
    episode_id VARCHAR(64) NOT NULL COMMENT '原人工周期，保持HUMAN owner',
    request_id VARCHAR(128) NOT NULL COMMENT '客户端稳定幂等键，同会话仅一次首次结果',
    actor_id VARCHAR(128) NOT NULL COMMENT '正式认证的改派操作者，必须等于当时认领人',
    from_agent_id VARCHAR(128) NOT NULL COMMENT '首次原认领客服稳定引用',
    to_agent_id VARCHAR(128) NOT NULL COMMENT '实时授权且在线有容量的同组目标',
    group_id VARCHAR(64) NOT NULL COMMENT '服务端assistant路由组，禁止任意跨组',
    fingerprint VARCHAR(64) NOT NULL COMMENT '主体/目标/周期/版本SHA256摘要，异参不可覆盖',
    result_version BIGINT NOT NULL COMMENT '改派后的聚合版本，大于1',
    trace_id VARCHAR(128) NOT NULL COMMENT '首次入口trace，重试不重写',
    created_at DATETIME(6) NOT NULL COMMENT '原子改派UTC时间',
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
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COMMENT = '不可变客服改派事实；主键防重复命令，外键防跨组织，所有事实必填';

-- MySQL5.7值域兜底：CHECK不执行时通过非空约束拒绝非法状态/改派事实；8.x同义。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_availability_insert_valid;

CREATE TRIGGER trg_cs_availability_insert_valid BEFORE
INSERT
    ON cs_agent_availability FOR EACH ROW
SET
    NEW.status = IF(
        NEW.status IN ('ONLINE', 'AWAY', 'OFFLINE'),
        NEW.status,
        NULL
    );

-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_availability_update_valid;

CREATE TRIGGER trg_cs_availability_update_valid BEFORE
UPDATE ON cs_agent_availability FOR EACH ROW
SET
    NEW.status = IF(
        NEW.status IN ('ONLINE', 'AWAY', 'OFFLINE'),
        NEW.status,
        NULL
    );

-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_transfer_insert_valid;

CREATE TRIGGER trg_cs_transfer_insert_valid BEFORE
INSERT
    ON cs_reception_transfer FOR EACH ROW
SET
    NEW.result_version = IF(
        NEW.result_version > 1
        AND NEW.actor_id = NEW.from_agent_id
        AND NEW.from_agent_id <> NEW.to_agent_id,
        NEW.result_version,
        NULL
    );

-- 审计不允许修改；重试只读取首次回执。通过必填版本拒绝UPDATE。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_transfer_immutable;

CREATE TRIGGER trg_cs_transfer_immutable BEFORE
UPDATE ON cs_reception_transfer FOR EACH ROW
SET
    NEW.result_version = NULL;

-- 合并来源文件结束：data/sql/mysql/ai_20261109_customer_service_availability.sql
-- 合并来源文件：data/sql/mysql/ai_20261109_customer_service_availability_lease.sql
-- 目的：为在线选择增加服务端代次，防止其他标签页的旧心跳覆盖明确下线/暂离。
-- 前提：已执行availability增量；MySQL5.7+/InnoDB；单次执行，DDL自动提交，先保存结构快照。
-- Owner：Customer Service唯一写；已授权客服明确选择递增，心跳只读代次并比较续约。
-- 隔离/生命周期：沿用tenant/topOrg/agent复合主键，运行行不删除；现有行从0开始，须明确重新上线。
-- 回滚：先关闭availability.enabled、停止心跳入口并回退应用；保留代次列便于审计，不重跑既有迁移。
-- 当前库已存在该对象时跳过；缺失才补建，不删除或重写业务记录。
SET
    @cs_migration_ddl = IF(
        EXISTS (
            SELECT
                1
            FROM
                information_schema.columns
            WHERE
                table_schema = DATABASE()
                AND table_name = 'cs_agent_availability'
                AND column_name = 'version'
        ),
        'SELECT 1',
        'ALTER TABLE cs_agent_availability ADD COLUMN version BIGINT NOT NULL DEFAULT 0 COMMENT ''明确状态选择递增代次；0表示尚未新协议上线，心跳不得改变该值'''
    );

PREPARE cs_migration_statement
FROM
    @cs_migration_ddl;

EXECUTE cs_migration_statement;

DEALLOCATE PREPARE cs_migration_statement;

-- 非空和非负约束拒绝无效代次；5.7使用同义触发器弥补CHECK不执行。
-- 当前库已存在该对象时跳过；缺失才补建，不删除或重写业务记录。
SET
    @cs_migration_ddl = IF(
        CAST(SUBSTRING_INDEX(VERSION(), '.', 1) AS UNSIGNED) < 8
        OR EXISTS (
            SELECT
                1
            FROM
                information_schema.table_constraints
            WHERE
                constraint_schema = DATABASE()
                AND table_name = 'cs_agent_availability'
                AND constraint_name = 'ck_cs_availability_version'
                AND constraint_type = 'CHECK'
        ),
        'SELECT 1',
        'ALTER TABLE cs_agent_availability ADD CONSTRAINT ck_cs_availability_version CHECK(version>=0)'
    );

PREPARE cs_migration_statement
FROM
    @cs_migration_ddl;

EXECUTE cs_migration_statement;

DEALLOCATE PREPARE cs_migration_statement;

-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_availability_version_insert;

CREATE TRIGGER trg_cs_availability_version_insert BEFORE
INSERT
    ON cs_agent_availability FOR EACH ROW
SET
    NEW.version = IF(NEW.version >= 0, NEW.version, NULL);

-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_availability_version_update;

CREATE TRIGGER trg_cs_availability_version_update BEFORE
UPDATE ON cs_agent_availability FOR EACH ROW
SET
    NEW.version = IF(NEW.version >= 0, NEW.version, NULL);

-- 合并来源文件结束：data/sql/mysql/ai_20261109_customer_service_availability_lease.sql
-- 合并来源文件：data/sql/mysql/ai_20261109_customer_service_system.sql
-- 目的：可信系统故障/安全护栏可靠转人工；保留来源轮次/epoch，旧代次失败不得覆盖新回复权。
-- 前提：已部署基线、reception、survey-runtime；单次执行并先备份。方言：MySQL5.7+/InnoDB，DDL逐条提交。
-- Owner：Customer Service；可信服务端写任务、Worker写申请/Outbox/HUMAN同事务；无HTTP/Tool创建入口。
-- 生命周期：PENDING待消费/COMPLETED已原子创建/SUPERSEDED旧代次失效/DEAD五次失败；死信人工重试，随会话留存不逻辑删除。
-- 隔离：tenant/topOrg主键与复合generation外键；主体/路由/trace来自服务端不可变快照，全部必填。
-- 回滚：先关闭system-handoff.enabled，导出任务与系统来源episode；存在系统来源事实时不得恢复旧约束。不删记录。
DROP TRIGGER IF EXISTS trg_cs_episode_survey_insert;

DROP TRIGGER IF EXISTS trg_cs_episode_survey_update;

-- MySQL 5.7 不执行 CHECK；8.0 需要显式扩展已有约束，不能影响其他来源的引用规则。
SET
    @survey_ddl = IF(
        CAST(SUBSTRING_INDEX(VERSION(), '.', 1) AS UNSIGNED) >= 8
        AND EXISTS (
            SELECT
                1
            FROM
                information_schema.table_constraints
            WHERE
                constraint_schema = DATABASE()
                AND table_name = 'cs_handoff_episode'
                AND constraint_name = 'ck_cs_handoff_episode_source'
                AND constraint_type = 'CHECK'
        ),
        'ALTER TABLE cs_handoff_episode DROP CHECK ck_cs_handoff_episode_source',
        'SELECT 1'
    );

PREPARE survey_statement
FROM
    @survey_ddl;

EXECUTE survey_statement;

DEALLOCATE PREPARE survey_statement;

SET
    @survey_ddl = IF(
        CAST(SUBSTRING_INDEX(VERSION(), '.', 1) AS UNSIGNED) >= 8,
        'ALTER TABLE cs_handoff_episode ADD CONSTRAINT ck_cs_handoff_episode_source CHECK (trigger_source IN (''AI_RECOMMENDATION_CONFIRMED'',''VISITOR_DIRECT_REQUEST'',''SURVEY_DISSATISFIED_OPT_IN'',''SYSTEM_POLICY''))',
        'SELECT 1'
    );

PREPARE survey_statement
FROM
    @survey_ddl;

EXECUTE survey_statement;

DEALLOCATE PREPARE survey_statement;

SET
    @survey_ddl = IF(
        CAST(SUBSTRING_INDEX(VERSION(), '.', 1) AS UNSIGNED) >= 8
        AND EXISTS (
            SELECT
                1
            FROM
                information_schema.table_constraints
            WHERE
                constraint_schema = DATABASE()
                AND table_name = 'cs_handoff_episode'
                AND constraint_name = 'ck_cs_handoff_episode_source_ref'
                AND constraint_type = 'CHECK'
        ),
        'ALTER TABLE cs_handoff_episode DROP CHECK ck_cs_handoff_episode_source_ref',
        'SELECT 1'
    );

PREPARE survey_statement
FROM
    @survey_ddl;

EXECUTE survey_statement;

DEALLOCATE PREPARE survey_statement;

SET
    @survey_ddl = IF(
        CAST(SUBSTRING_INDEX(VERSION(), '.', 1) AS UNSIGNED) >= 8,
        'ALTER TABLE cs_handoff_episode ADD CONSTRAINT ck_cs_handoff_episode_source_ref CHECK ((trigger_source = ''AI_RECOMMENDATION_CONFIRMED'' AND proposal_id IS NOT NULL AND turn_id IS NOT NULL AND survey_id IS NULL) OR (trigger_source = ''VISITOR_DIRECT_REQUEST'' AND proposal_id IS NULL AND turn_id IS NULL AND survey_id IS NULL AND reason_code = ''USER_REQUESTED'') OR (trigger_source = ''SURVEY_DISSATISFIED_OPT_IN'' AND proposal_id IS NULL AND turn_id IS NOT NULL AND survey_id IS NOT NULL AND reason_code = ''USER_DISSATISFIED'') OR (trigger_source = ''SYSTEM_POLICY'' AND proposal_id IS NULL AND turn_id IS NOT NULL AND survey_id IS NULL AND reason_code IN (''SYSTEM_FAILURE'',''SAFETY_OR_URGENT'')))',
        'SELECT 1'
    );

PREPARE survey_statement
FROM
    @survey_ddl;

EXECUTE survey_statement;

DEALLOCATE PREPARE survey_statement;

-- MySQL 5.7 的 CHECK 不执行；独立触发器同时校验来源、引用及原因，不改写合法业务状态。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_episode_survey_insert;

CREATE TRIGGER trg_cs_episode_survey_insert BEFORE
INSERT
    ON cs_handoff_episode FOR EACH ROW
SET
    NEW.trigger_source = IF(
        (
            NEW.trigger_source = 'AI_RECOMMENDATION_CONFIRMED'
            AND NEW.proposal_id IS NOT NULL
            AND NEW.turn_id IS NOT NULL
            AND NEW.survey_id IS NULL
            AND NEW.reason_code IN (
                'UNRESOLVED_AFTER_RETRY',
                'USER_DISSATISFIED',
                'REQUIRES_HUMAN_JUDGMENT'
            )
        )
        OR (
            NEW.trigger_source = 'VISITOR_DIRECT_REQUEST'
            AND NEW.proposal_id IS NULL
            AND NEW.turn_id IS NULL
            AND NEW.survey_id IS NULL
            AND NEW.reason_code = 'USER_REQUESTED'
        )
        OR (
            NEW.trigger_source = 'SURVEY_DISSATISFIED_OPT_IN'
            AND NEW.proposal_id IS NULL
            AND NEW.turn_id IS NOT NULL
            AND NEW.survey_id IS NOT NULL
            AND NEW.reason_code = 'USER_DISSATISFIED'
        )
        OR (
            NEW.trigger_source = 'SYSTEM_POLICY'
            AND NEW.proposal_id IS NULL
            AND NEW.turn_id IS NOT NULL
            AND NEW.survey_id IS NULL
            AND NEW.reason_code IN ('SYSTEM_FAILURE', 'SAFETY_OR_URGENT')
        ),
        NEW.trigger_source,
        NULL
    );

-- 更新必须维持相同来源引用不变量，不能把直接申请伪装为调查申请。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_episode_survey_update;

CREATE TRIGGER trg_cs_episode_survey_update BEFORE
UPDATE ON cs_handoff_episode FOR EACH ROW
SET
    NEW.trigger_source = IF(
        (
            NEW.trigger_source = 'AI_RECOMMENDATION_CONFIRMED'
            AND NEW.proposal_id IS NOT NULL
            AND NEW.turn_id IS NOT NULL
            AND NEW.survey_id IS NULL
            AND NEW.reason_code IN (
                'UNRESOLVED_AFTER_RETRY',
                'USER_DISSATISFIED',
                'REQUIRES_HUMAN_JUDGMENT'
            )
        )
        OR (
            NEW.trigger_source = 'VISITOR_DIRECT_REQUEST'
            AND NEW.proposal_id IS NULL
            AND NEW.turn_id IS NULL
            AND NEW.survey_id IS NULL
            AND NEW.reason_code = 'USER_REQUESTED'
        )
        OR (
            NEW.trigger_source = 'SURVEY_DISSATISFIED_OPT_IN'
            AND NEW.proposal_id IS NULL
            AND NEW.turn_id IS NOT NULL
            AND NEW.survey_id IS NOT NULL
            AND NEW.reason_code = 'USER_DISSATISFIED'
        )
        OR (
            NEW.trigger_source = 'SYSTEM_POLICY'
            AND NEW.proposal_id IS NULL
            AND NEW.turn_id IS NOT NULL
            AND NEW.survey_id IS NULL
            AND NEW.reason_code IN ('SYSTEM_FAILURE', 'SAFETY_OR_URGENT')
        ),
        NEW.trigger_source,
        NULL
    );

SET
    @policy_ddl = IF(
        CAST(SUBSTRING_INDEX(VERSION(), '.', 1) AS UNSIGNED) >= 8
        AND EXISTS (
            SELECT
                1
            FROM
                information_schema.table_constraints
            WHERE
                constraint_schema = DATABASE()
                AND table_name = 'cs_handoff_episode'
                AND constraint_name = 'ck_cs_handoff_episode_reason'
                AND constraint_type = 'CHECK'
        ),
        'ALTER TABLE cs_handoff_episode DROP CHECK ck_cs_handoff_episode_reason',
        'SELECT 1'
    );

PREPARE policy_statement
FROM
    @policy_ddl;

EXECUTE policy_statement;

DEALLOCATE PREPARE policy_statement;

SET
    @policy_ddl = IF(
        CAST(SUBSTRING_INDEX(VERSION(), '.', 1) AS UNSIGNED) >= 8,
        'ALTER TABLE cs_handoff_episode ADD CONSTRAINT ck_cs_handoff_episode_reason CHECK(reason_code IN (''UNRESOLVED_AFTER_RETRY'',''USER_DISSATISFIED'',''REQUIRES_HUMAN_JUDGMENT'',''USER_REQUESTED'',''SYSTEM_FAILURE'',''SAFETY_OR_URGENT''))',
        'SELECT 1'
    );

PREPARE policy_statement
FROM
    @policy_ddl;

EXECUTE policy_statement;

DEALLOCATE PREPARE policy_statement;

CREATE TABLE IF NOT EXISTS cs_system_handoff (
    tenant_id VARCHAR(64) NOT NULL COMMENT '认证租户',
    top_org_id VARCHAR(64) NOT NULL COMMENT '可信顶级组织',
    task_id VARCHAR(64) NOT NULL COMMENT '完整范围/轮次/代次/原因SHA256稳定键',
    conversation_id VARCHAR(128) NOT NULL COMMENT '原AI会话引用',
    assistant_config_id VARCHAR(64) NOT NULL COMMENT '持久智能体配置',
    bvent_id VARCHAR(64) NULL COMMENT '大会引用，组织级为空',
    principal_id VARCHAR(128) NOT NULL COMMENT '原认证访客，不使用系统账号替换',
    turn_id VARCHAR(128) NOT NULL COMMENT '真实失败轮次引用',
    generation_epoch BIGINT NOT NULL COMMENT '失败开始时AI代次，大于0',
    reason_code VARCHAR(32) NOT NULL COMMENT '服务器SYSTEM_FAILURE或SAFETY_OR_URGENT',
    trace_id VARCHAR(128) NOT NULL COMMENT '首次故障trace，重试不覆盖',
    state VARCHAR(16) NOT NULL COMMENT 'PENDING/COMPLETED/SUPERSEDED/DEAD集中状态',
    attempts INT NOT NULL COMMENT '消费失败次数0—5，人工重试重置预算',
    next_attempt_at DATETIME(6) NOT NULL COMMENT 'UTC恢复期限，退避5秒乘次数，最多300秒',
    episode_id VARCHAR(64) NULL COMMENT '仅COMPLETED持有真实同组织会话的人工周期',
    created_at DATETIME(6) NOT NULL COMMENT '首次登记UTC时间',
    retry_actor_id VARCHAR(128) NULL COMMENT '最近一次正式客服恢复预算的主体',
    retry_at DATETIME(6) NULL COMMENT '最近一次人工重试UTC时间',
    retry_count INT NOT NULL DEFAULT 0 COMMENT '累计人工重试次数，不被自动失败预算重置',
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
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COMMENT = '可信系统转人工可靠任务；主键防重、复合FK防跨组织，无逻辑删除';

-- 到期索引用于多节点恢复，消费持generation和任务锁，不执行网络调用。
-- 当前库已存在该对象时跳过；缺失才补建，不删除或重写业务记录。
SET
    @cs_migration_ddl = IF(
        EXISTS (
            SELECT
                1
            FROM
                information_schema.statistics
            WHERE
                table_schema = DATABASE()
                AND table_name = 'cs_system_handoff'
                AND index_name = 'ix_cs_system_due'
        ),
        'SELECT 1',
        'CREATE INDEX ix_cs_system_due ON cs_system_handoff(state,next_attempt_at)'
    );

PREPARE cs_migration_statement
FROM
    @cs_migration_ddl;

EXECUTE cs_migration_statement;

DEALLOCATE PREPARE cs_migration_statement;

-- MySQL5.7值域兜底：非法状态/原因/预算/完成事实以非空约束拒绝，8.x同义。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_system_insert_valid;

CREATE TRIGGER trg_cs_system_insert_valid BEFORE
INSERT
    ON cs_system_handoff FOR EACH ROW
SET
    NEW.state = IF(
        NEW.state IN ('PENDING', 'COMPLETED', 'SUPERSEDED', 'DEAD')
        AND NEW.reason_code IN ('SYSTEM_FAILURE', 'SAFETY_OR_URGENT')
        AND NEW.attempts BETWEEN 0 AND 5
        AND NEW.generation_epoch > 0
        AND NEW.retry_count >= 0
        AND (
            (
                NEW.retry_count = 0
                AND NEW.retry_actor_id IS NULL
                AND NEW.retry_at IS NULL
            )
            OR (
                NEW.retry_count > 0
                AND NEW.retry_actor_id IS NOT NULL
                AND NEW.retry_at IS NOT NULL
            )
        )
        AND (
            (
                NEW.state = 'COMPLETED'
                AND NEW.episode_id IS NOT NULL
            )
            OR (
                NEW.state <> 'COMPLETED'
                AND NEW.episode_id IS NULL
            )
        ),
        NEW.state,
        NULL
    );

-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_system_update_valid;

CREATE TRIGGER trg_cs_system_update_valid BEFORE
UPDATE ON cs_system_handoff FOR EACH ROW
SET
    NEW.state = IF(
        NEW.state IN ('PENDING', 'COMPLETED', 'SUPERSEDED', 'DEAD')
        AND NEW.reason_code IN ('SYSTEM_FAILURE', 'SAFETY_OR_URGENT')
        AND NEW.attempts BETWEEN 0 AND 5
        AND NEW.generation_epoch > 0
        AND NEW.retry_count >= 0
        AND (
            (
                NEW.retry_count = 0
                AND NEW.retry_actor_id IS NULL
                AND NEW.retry_at IS NULL
            )
            OR (
                NEW.retry_count > 0
                AND NEW.retry_actor_id IS NOT NULL
                AND NEW.retry_at IS NOT NULL
            )
        )
        AND (
            (
                NEW.state = 'COMPLETED'
                AND NEW.episode_id IS NOT NULL
            )
            OR (
                NEW.state <> 'COMPLETED'
                AND NEW.episode_id IS NULL
            )
        ),
        NEW.state,
        NULL
    );

-- 合并来源文件结束：data/sql/mysql/ai_20261109_customer_service_system.sql
-- 合并来源文件：data/sql/mysql/ai_20261110_customer_service_routing.sql
-- 目的：由客服上下文唯一维护业务组、成员关联、助手分类路由及申请时冻结的接待归属。
-- 前提：已执行 20261109 客服接待、跟进及系统增量；开发阶段无外部组回填，不迁移旧申请。
-- 执行：一次性维护窗口增量，先备份；MySQL DDL 自动提交，Kingbase 应由部署事务提交。
-- 回滚：先停用客服入口，导出新增配置/路由审计事实后由运维按外键逆序处理；禁止清理旧业务记录。
-- owner/生命周期：仅客服配置应用写组及关联，申请编排写冻结路由；组和路由不逻辑/物理删除。
-- 账号由用户中心拥有，仅保存引用；成员关联可撤销，历史快照保留，租户/组织复合主外键防止串组。
-- 方言：MySQL 5.7+ / InnoDB；5.7由下列触发器补强CHECK。
-- 组织配置聚合根；主键保证一个版本源，非空归属及写入者用于授权审计。
CREATE TABLE IF NOT EXISTS cs_reception_configuration (
    tenant_id VARCHAR(64) NOT NULL, -- 认证租户
    top_org_id VARCHAR(64) NOT NULL, -- 核验后的目标组织
    version BIGINT NOT NULL, -- 从1递增的配置CAS版本
    updated_by VARCHAR(128) NOT NULL, -- 最近管理账号引用，不保存凭据
    updated_at DATETIME(6) NOT NULL, -- 最近提交UTC时间
    PRIMARY KEY (tenant_id, top_org_id),
    CONSTRAINT ck_cs_routing_version CHECK (version > 0) -- 防止无版本配置被发布
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

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
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

-- 成员关联可撤销；不是用户账号主表，账号启用必须实时向用户中心核验。
CREATE TABLE IF NOT EXISTS cs_reception_member (
    tenant_id VARCHAR(64) NOT NULL, -- 认证租户
    top_org_id VARCHAR(64) NOT NULL, -- 所属组织
    group_id VARCHAR(64) NOT NULL, -- 本地业务组引用
    account_id VARCHAR(128) NOT NULL, -- 用户中心稳定账号ID
    display_name VARCHAR(128) NOT NULL, -- 最近核验名称，仅用于展示
    PRIMARY KEY (tenant_id, top_org_id, group_id, account_id), -- 同账号可属于多组，同组禁止重复
    CONSTRAINT fk_cs_member_group FOREIGN KEY (tenant_id, top_org_id, group_id) REFERENCES cs_reception_group (tenant_id, top_org_id, group_id)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

-- 账号反查索引服务实时授权，不存独立账号状态。
-- 当前库已存在该对象时跳过；缺失才补建，不删除或重写业务记录。
SET
    @cs_migration_ddl = IF(
        EXISTS (
            SELECT
                1
            FROM
                information_schema.statistics
            WHERE
                table_schema = DATABASE()
                AND table_name = 'cs_reception_member'
                AND index_name = 'ix_cs_member_account'
        ),
        'SELECT 1',
        'CREATE INDEX ix_cs_member_account ON cs_reception_member(tenant_id,top_org_id,account_id)'
    );

PREPARE cs_migration_statement
FROM
    @cs_migration_ddl;

EXECUTE cs_migration_statement;

DEALLOCATE PREPARE cs_migration_statement;

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
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

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
    confidence DOUBLE NOT NULL, -- 0..1，降级为0
    classification_source VARCHAR(64) NOT NULL, -- MODEL或明确降级原因
    policy_version VARCHAR(64) NOT NULL, -- 语义策略版本
    trace_id VARCHAR(128) NOT NULL, -- 原申请追踪引用，不保存对话正文
    created_at DATETIME(6) NOT NULL, -- 首次分配UTC时间
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
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

-- 分组队列索引避免跨组织全表扫描。
-- 当前库已存在该对象时跳过；缺失才补建，不删除或重写业务记录。
SET
    @cs_migration_ddl = IF(
        EXISTS (
            SELECT
                1
            FROM
                information_schema.statistics
            WHERE
                table_schema = DATABASE()
                AND table_name = 'cs_handoff_route'
                AND index_name = 'ix_cs_frozen_queue'
        ),
        'SELECT 1',
        'CREATE INDEX ix_cs_frozen_queue ON cs_handoff_route(tenant_id,top_org_id,group_id,assistant_config_id)'
    );

PREPARE cs_migration_statement
FROM
    @cs_migration_ddl;

EXECUTE cs_migration_statement;

DEALLOCATE PREPARE cs_migration_statement;

-- MySQL5.7不执行CHECK，插入/更新触发器以同等值域拒绝非法配置，禁止仅靠应用校验。
/* sql-formatter-disable */
DELIMITER $$
-- configuration INSERT值域约束。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_routing_configuration_bi$$
CREATE TRIGGER trg_cs_routing_configuration_bi
    BEFORE INSERT ON cs_reception_configuration
    FOR EACH ROW
BEGIN
    IF NEW.version < 1 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'invalid reception configuration';
    END IF;
END $$
-- configuration UPDATE值域约束。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_routing_configuration_bu$$
CREATE TRIGGER trg_cs_routing_configuration_bu
    BEFORE UPDATE ON cs_reception_configuration
    FOR EACH ROW
BEGIN
    IF NEW.version < 1 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'invalid reception configuration';
    END IF;
END $$
-- group INSERT值域约束。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_routing_group_bi$$
CREATE TRIGGER trg_cs_routing_group_bi
    BEFORE INSERT ON cs_reception_group
    FOR EACH ROW
BEGIN
    IF NEW.category NOT IN('EXHIBITOR', 'VISITOR', 'MEDIA', 'OTHER') THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'invalid reception configuration';
    END IF;
END $$
-- group UPDATE值域约束。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_routing_group_bu$$
CREATE TRIGGER trg_cs_routing_group_bu
    BEFORE UPDATE ON cs_reception_group
    FOR EACH ROW
BEGIN
    IF NEW.category NOT IN('EXHIBITOR', 'VISITOR', 'MEDIA', 'OTHER') THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'invalid reception configuration';
    END IF;
END $$
-- route INSERT值域约束。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_routing_route_bi$$
CREATE TRIGGER trg_cs_routing_route_bi
    BEFORE INSERT ON cs_reception_route
    FOR EACH ROW
BEGIN
    IF NEW.category NOT IN('EXHIBITOR', 'VISITOR', 'MEDIA', 'OTHER') THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'invalid reception configuration';
    END IF;
END $$
-- route UPDATE值域约束。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_routing_route_bu$$
CREATE TRIGGER trg_cs_routing_route_bu
    BEFORE UPDATE ON cs_reception_route
    FOR EACH ROW
BEGIN
    IF NEW.category NOT IN('EXHIBITOR', 'VISITOR', 'MEDIA', 'OTHER') THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'invalid reception configuration';
    END IF;
END $$
-- 冻结路由INSERT时校验类别、置信度和正版本。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_frozen_bi$$
CREATE TRIGGER trg_cs_frozen_bi
    BEFORE INSERT ON cs_handoff_route
    FOR EACH ROW
BEGIN
    IF NEW.category NOT IN('EXHIBITOR', 'VISITOR', 'MEDIA', 'OTHER')
       OR NEW.confidence < 0
       OR NEW.confidence > 1
       OR NEW.config_version < 1 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'invalid frozen route';
    END IF;
END $$
-- 冻结路由UPDATE时校验类别、置信度和正版本。
-- 维护窗口重建同名值域触发器，保证重跑后仍使用最终规则。
DROP TRIGGER IF EXISTS trg_cs_frozen_bu$$
CREATE TRIGGER trg_cs_frozen_bu
    BEFORE UPDATE ON cs_handoff_route
    FOR EACH ROW
BEGIN
    IF NEW.category NOT IN('EXHIBITOR', 'VISITOR', 'MEDIA', 'OTHER')
       OR NEW.confidence < 0
       OR NEW.confidence > 1
       OR NEW.config_version < 1 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'invalid frozen route';
    END IF;
END $$

DELIMITER ;
/* sql-formatter-enable */
-- 合并来源文件结束：data/sql/mysql/ai_20261110_customer_service_routing.sql
-- 2026-11-11 完整需求验收增量：组织营业时间与离线留言认领。
-- 方言：MySQL 5.7+/InnoDB；前提：已执行上文接待、等待与路由结构；先备份、停止业务写入。
-- owner：Customer Service唯一写入；配置管理读取营业策略，等待编排冻结新单期限，访客写留言、原路由组客服认领。
-- 生命周期：规则变更不回写历史期限；留言与工单一起保留审计，无逻辑删除、不级联删除；回滚须先导出并人工审核。
-- 兼容：仅新增表，IF NOT EXISTS支持开发库重复执行；不改变既有工单或通知状态。
CREATE TABLE IF NOT EXISTS cs_reception_calendar (
    tenant_id VARCHAR(64) NOT NULL COMMENT '认证租户，组织隔离复合主键',
    top_org_id VARCHAR(64) NOT NULL COMMENT '用户中心核验后的组织',
    version BIGINT NOT NULL COMMENT '从1递增的保存CAS版本，应用拒绝非正值',
    calendar_version VARCHAR(64) NOT NULL COMMENT '营业规则版本，历史期限追溯引用',
    zone VARCHAR(64) NOT NULL COMMENT '合法IANA时区',
    opens VARCHAR(8) NOT NULL COMMENT '当地同日营业起点，左闭',
    closes VARCHAR(8) NOT NULL COMMENT '当地同日营业终点，右开',
    overrides_json TEXT NOT NULL COMMENT 'ISO日期到营业布尔值JSON，缺省周一到周五',
    updated_by VARCHAR(128) NOT NULL COMMENT '最近保存的认证账号引用',
    updated_at DATETIME(6) NOT NULL COMMENT '最近提交UTC时间',
    PRIMARY KEY (tenant_id, top_org_id) COMMENT '每租户组织唯一营业策略'
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COMMENT = '客服组织营业策略，独立CAS版本';

CREATE TABLE IF NOT EXISTS cs_followup_form (
    tenant_id VARCHAR(64) NOT NULL COMMENT '认证租户',
    top_org_id VARCHAR(64) NOT NULL COMMENT '原接待组织',
    conversation_id VARCHAR(128) NOT NULL COMMENT '原AI与人工会话',
    episode_id VARCHAR(64) NOT NULL COMMENT '原接待周期，不能漂移到后续周期',
    ticket_id VARCHAR(64) NOT NULL COMMENT '原等待owner创建的稳定工单号',
    request_id VARCHAR(100) NOT NULL COMMENT '首次提交幂等键，重试保持原值',
    contact_name VARCHAR(100) NOT NULL COMMENT '访客自填称呼，可空串，不用于认证',
    content TEXT NOT NULL COMMENT '访客主动提交的1到4000字纯文本留言',
    state VARCHAR(16) NOT NULL COMMENT 'OPEN待认领或CLAIMED已认领，应用显式转换',
    version BIGINT NOT NULL COMMENT '从1递增的认领CAS版本',
    assigned_to VARCHAR(128) NULL COMMENT '唯一认领客服账号，未认领为空',
    claim_request_id VARCHAR(100) NULL COMMENT '首次认领幂等键，未认领为空',
    submitted_at DATETIME(6) NOT NULL COMMENT '首次提交UTC时间',
    claimed_at DATETIME(6) NULL COMMENT '首次认领UTC时间，未认领为空',
    PRIMARY KEY (
        tenant_id,
        top_org_id,
        conversation_id,
        episode_id
    ) COMMENT '一原周期至多一个留言表单',
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
    )
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COMMENT = '离线留言原文及唯一认领事实，无自动删除';

-- MySQL5.7不执行CHECK，INSERT/UPDATE双向触发器拒绝非法版本与不完整认领事实；不代替应用CAS。
DROP TRIGGER IF EXISTS trg_cs_calendar_bi;

CREATE TRIGGER trg_cs_calendar_bi BEFORE
INSERT
    ON cs_reception_calendar FOR EACH ROW
SET
    NEW.version = IF(NEW.version > 0, NEW.version, NULL);

DROP TRIGGER IF EXISTS trg_cs_calendar_bu;

CREATE TRIGGER trg_cs_calendar_bu BEFORE
UPDATE ON cs_reception_calendar FOR EACH ROW
SET
    NEW.version = IF(NEW.version > 0, NEW.version, NULL);

DROP TRIGGER IF EXISTS trg_cs_form_bi;

CREATE TRIGGER trg_cs_form_bi BEFORE
INSERT
    ON cs_followup_form FOR EACH ROW
SET
    NEW.state = IF(
        NEW.version > 0
        AND (
            (
                NEW.state = 'OPEN'
                AND NEW.assigned_to IS NULL
                AND NEW.claim_request_id IS NULL
                AND NEW.claimed_at IS NULL
            )
            OR (
                NEW.state = 'CLAIMED'
                AND NEW.assigned_to IS NOT NULL
                AND NEW.claim_request_id IS NOT NULL
                AND NEW.claimed_at IS NOT NULL
            )
        ),
        NEW.state,
        NULL
    );

DROP TRIGGER IF EXISTS trg_cs_form_bu;

CREATE TRIGGER trg_cs_form_bu BEFORE
UPDATE ON cs_followup_form FOR EACH ROW
SET
    NEW.state = IF(
        NEW.version > 0
        AND (
            (
                NEW.state = 'OPEN'
                AND NEW.assigned_to IS NULL
                AND NEW.claim_request_id IS NULL
                AND NEW.claimed_at IS NULL
            )
            OR (
                NEW.state = 'CLAIMED'
                AND NEW.assigned_to IS NOT NULL
                AND NEW.claim_request_id IS NOT NULL
                AND NEW.claimed_at IS NOT NULL
            )
        ),
        NEW.state,
        NULL
    );