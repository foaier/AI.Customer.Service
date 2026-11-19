# 2026-11-19 后端增量迁移

来源只读仓库：`D:/WorkCode/3_Coordination/AICustomerService/byt-cloud-agent`，范围 `12814980..20c34f51ad1fec4baa3e81fe032c89a5fb2cf7ab`。本次将客服相关契约适配到独立 .NET 七层架构、EF ORM 和 React 工作台。

| 来源提交                          | 本项目适配                                                                                              |
| --------------------------------- | ------------------------------------------------------------------------------------------------------- |
| `0f708759` 财务分类和雅式通知     | 正式类别 FINANCE；保留历史数字值、原组 ID 和成员；雅式官方协议与回执                                    |
| `69deb47d` 通用配置值接口和通知键 | 适配已有组织级专用通知 API，完整持久化配置与版本 CAS                                                    |
| `39e0c3b7` 统一开关、企微群路由   | 配置控制 HANDOFF_NOTIFY、旧 INTERNAL、EMAIL、EMAIL_REPLY；四类群映射                                    |
| `7a2b1d6b` 外部身份绑定           | Conversation owner 首次绑定、原访客授权和冻结身份门禁                                                   |
| `5e4519d9` 探针投递字段           | 生产查询返回安全 providerRequestId；测试探针平台未复制                                                  |
| `1205f2cb` 匿名会话人工流程       | 高熵会话凭据、HTTP/SSE/历史/人工/WebSocket 访问；可在 AI 首条消息前申请人工                             |
| `a2dae4ca` 旧财务数据迁移         | 独立数据库 004 增量转换 JSON 类别和路由键，推进旧配置版本                                               |
| `b93ef99f` 管理员读群 ID          | 组织配置接口返回可编辑群 ID，令牌只返回 configured 状态                                                 |
| `3d122e93` 工作台链接             | 适配 `/b/inbox?topOrgId=...&assistantConfigId=...&conversationId=...`，从有权查看的队列定位，不自动认领 |
| `20c34f51` 取消申请幂等重放       | 已取消的原 REQUEST 命令返回 409；新请求键可创建新周期                                                   |

## 组织通知配置

`GET/PUT /api/customer-service/configuration/chat-notifications?topOrgId=...` 沿用组织账号授权。首次 `version=0`，保存递增，过期草稿返回 409。响应字段：`version/enabled/activeProvider/accountCode/baseUrl/publicBaseUrl/chatGroup/apiTokenConfigured`。

```json
{
    "version": 0,
    "enabled": true,
    "activeProvider": "WECOM_ROBOT",
    "publicBaseUrl": "https://support.example.com",
    "chatGroup": {
        "SALES": "sales-hook-id",
        "VISITOR": "visitor-hook-id",
        "FINANCE": "finance-hook-id",
        "EMP": "other-hook-id"
    }
}
```

`activeProvider` 为 WECOM_ROBOT 或 ADSALE；`accountCode` 固定 `vendor_31`；`baseUrl` 固定 `https://hub.adsaleinfo.com/open-api`。新增可选字段省略时保留原值，`chatGroup` 为完整替换。兼容旧群键 EXHIBITOR/MEDIA/OTHER，保存归一到 SALES/FINANCE/EMP；接待类别 API 本身不再接受 MEDIA。

禁用允许空群；启用要求四类群齐全且客服站点为无账号、查询和片段的 HTTPS 地址。企微群值为 10–200 位字母、数字、下划线或连字符的 Hook ID；雅式群 ID 最大 128 字符。ADSALE 启用还要求 Token。

雅式 Token 由 `apiToken` 写入；省略保留，`clearApiToken=true` 显式清除，两者不能同时提供。响应不返回明文或密文。部署 Secret `Notifications__EncryptionKey` 必须是 Base64 编码的随机 32 字节 AES 主密钥，多实例保持一致；AES-256-GCM 随机 nonce 保护令牌，密钥缺失或错误拒绝保存/解密。不要将实际主密钥写入仓库。更换主密钥前需完成令牌重新加密或安全重配。

旧 `Notifications:Enabled`、`Notifications:WeComWebhook` 已移除。邮件网关地址和认证继续由部署注入；EVENT 审计开关独立。关闭组织通知后待发送任务暂停且不耗尝试次数；已开始的网络请求可能已经被供应商受理。

## 即时投递与恢复

启用时，每个新 REQUESTED 在业务事务内登记唯一 `handoff-requested:{episodeId}` 任务。幂等重放不新增；达到 T+5 仅推进旧 waiting 字段，不再新建群提醒。已有 INTERNAL 待发任务仍能处理。

