# 客服邮件最终回复与结案

2026-11-09。客服上下文唯一主写，适用于访客已明确选择邮件跟进且已取得有效邮箱的原工单。在线接待结束与邮件工单结案是不同事实。

## 业务与数据边界

`cs_waiting_workflow` 持有原邮箱、稳定 ticketId 和等待结果；`cs_followup_response` 持有该工单唯一最终正文、回复客服、首次追踪标识、可靠发送任务及结案请求/客服引用。正文与 PENDING 任务在同一行原子提交，等价于事务 Outbox。用户中心仍唯一管理账号、组、成员。

工单按 tenant/topOrg/conversation/episode/ticket 复合外键隔离。应用先锁 generation，再锁原 episode、workflow、response；后来的新接待不会改变旧工单目标。HTTP 每次通过正式登录和用户中心实时组成员校验，不接收客户端邮箱、actorId、groupId 或组织归属覆盖。页面只返回 ticketId/state/version/subject/content/attempts，不返回邮箱和租约。

每张工单只有一份不可变最终回复。相同 requestId、客服、主题、正文返回首次事实；异键或异内容不能覆盖。主题为无换行的 1—200 字符纯文本，正文为 1—10000 字符纯文本，拒绝 NUL。正文不入模型和普通日志。

## 状态与可靠投递

| 状态    | 业务含义                       | 下一动作                      |
| ------- | ------------------------------ | ----------------------------- |
| OPEN    | 页面投影：尚无最终回复行       | 客服提交回复                  |
| PENDING | 正文与任务已提交，尚未网关受理 | Worker 领取                   |
| CLAIMED | Worker 持有 30 秒租约          | 确认受理/失败，或租约到期接管 |
| SENT    | 网关以相同消息键明确受理       | 客服显式结案                  |
| DEAD    | 五次自动尝试耗尽               | 客服显式重试，保持原键和正文  |
| CLOSED  | 已受理后由客服显式结案         | 只读恢复、原请求重放          |

网关受理不表示收件箱送达或已读。只有 SENT 可以关闭；结案同键必须同客服。所有推进使用正版本 CAS。已过期租约和被接管的旧租约不能确认当前任务。网络位于事务外，每五秒最多领取十条，退避最大 300 秒；单条发送/确认失败不阻断其他任务。通知网关关闭时不领取、不消耗次数。

受控通知网关接收 `kind=EMAIL_REPLY`，包含 eventKey、scope、episodeId、ticketId、traceId、email、subject、content。`Idempotency-Key` 和 eventKey 均为 `reply:{ticketId}`；网关必须持久化去重。只有 2xx 且 JSON 为 `accepted=true`、相同 eventKey 才确认 SENT；空 2xx、错误键、超时都不算成功。

## HTTP 与配置

基础路径：`/api/customer-service/agents/conversations/{conversationId}/followups/{episodeId}`，均需 `?topOrgId=...`。

| 方法/后缀   | 请求                                         | 结果                         |
| ----------- | -------------------------------------------- | ---------------------------- |
| GET         | 无 body                                      | 原工单回复最小投影           |
| POST /reply | requestId, expectedVersion, subject, content | 原子保存最终回复及待投递任务 |
| POST /retry | requestId, expectedVersion                   | 仅 DEAD 恢复 PENDING         |
| POST /close | requestId, expectedVersion                   | 仅 SENT 结案，保留首次关闭者 |

未知 JSON 字段返回 400，撤权返回 403，状态/版本冲突返回 409，依赖不可用返回 503。错误不回显正文、邮箱、SQL 或凭据。

| 配置                                    | 默认值与范围                | 缺省行为                         |
| --------------------------------------- | --------------------------- | -------------------------------- |
| customer-service.notifications.enabled  | 应用级布尔，false           | 不调用网关、不领取任务           |
| customer-service.notifications.endpoint | 服务端 HTTP(S) 地址，默认空 | 空地址禁止投递；连接2秒、请求5秒 |

迁移：统一使用 `data/sql/{mysql|kingbase}/ai_20261111_customer_service/all_in_one.sql`；其中最终回复来源段必须晚于 followup，已部分部署时只执行尚未部署的完整来源段，见[数据库入口](database-20261111.md)。完成迁移再启动应用；API和Worker内置，网关持久幂等就绪后才开启外部通知。MySQL 5.7 的本增量通过触发器补足 CHECK；Kingbase 使用 CHECK。禁止盲目重跑或清表。回滚前停止应用并关闭外部通知并导出正文和待处理任务，由 owner 决定结构处置，不删除审计记录。

2026-11-09 开发 MySQL 5.7.35 已执行增量；快照 `.codex/followup-response-before-schema-20261109.sql`。非法状态、错组织、缺租约、缺结案事实四项目标库探针均被拒绝并回滚，回复表业务行仍为零。该阶段本机API已开启，外部通知关闭；2026-11-10起回复Worker随应用运行，网关关闭时不领取任务。独立 H2/Tomcat/本机 HTTP 网关的业务页面验收见 `.codex/qa/customer-service-reply-20261109/`；此证据不代表真实邮箱投递。

## 业务页面

- 访客：`/customer-service/visitor.html?assistantConfigId={id}`，加载配置后直接咨询、申请人工、等待、邮箱跟进和评价。会话 ID 写入 URL，刷新按正式权限恢复原会话。
- 客服：`/customer-service/agent.html?assistantConfigId={id}`，组内待接单/本人接待、双向消息、后续跟进、最终邮件回复与结案。每个操作仍由后端核验正式客服资格。

两个入口共用联调台的协议与状态实现，业务页没有 Mock 管理控件或内部 JSON 状态。未提供 assistantConfigId 时明确显示入口缺配置；不自动选择组织或助手。现有联调台仍用于开发定位，不作为正式权限来源。
