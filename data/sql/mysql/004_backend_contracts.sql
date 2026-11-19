-- 目的：同步 2026-11-19 后端财务分类、通知配置及会话身份契约。方言：mysql。
-- 前提：仅独立 .NET 库，已执行 001、002、003；停写并备份后执行一次。
-- 兼容：保留组 ID、成员、历史接待数值分类；API 改用 FINANCE，旧草稿版本失效。
-- 回滚：发布失败时恢复升级前数据库备份及旧程序；不要只删除授权表后运行新程序。
-- owner：Conversation 写匿名授权、外部身份及会话策略；Customer Service 写通知配置/任务回执。
-- 生命周期：所有记录物理保留；凭据撤销用 revoked_at，不级联删除历史消息。
ALTER TABLE ai_conversation_generation
ADD COLUMN external_identity_required TINYINT(1) NOT NULL DEFAULT 0;

ALTER TABLE cs_reliable_task
ADD COLUMN provider_request_id VARCHAR(128) NULL;

ALTER TABLE cs_chat_notification_configuration
ADD COLUMN enabled TINYINT(1) NOT NULL DEFAULT 0;

ALTER TABLE cs_chat_notification_configuration
ADD COLUMN active_provider VARCHAR(128) NOT NULL DEFAULT 'WECOM_ROBOT';

ALTER TABLE cs_chat_notification_configuration
ADD COLUMN account_code VARCHAR(128) NOT NULL DEFAULT 'vendor_31';

ALTER TABLE cs_chat_notification_configuration
ADD COLUMN base_url VARCHAR(256) NOT NULL DEFAULT 'https://hub.adsaleinfo.com/open-api';

ALTER TABLE cs_chat_notification_configuration
ADD COLUMN public_base_url VARCHAR(256) NOT NULL DEFAULT '';

ALTER TABLE cs_chat_notification_configuration
ADD COLUMN api_token_ciphertext VARCHAR(8192) NULL;

-- 升级后通知缺省关闭；管理员补齐站点、群映射/令牌后显式启用，不再读取旧部署总开关。
-- 只替换序列化类别/路由键，保持组引用和其它 JSON 字段；递增 CAS 版本拒绝旧页面覆盖。
UPDATE cs_reception_configuration
SET
    `groups` =
REPLACE
    (
        REPLACE
            (
                `groups`,
                '"category":"MEDIA"',
                '"category":"FINANCE"'
            ),
            '"category": "MEDIA"',
            '"category": "FINANCE"'
    ),
    `routes` =
REPLACE
    (`routes`, '"MEDIA":', '"FINANCE":'),
    version = version + 1
WHERE
    `groups` LIKE '%"category":"MEDIA"%'
    OR `groups` LIKE '%"category": "MEDIA"%'
    OR `routes` LIKE '%"MEDIA":%';

-- 匿名授权：高熵凭据只存摘要，expires_at 为七天 UTC 到期时间，revoked_at 非空即拒绝访问。
CREATE TABLE ai_anonymous_conversation_access (
    id VARCHAR(128) NOT NULL,
    tenant_id VARCHAR(64) NOT NULL,
    top_org_id VARCHAR(64) NOT NULL,
    version BIGINT NOT NULL,
    conversation_id VARCHAR(128) NOT NULL,
    assistant_config_id VARCHAR(128) NOT NULL,
    bvent_id VARCHAR(128) NULL,
    principal_id VARCHAR(128) NOT NULL,
    credential_hash VARCHAR(64) NOT NULL,
    expires_at DATETIME(6) NOT NULL,
    revoked_at DATETIME(6) NULL,
    CONSTRAINT PK_ai_anonymous_conversation_access PRIMARY KEY (tenant_id, top_org_id, id),
    CONSTRAINT ck_ai_anonymous_conversation_access_scope CHECK (
        tenant_id <> ''
        AND top_org_id <> ''
        AND id <> ''
        AND version >= 1
    ),
    FOREIGN KEY (tenant_id, top_org_id, conversation_id) REFERENCES ai_conversation_generation (tenant_id, top_org_id, id) ON DELETE RESTRICT
);

-- 全局摘要唯一用于凭据定位；完整会话归属唯一防止重复补发授权。
CREATE UNIQUE INDEX IX_ai_anonymous_access_credential ON ai_anonymous_conversation_access (credential_hash);

CREATE UNIQUE INDEX IX_ai_anonymous_access_conversation ON ai_anonymous_conversation_access (tenant_id, top_org_id, conversation_id);

-- 外部身份：仅 ADSALE；external_auth_id 为不透明 cookie ID，不用于认证，不进入通知。
CREATE TABLE ai_conversation_external_identity (
    id VARCHAR(128) NOT NULL,
    tenant_id VARCHAR(64) NOT NULL,
    top_org_id VARCHAR(64) NOT NULL,
    version BIGINT NOT NULL,
    conversation_id VARCHAR(128) NOT NULL,
    assistant_config_id VARCHAR(128) NOT NULL,
    bvent_id VARCHAR(128) NULL,
    principal_id VARCHAR(128) NOT NULL,
    provider_code VARCHAR(128) NOT NULL,
    external_auth_id VARCHAR(512) NOT NULL,
    bound_at DATETIME(6) NOT NULL,
    CONSTRAINT ck_external_identity_provider CHECK (provider_code = 'ADSALE'),
    CONSTRAINT PK_ai_conversation_external_identity PRIMARY KEY (tenant_id, top_org_id, id),
    CONSTRAINT ck_ai_conversation_external_identity_scope CHECK (
        tenant_id <> ''
        AND top_org_id <> ''
        AND id <> ''
        AND version >= 1
    ),
    FOREIGN KEY (tenant_id, top_org_id, conversation_id) REFERENCES ai_conversation_generation (tenant_id, top_org_id, id) ON DELETE RESTRICT
);

-- 同范围同供应商只允许首个绑定；不同会话可使用相同外部 ID，绝不按此 ID 合并或授予权限。
CREATE UNIQUE INDEX IX_ai_external_identity_scope_provider ON ai_conversation_external_identity (
    tenant_id,
    top_org_id,
    conversation_id,
    provider_code
);
