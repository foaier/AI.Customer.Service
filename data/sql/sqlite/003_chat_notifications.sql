-- Run once after 002_inbox_auto_close.sql. No credentials or global settings are stored here.
CREATE TABLE cs_chat_notification_configuration (
    id TEXT NOT NULL,
    tenant_id TEXT NOT NULL,
    top_org_id TEXT NOT NULL,
    version INTEGER NOT NULL,
    chat_group TEXT NOT NULL,
    CONSTRAINT PK_cs_chat_notification_configuration PRIMARY KEY (tenant_id, top_org_id, id),
    CONSTRAINT ck_cs_chat_notification_configuration_scope CHECK (
        tenant_id <> ''
        AND top_org_id <> ''
        AND id <> ''
        AND version >= 1
    )
);

CREATE UNIQUE INDEX IX_cs_chat_notification_configuration_tenant_id_top_org_id ON cs_chat_notification_configuration (tenant_id, top_org_id);
