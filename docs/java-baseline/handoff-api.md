# 访客人工申请、恢复与取消意图接口

说明：本文保留2026-11-07申请/取消意图阶段契约，涉及外部claim owner、取消回执、Tool关闭及未接通评价的旧结论已被替代。2026-11-10当前实现由本项目唯一主写接单/接待/消息，现行入口、SSE、取消/结束、工单及系统策略见 [reception-api.md](reception-api.md)、[survey-waiting-realtime.md](survey-waiting-realtime.md)、[availability-system-metrics.md](availability-system-metrics.md)。产品规则以 [spec.md](spec.md) 为准，实施与验收状态见 [tasks.md](tasks.md)。以下旧阶段细节不能作为当前待办或发布证据。

## 1. 公共约定

- 路径前缀为 `/api/customer-service/conversations/{conversationId}/handoffs`。部署有 context-path 时须加实际前缀，例如本地配置中的 `/ai_chat`。
- 使用现有正式登录认证；浏览器必须通过当前认证模块携带有效凭据。仅页面已登录不会自动为任意自定义请求补上 Authorization。本文不新增登录或 Token 存储机制。
- 访客主体来自认证 `accountId`。Conversation owner 提供会话租户、顶级组织、assistant 和大会，用户-会话关系 owner 验证访问权。当前支持有可信持久归属的会展智能体会话；缺少归属、跨租户或无访问关系均拒绝。匿名恢复凭据尚未实现。
- 请求体只接受下文命令字段；客户端不得指定身份、租户、组织、assistant、大会、客服组或客服账号。即使 JSON 层忽略额外字段，也不会把这些值写入可信命令。
- 请求和响应支持 `X-Request-Id`；同时提交 Header 与 body 的 `requestId` 时，两者须一致。客户端应在首次发出命令前保存幂等键，网络重试复用原键。
- 所有下述 Controller 响应均为 `Cache-Control: no-store`，公共信封字段为 `requestId/code/message/retryable/serverTime/data`。`serverTime`、`requestedAt` 为 UTC 时间，示例仅说明字段形状。

## 2. 访客直接申请人工

`POST /api/customer-service/conversations/{conversationId}/handoffs`

```json
{ "requestId": "apply-unique-id" }
```

访客明确申请时立即执行，无需 AI 建议卡和二次确认。当前直接申请没有客户端版本字段；应用服务通过 Conversation generation owner 行锁检查归属和 AI/HUMAN 状态。

成功 HTTP 200，核心响应：

```json
{
    "code": "REQUESTED",
    "message": "转人工申请已记录，等待客服受理",
    "data": {
        "requestId": "apply-unique-id",
        "episodeId": "hde_generated-id",
        "status": "REQUESTED",
        "version": 1,
        "replayed": false
    }
}
```

一个共享数据源短事务写 episode、直接申请回执、`handoff.requested` Outbox，并将 Conversation reply owner 切到 HUMAN、推进 generation epoch。任一步失败整体回滚。当前代码直接申请来源为 `VISITOR_DIRECT_REQUEST`、原因为 `USER_REQUESTED`，对应规范的用户明确申请语义；AI 建议确认来源为 `AI_RECOMMENDATION_CONFIRMED`。其余来源和原因的统一字面值仍须在阶段 1 的完整事件契约中收敛，不能把尚未实现的评价来源视为已支持。

同键同参重放返回原 episode，`replayed=true`；HUMAN owner 期间新申请键或同键异参返回冲突。当前直接申请若两处均未提供键，会生成服务端键，客户端仍应主动提供稳定键，否则首次响应丢失时无法按原键重试；可先调用恢复接口确认状态。`REQUESTED` 仅表示本地意图已记录，不能显示“客服已接入”。

## 3. 刷新或重连恢复

`GET /api/customer-service/conversations/{conversationId}/handoffs/latest`

无申请时 HTTP 200，`code=OK`，`data={"episode":null}`。有申请时 `data` 形状为：

```json
{
    "episode": {
        "episodeId": "hde_generated-id",
        "conversationId": "conversation-id",
        "triggerSource": "VISITOR_DIRECT_REQUEST",
        "reasonCode": "USER_REQUESTED",
        "status": "REQUESTED",
        "version": 1,
        "requestedAt": "2026-11-07T00:00:00Z",
        "cancellation": {
            "cancellationId": "hca_generated-id",
            "status": "PENDING_OWNER_ACK",
            "requestedAt": "2026-11-07T00:01:00Z"
        }
    }
}
```

尚未取消时 `cancellation=null`。按 `requested_at DESC, episode_id DESC` 稳定取最新周期。查询精确匹配可信 tenant/topOrg/conversation/assistant/bvent，不返回内部路由、主体和 trace，也不产生任何写入。该接口恢复人工申请进度，不是完整消息时间线或实时事件补齐接口。

## 4. 提交取消意图

`POST /api/customer-service/conversations/{conversationId}/handoffs/{episodeId}/cancellation`

```json
{ "requestId": "cancel-unique-id", "expectedEpisodeVersion": 1 }
```

取消键必须由 body 或 Header 至少一处提供，最长 128 字符；`expectedEpisodeVersion` 必须为正数，取自最近的周期恢复结果。当前仅支持 `REQUESTED` 周期；即使账号可查看 conversation，也必须匹配 episode 原申请者才可取消。

