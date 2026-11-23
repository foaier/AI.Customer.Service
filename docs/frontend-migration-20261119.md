# 2026-11-19 前端增量迁移

来源：`D:/WorkCode/3_Coordination/AICustomerService/bayantu.app.bvent.main`，提交 `dbd4b5abc5e6435ff2a3c19473b9a92722cfe80b`（`++人工客服`）。本次按该提交的 Vue 前端差异迁移到独立 React 页面，来源仓库只读。

| 来源文件（相对 EvosWebApp/WebApps.Static）          | 独立项目适配                                                                                           |
| --------------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| `src/views/agentManagement/agentworkbench.vue`      | 在线接待列表消息预览、未读优先、通知去重和合并刷新、失效选中项清理；独立留言页同步未查看优先与通知刷新 |
| `src/views/agentManagement/agentWorkbenchModel.js`  | 沿用已有未读水位、查看回执和消息映射；保留目标 API 的 `scope` 结构                                     |
| `src/views/agentManagement/customerworkbench.vue`   | 新增四类企业微信群配置表单，支持读取、首次保存、冲突提示和未修改禁用保存                               |
| `src/views/agentManagement/customerServiceModel.js` | 财务组文案，`EXHIBITOR → SALES`、`MEDIA → FINANCE`、`OTHER → EMP` 群映射及旧键兼容                     |
| `src/api/customerService.js`、接口文档              | 复用现有收件箱/回执接口，新增本项目正式组织配置 API                                                    |

## 工作台行为

后台预览只加载 CLAIMED/ACTIVE 周期的消息，最多三个并发请求，缓存按周期、访客消息水位和接待版本失效。读取预览不会提交 READ；只有选中会话、消息已显示且页面可见时才提交访客消息水位。相同水位的实时快照不重复加载预览。

通知首帧作为基线，相同 changeToken 去重，变化在 300ms 内合并刷新。完整队列快照替换旧队列，因此已取消/被他人领取的申请不会残留。切换助手会重建对应页面；过期 HTTP 结果及已关闭 WebSocket 不再覆盖当前选择。修正同源 WebSocket 地址，`/b/inbox` 等页面路由不再进入 API 路径。

## 企业微信群接口和投递（初次迁移记录）

以下为提交 412e9e7 时的行为。后续后端补迁移已将总开关改为持久配置、加入 ADSALE 和即时通知，并将正式类别改为 FINANCE；当前契约以 [后端迁移说明](backend-migration-20261119.md) 为准。

`GET/PUT /api/customer-service/configuration/chat-notifications?topOrgId=...`，复用配置页的服务端账号与组织准入。PUT 只接受 `version`、`chatGroup`，不接受身份、认证令牌或任意网关 URL。首次保存版本为 0；成功加一；旧版本返回 409。

```json
{
    "version": 0,
    "chatGroup": {
        "SALES": "sales-hook-id",
        "VISITOR": "visitor-hook-id",
        "FINANCE": "finance-hook-id",
        "EMP": "other-hook-id"
    }
}
```

四类均需填写，可复用一个群。值为官方机器人 Webhook 的 `key=` 参数，即 10–200 位字母、数字、下划线或连字符，不接受完整 URL。GET/PUT 返回当前版本、群配置、`enabled` 和固定 `activeProvider=WECOM_ROBOT`。Hook ID 仅通过组织配置接口返回；部署凭据不回显。

独立项目使用已有官方企微适配器，没有复制 Java 平台的通用系统配置管理、ADSALE 网关或 `saveConfirmKey` 协议。通知总开关仍为部署配置 `Notifications:Enabled`，页面明确展示关闭状态。已有 `Notifications:Authorization`、邮件/事件通道配置保持独立。

保存后，可靠任务 INTERNAL 的等待提醒按任务租户、组织及原接待周期冻结类别选取群。没有保存群配置的组织继续使用原部署通道；仅启用新群路由时，未配置组织的任务不被领取或消耗重试次数。官方机器人必须返回 `errcode=0` 才算受理。仍沿用原项目等待提醒触发时机，并非每条聊天消息都外发。

财务类别继续使用现有 API/数据库中的 `MEDIA` 键，避免本次前端迁移改写历史接待及组 ID；群路由使用 `FINANCE`。页面将默认的“媒体组/媒体客服”显示为“财务客服”，保存分组后持久化该名称；分类提示明确将付款、发票和账单归入该类别。

## 数据库升级

已有独立库在应用 `002_inbox_auto_close.sql` 后、启动新版服务前执行对应 `data/sql/{sqlite,mysql,kingbase}/003_chat_notifications.sql` 一次。新增组织级 `cs_chat_notification_configuration`，不修改已有接待、成员及路由。脚本不用于 Java 原库。空库用 ORM 初始化则会直接创建最新结构。

## 验证

- .NET 集成/协议回归覆盖配置权限、跨组织/租户拒绝、CAS 冲突、非法 URL、旧键归一、持久重读、真实适配器群选择、明确受理和任务候选范围。
- `npm --prefix frontend run build` 验证 TypeScript 和生产打包。
- `npm --prefix frontend run test:frontend` 运行 React 浏览器回归，覆盖预览不标已读、未读排序、通知合并、旧响应隔离、切换助手、群配置保存/重读/冲突/404、移动宽度及留言排序。Windows 可设置 `PLAYWRIGHT_CHANNEL=msedge` 使用已安装 Edge。
- 前端及验证工具依赖由 `frontend/package.json` 独立管理，本迁移不再依赖根目录 Node 工具配置。
- SQLite 验证 `001 + 002 + 003` 连续升级。MySQL、Kingbase 提供对应 DDL，尚未在实库运行；通知 HTTP 使用可控协议替身，未向真实企业微信群发送测试消息。