冻结 Provider、通知类别、雅式群 ID、API/客服站点、申请时间、语言、咨询摘要和最近成功 AI 回复。企微 Hook ID 和雅式 Token 发送时读取当前配置。切换 Provider 后旧任务进入 DEAD，不自动改走新通道。通知不包含外部身份、邮箱或推断的用户画像；缺失资料使用 `[—]`，文本最多 2048 UTF-8 字节并保留完整链接。

雅式固定 POST `https://hub.adsaleinfo.com/open-api/messages/send`，认证头 `X-API-Key`，正文为 `recipient_type=group/chat_id/msg_type=text/content.content`。HTTP 2xx 还需 `success:true` 和顶层或 data 内合法 `request_id`，只有 `[A-Za-z0-9._:-]{1,128}` 可以持久化。HTTP 408/502/503 和网络故障允许退避，其他雅式拒绝进入 DEAD；企微要求整数 `errcode=0`。不跟随重定向，不记录供应商正文或凭据。

沿用任务租约、最多五次自动尝试和原周期权限。`GET /api/customer-service/agents/conversations/{id}/waiting?topOrgId=...` 返回安全投递摘要；`POST .../notifications/retry?topOrgId=...&episodeId=...` 可恢复原周期 DEAD 通知，保持 eventKey 与冻结载荷。官方机器人没有严格幂等承诺，恢复投递可能重复。

## 会话与外部身份

| 路径                                                              | 认证和用途                                                               |
| ----------------------------------------------------------------- | ------------------------------------------------------------------------ |
| POST `/api/agent/session`                                         | Bearer；创建登录会话，body `{assistantConfigId}`                         |
| POST `/api/agent/anonymous/session`                               | 公开；仅部署明确允许匿名的助手，body 同上                                |
| POST `/api/agent/session/{sessionId}/external-identity`           | 原登录访客；body `{cookieId}`                                            |
| POST `/api/agent/anonymous/session/{sessionId}/external-identity` | 原匿名凭据；body 同上                                                    |
| POST `/api/agent/anonymous/chatStream`                            | 原匿名凭据；复用文本聊天 DTO，tempUserId 只做一致性核验                  |
| POST `/api/agent/anonymous/history_detail`                        | 原匿名凭据；body `{sessionId,tempUserId,businessType,assistantConfigId}` |

创建响应使用 `isSuccess/returnObj`；匿名返回 `sessionId/tempUserId/credential/expiresAt/externalIdentityRequired`。凭据由 256 位随机值生成，43 位 base64url，仅创建时返回，数据库保存 SHA-256 摘要，七天有效。HTTP 用 `X-Conversation-Credential`，不能和 Authorization 混用。WebSocket 复用 `/api/customer-service/realtime/ws?credential=...&agent=false&conversationId=...&afterSeq=0`；拒绝客服订阅、跨会话与范围不符，连接期间重查到期和撤销。代理访问日志应脱敏认证 query。不提供给旧会话补发凭据的入口。

`cookieId` 为不改写的非空原值，最多 512 字符。同会话同值幂等，不同值返回 409，不回显、不用于授权、合并会话或通知。新会话冻结 `ExternalIdentityRequired`；开启时未绑定即在 AI 轮次创建前返回 `EXTERNAL_IDENTITY_REQUIRED`/409。人工申请通过既有客服入口，不依赖 AI 首条消息或外部身份绑定。

本项目使用受控 `Assistants:Items`，例如在现有助手定义增加 `AllowAnonymous: true` 和 `ExternalIdentityRequired: true`；两项默认 false。匿名可见助手 ID 必须唯一。策略变更只影响之后创建的会话，已有会话不重算。

## 适配边界与数据库

Java 平台的 SYSTEM/TENANT/BVENT 通用配置管理、saveConfirmKey、测试探针控制台和测试聊天页面不属于本项目已有模块；本次保留专用组织配置 API、CAS 和部署助手注册表，提供生产匿名 API，没有复制整个平台管理系统。原 Vue 仍需使用本项目路径和认证契约；React 工作台同步可编辑通知开关/通道/令牌和会话定位。

已有独立库依次执行 001、002、003 后，停写并备份，再执行对应 `data/sql/{sqlite,mysql,kingbase}/004_backend_contracts.sql` 一次。空库可由 ORM 创建最新结构。004 新增会话策略、匿名授权和外部身份表、通知配置字段及供应商回执。保留组 ID/成员/历史分类数字，将旧 MEDIA JSON 转 FINANCE 并推进配置版本；通知默认关闭，补齐配置后显式启用。回滚恢复备份和原版本，禁止对 Java 原库执行。

验证：.NET 集成/协议回归、React 构建和 Edge 浏览器回归，以及 `npm run test:migrations` 对 SQLite 历史 DDL 连续升级。MySQL/Kingbase 脚本完成静态检查，未连接对应实库；外部通知使用 HTTP 协议替身，未向真实群发送测试消息。