成功及同键同参重放均 HTTP 202：

```json
{
    "code": "PENDING_OWNER_ACK",
    "message": "取消申请已记录，正在等待客服系统确认",
    "data": {
        "requestId": "cancel-unique-id",
        "cancellationId": "hca_generated-id",
        "episodeId": "hde_generated-id",
        "status": "PENDING_OWNER_ACK",
        "expectedEpisodeVersion": 1,
        "requestedAt": "2026-11-07T00:01:00Z",
        "replayed": false
    }
}
```

在 episode 行锁下校验范围、原申请者、幂等摘要、期望版本和集中状态规则，原子写取消事实及 Outbox。同键同参保持首次 `cancellationId/requestedAt`；trace 不参与摘要。一个周期只允许一个取消意图，另一个键重复提交、复用键改变参数或旧版本均返回 409。

取消意图独立于 episode 状态，不改变 `REQUESTED`、原来源/原因、episode 版本及 HUMAN owner；AI 仍不可启动新回复。前端应展示“等待客服系统确认”，刷新后继续展示相同进度。当前没有取消确认回调，不能根据 202、轮询次数、超时或 Rabbit ACK 展示“取消成功/已恢复 AI”。

## 5. 错误与客户端处理

| HTTP / code                | 含义                                                | 客户端处理                                     |
| -------------------------- | --------------------------------------------------- | ---------------------------------------------- |
| 400 / `INVALID_ARGUMENT`   | 键冲突、取消缺少稳定键、缺少/非法版本或领域输入非法 | 修正输入；不要自动更换键重发写命令             |
| 403 / `FORBIDDEN`          | 登录主体、会话归属、路由绑定或原申请者校验未通过    | 停止命令并核对登录和会话权限                   |
| 409 / `STATE_CONFLICT`     | 版本变化、状态不允许、同键异参或同周期重复取消      | 先恢复最新状态，再由用户决定；不无限重试       |
| 503 / `INTERNAL_RETRYABLE` | 未分类基础设施故障                                  | 稍后用同键同参重试，必要时先恢复确认是否已提交 |

认证过滤器拒绝失效/缺失凭据、MVC 拒绝非法 JSON 等可能在进入 Controller 前返回平台标准错误，不能假设它们也具有上述信封。Controller 的 403/409 不回显内部异常细节。

## 6. 数据、投递和部署前提

- `cs_handoff_episode` 和 `cs_handoff_cancellation` 均由客服编排 owner 主写。取消表以 tenant/org/cancellationId 为主键，以 tenant/org/episode 和 tenant/org/conversation/requestId 唯一约束去重；完整 tenant/org/conversation/episode 外键阻止跨会话挂接。所有字段保留审计所需事实，不做逻辑删除或单独删除幂等回执，归档须统一处理。
- 取消事件 `handoff.cancellation.requested` 的 `aggregateId=cancellationId`、`schemaVersion=2`。payload 包含 `cancellationId/episodeId/conversationId/expectedEpisodeVersion/status`，不含凭据、聊天正文或访客主体。现有 Rabbit envelope 另外携带 tenant/topOrg/conversation/assistant/bvent/trace/turn 引用；AI 确认路径保留原 turn，直接申请 turn 可为空。
- 取消事实和 Outbox 使用同一主数据源事务；投递重试、租约、次数上限和 DEAD 处理沿用 handoff Worker。Rabbit ACK 只证明 Broker 接收，不能证明客服系统执行或最终撤销。
- 开发库只使用与数据库方言匹配的一份合并文件：`data/sql/{mysql|kingbase}/ai_20261111_customer_service/all_in_one.sql`。文件内部先建立 generation 和 handoff episode，再创建依赖完整 episode 唯一键的取消表，后续按顺序覆盖本轮全部客服增量；已部分部署时只执行缺失来源段，见[数据库入口](database-20261111.md)。
- 该文件是一次性开发结构，不为已经部分执行的旧分批脚本提供升级或重复运行兼容。执行前先盘点目标库并备份；中途失败应核对已创建对象后修复，不可盲目重跑。本轮没有执行真实数据库 DDL。
- 建议卡、申请与恢复HTTP随应用注册，不再配置api-enabled；本项目本地事务完成接单、取消和消息，不依赖外部消费者。部署前须具备本期完整schema及正式会话归属上下文。
- `customer-service.handoff-proposal.outbox-enabled`仅控制可选对外Rabbit事件转发，默认false；当前内部接待和访客SSE直接恢复持久事实，无需启用。外部消费者若需要订阅，单独完成幂等与Broker验收。

## 7. 当前验证与剩余工作

2026-11-07 Maven reactor 定向回归：service 38/38、web 18/18、迁移静态契约 2/2 通过。覆盖恢复隔离、直接申请与 AI 确认来源保持、取消同键/并发去重、冲突/越权、Outbox 故障回滚后重试、全范围外键及 Spring 生产构造装配。为绕过既有腾讯云测试缺失类型，仅临时限制目标测试源码编译，验证后已移除过滤；这不是全仓测试通过的证据。

仍需外部唯一 owner 的取消接收/幂等回执/迟到 claim 处理契约、可靠命令消费者、受信回调、终态与 AI 恢复协调，以及真实 MySQL/Kingbase、正式身份和浏览器 E2E。C 端实时卡片与完整消息恢复也未由本接口代替。当前整体状态为 PARTIAL。
