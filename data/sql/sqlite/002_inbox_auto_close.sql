-- Run once after 001_initial.sql. The application enforces state consistency on SQLite.
ALTER TABLE cs_handoff_episode ADD COLUMN auto_close_at TEXT NULL;
ALTER TABLE cs_handoff_episode ADD COLUMN resolution_reason TEXT NULL;
UPDATE cs_handoff_episode SET resolution_reason = 'AGENT_MANUAL' WHERE status = 3 AND resolution_reason IS NULL;
CREATE INDEX IX_cs_handoff_episode_status_auto_close_at_id ON cs_handoff_episode (status, auto_close_at, id);

CREATE TABLE cs_followup_receipt (
    id TEXT NOT NULL,
    tenant_id TEXT NOT NULL,
    top_org_id TEXT NOT NULL,
    episode_id TEXT NOT NULL,
    seen_by TEXT NOT NULL,
    seen_at TEXT NOT NULL,
    version INTEGER NOT NULL,
    conversation_id TEXT NOT NULL,
    assistant_config_id TEXT NOT NULL,
    bvent_id TEXT NULL,
    principal_id TEXT NOT NULL,
    CONSTRAINT PK_cs_followup_receipt PRIMARY KEY (tenant_id, top_org_id, id),
    CONSTRAINT ck_cs_followup_receipt_scope CHECK (tenant_id <> '' AND top_org_id <> '' AND id <> '' AND version >= 1),
    CONSTRAINT FK_cs_followup_receipt_ai_conversation FOREIGN KEY (tenant_id, top_org_id, conversation_id)
        REFERENCES ai_conversation_generation (tenant_id, top_org_id, id) ON DELETE RESTRICT,
    CONSTRAINT FK_cs_followup_receipt_form FOREIGN KEY (tenant_id, top_org_id, episode_id)
        REFERENCES cs_followup_form (tenant_id, top_org_id, episode_id) ON DELETE RESTRICT,
    CONSTRAINT uq_cs_followup_receipt_seen UNIQUE (tenant_id, top_org_id, conversation_id, episode_id, seen_by)
);
CREATE INDEX IX_cs_followup_receipt_episode ON cs_followup_receipt (tenant_id, top_org_id, episode_id);
