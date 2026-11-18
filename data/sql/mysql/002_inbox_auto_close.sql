-- Run once after 001_initial.sql; stop concurrent DDL before applying.
ALTER TABLE `cs_handoff_episode`
    ADD COLUMN `auto_close_at` DATETIME(6) NULL COMMENT '客服最近回复后的五分钟自动结单截止 UTC 时间',
    ADD COLUMN `resolution_reason` VARCHAR(32) NULL COMMENT 'AGENT_MANUAL 或 VISITOR_INACTIVE_5M';

UPDATE `cs_handoff_episode`
SET `resolution_reason` = 'AGENT_MANUAL'
WHERE `status` = 3 AND `resolution_reason` IS NULL;

CREATE INDEX `IX_cs_handoff_episode_status_auto_close_at_id`
    ON `cs_handoff_episode` (`status`, `auto_close_at`, `id`);

ALTER TABLE `cs_handoff_episode`
    ADD CONSTRAINT `ck_episode_auto_close` CHECK (
        (`status` = 3 AND `resolution_reason` IS NOT NULL AND `resolution_reason` IN ('AGENT_MANUAL', 'VISITOR_INACTIVE_5M') AND `auto_close_at` IS NULL)
        OR (`status` <> 3 AND `resolution_reason` IS NULL AND (`auto_close_at` IS NULL OR `status` = 2))
    );

CREATE TABLE `cs_followup_receipt` (
    `id` VARCHAR(128) NOT NULL,
    `tenant_id` VARCHAR(64) NOT NULL,
    `top_org_id` VARCHAR(64) NOT NULL,
    `episode_id` VARCHAR(128) NOT NULL,
    `seen_by` VARCHAR(128) NOT NULL COMMENT '正式认证客服账号',
    `seen_at` DATETIME(6) NOT NULL COMMENT '首次查看 UTC 时间',
    `version` BIGINT NOT NULL,
    `conversation_id` VARCHAR(128) NOT NULL,
    `assistant_config_id` VARCHAR(128) NOT NULL,
    `bvent_id` VARCHAR(128) NULL,
    `principal_id` VARCHAR(128) NOT NULL,
    CONSTRAINT `PK_cs_followup_receipt` PRIMARY KEY (`tenant_id`, `top_org_id`, `id`),
    CONSTRAINT `ck_cs_followup_receipt_scope` CHECK (`tenant_id` <> '' AND `top_org_id` <> '' AND `id` <> '' AND `version` >= 1),
    CONSTRAINT `fk_cs_followup_receipt_conversation` FOREIGN KEY (`tenant_id`, `top_org_id`, `conversation_id`)
        REFERENCES `ai_conversation_generation` (`tenant_id`, `top_org_id`, `id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_cs_followup_receipt_form` FOREIGN KEY (`tenant_id`, `top_org_id`, `episode_id`)
        REFERENCES `cs_followup_form` (`tenant_id`, `top_org_id`, `episode_id`) ON DELETE RESTRICT,
    CONSTRAINT `uq_cs_followup_receipt_seen` UNIQUE (`tenant_id`, `top_org_id`, `conversation_id`, `episode_id`, `seen_by`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE INDEX `IX_cs_followup_receipt_episode` ON `cs_followup_receipt` (`tenant_id`, `top_org_id`, `episode_id`);
