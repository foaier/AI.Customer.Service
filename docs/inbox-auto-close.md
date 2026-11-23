# 收件箱提醒与超时结单

提供坐席收件箱汇总、留言首次查看回执，以及客服回复后访客沉默五分钟自动结单。

## 收件箱

`GET /api/customer-service/agents/inbox/summary?topOrgId={org}&assistantConfigId={assistant}` 返回 `unreadMessageCount`、`unreadConversationCount`、`newFollowupCount`、`pendingReceptionCount` 和不透明的 `changeToken`。省略 `assistantConfigId` 时汇总该组织下账号当前有权访问的助手。

聊天未读只统计本人当前 `CLAIMED/ACTIVE` 接待周期中、超过本人持久 `readSeq` 的访客消息，不把序号差当条数。列表的 `unreadCount`、`lastVisitorSeq`、`readSeq` 和 `lastVisitorMessageAt` 不返回消息正文。READ 仍通过原 `/receipts` 接口提交。

新留言数只统计真实 FORM、未结案、未被当前账号 SEEN，且未认领或由本人认领的工单。查看回执：

```http
POST /api/customer-service/agents/conversations/{conversationId}/followups/{episodeId}/receipts?topOrgId={org}
Content-Type: application/json

{"kind":"SEEN","ticketId":"ticket_x"}
```

查看事实以原接待周期和正式客服账号为键保存；重复请求返回首次 `seenAt`。页面须在对应留言实际显示后再提交，队列摘要或通知帧不能代替查看回执。

客服队列 WebSocket 先发送既有 `snapshot`，随后以 `inbox.notifications` 发送当前提醒基线；后续仅在完整授权范围的 `changeToken` 改变时发送新帧。消息正文仍只从原消息接口获取。

## 自动结单

只有客服已首响的 ACTIVE 周期启用此规则。每条成功保存的客服消息设置 `autoCloseAt` 为服务端 UTC 时间加五分钟；访客回复清空截止时间，后续客服回复重新计时。到期后复用 RESOLVED 和既有 AI 恢复事务，`resolutionReason` 为 `VISITOR_INACTIVE_5M`；人工结束为 `AGENT_MANUAL`。实时状态仍通过既有 snapshot 恢复。

后台每五秒最多扫描 100 个到期候选，并在组织写锁内复核状态和截止时间。消息请求先取得写锁且发现期限已到时，会提交自动结单后返回 409；之前成功保存的同键消息仍可幂等重放。

## 数据库升级

每个数据库在 `001_initial.sql` 后、发布新版服务前应用对应脚本。脚本会增加接待截止和结案原因列、回填既有 RESOLVED 周期为 `AGENT_MANUAL`，并创建 `cs_followup_receipt`：

- MySQL：`data/sql/mysql/002_inbox_auto_close.sql`
- SQLite：`data/sql/sqlite/002_inbox_auto_close.sql`
- Kingbase：`data/sql/kingbase/002_inbox_auto_close.sql`

升级脚本只执行一次；升级期间停止并发 DDL。SQLite 的状态约束由应用执行，MySQL/Kingbase 同时创建数据库 CHECK 约束。
