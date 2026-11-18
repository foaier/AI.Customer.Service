-- Run once after 001_initial.sql; stop concurrent DDL before applying.
ALTER TABLE cs_handoff_episode ADD COLUMN IF NOT EXISTS auto_close_at TIMESTAMP NULL;
ALTER TABLE cs_handoff_episode ADD COLUMN IF NOT EXISTS resolution_reason VARCHAR(32) NULL;
UPDATE cs_handoff_episode SET resolution_reason = 'AGENT_MANUAL' WHERE status = 3 AND resolution_reason IS NULL;
CREATE INDEX IF NOT EXISTS IX_cs_handoff_episode_status_auto_close_at_id ON cs_handoff_episode (status, auto_close_at, id);

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_episode_auto_close') THEN
        ALTER TABLE cs_handoff_episode ADD CONSTRAINT ck_episode_auto_close CHECK (
            (status = 3 AND resolution_reason IS NOT NULL AND resolution_reason IN ('AGENT_MANUAL', 'VISITOR_INACTIVE_5M') AND auto_close_at IS NULL)
            OR (status <> 3 AND resolution_reason IS NULL AND (auto_close_at IS NULL OR status = 2))
        );
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS cs_followup_receipt (
    id character varying(128) NOT NULL,
    tenant_id character varying(64) NOT NULL,
    top_org_id character varying(64) NOT NULL,
    episode_id character varying(128) NOT NULL,
    seen_by character varying(128) NOT NULL,
    seen_at timestamp NOT NULL,
    version bigint NOT NULL,
    conversation_id character varying(128) NOT NULL,
    assistant_config_id character varying(128) NOT NULL,
    bvent_id character varying(128),
    principal_id character varying(128) NOT NULL,
    CONSTRAINT "PK_cs_followup_receipt" PRIMARY KEY (tenant_id, top_org_id, id),
    CONSTRAINT ck_cs_followup_receipt_scope CHECK (tenant_id <> '' AND top_org_id <> '' AND id <> '' AND version >= 1),
    CONSTRAINT fk_cs_followup_receipt_conversation FOREIGN KEY (tenant_id, top_org_id, conversation_id)
        REFERENCES ai_conversation_generation (tenant_id, top_org_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_cs_followup_receipt_form FOREIGN KEY (tenant_id, top_org_id, episode_id)
        REFERENCES cs_followup_form (tenant_id, top_org_id, episode_id) ON DELETE RESTRICT,
    CONSTRAINT uq_cs_followup_receipt_seen UNIQUE (tenant_id, top_org_id, conversation_id, episode_id, seen_by)
);
CREATE INDEX IF NOT EXISTS IX_cs_followup_receipt_episode ON cs_followup_receipt (tenant_id, top_org_id, episode_id);
