-- Run once after 002_inbox_auto_close.sql.
CREATE TABLE cs_chat_notification_configuration (
    id VARCHAR(128) NOT NULL,
    tenant_id VARCHAR(64) NOT NULL,
    top_org_id VARCHAR(64) NOT NULL,
    version BIGINT NOT NULL,
    chat_group LONGTEXT NOT NULL,
    CONSTRAINT PK_cs_chat_notification_configuration PRIMARY KEY (tenant_id, top_org_id, id),
    CONSTRAINT ck_cs_chat_notification_configuration_scope CHECK (
        tenant_id <> ''
        AND top_org_id <> ''
        AND id <> ''
        AND version >= 1
    ),
    UNIQUE KEY IX_cs_chat_notification_configuration_tenant_id_top_org_id (tenant_id, top_org_id)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin;
