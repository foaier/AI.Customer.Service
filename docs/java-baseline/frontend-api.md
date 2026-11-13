# 客服前端对接 API（2026-11-11，11-12验证更新）

本文按当前实现整理，覆盖分组/人员、营业时间、AI、转人工、接单聊天、离线留言、满意度和导出。验收状态及外部依赖限制见 [本轮验收报告](requirements-e2e-20261111.md)。历史文档中的 `/v2`、人工 SSE、额外 realtime ticket 和异步取消确认不作为本次接入协议。

## 1. 公共约定与调用顺序

- 本地服务根地址：`http://localhost:8081/ai_chat`。以下 `/api/...` 均附在该根地址后。
- HTTP 携带现有登录 `Authorization: Bearer <登录令牌>`；JSON 命令带 `Content-Type: application/json`。不在 URL 传令牌，不新增客服票据。
- 当前 C 端入口复用正式登录与会话关系授权；本轮未实现匿名凭据。客服必须是目标组织启用账号且在本地业务组内。登录后即可手动接单，不要求上线或心跳。
- 当前组织 `3b450000-00f0-5254-27f5-08d6e8b65998`；验收助手 `41fd45d2-bf35-4924-b9fc-634d6cbc72d4`；展会 `019d65e5-44d4-7056-8c5f-a66dcc92cc46`。其他助手从运行配置取得归属。
- 客服接口的 `topOrgId` 是待校验的目标组织；租户、操作者、访客、组和会话归属由服务端解析。不要向命令增加 `tenantId/actorId/groupId/senderId`。
- 常规客服响应为 `{"code":"OK","data":...}`，失败为 `{"code":"STATE_CONFLICT","message":"..."}`。申请/建议接口还含 `requestId/message/retryable/serverTime`，应同时判断 HTTP 状态和业务 code。AI 平台接口保留原 `returnObj` 信封。
- 时间为 ISO UTC 字符串；营业时间使用配置的 IANA 时区。渲染消息用纯文本，不使用 `innerHTML`。
- 首次发命令前保存 `requestId/clientMsgId` 及原 body。网络重试复用原键与原版本；收到 409 先恢复最新状态，不自动更换键循环提交。接待版本、等待版本、表单版本、回复版本、调查版本各自独立。

建议接入顺序：管理员读取配置→选择组织人员→保存组及助手映射→保存营业时间；访客读取助手运行配置→创建会话→AI聊天→明确申请/确认建议→订阅状态；客服订阅队列→选择接待→认领→读取AI上下文→双方消息/回执→结束；非营业或选择邮件跟进→提交留言→客服认领→回复；AI明确结束后显示调查→提交评价→需要时再次询问人工；管理页面分页下载聊天及评价。

## 2. 业务组与人员

前缀 `/api/customer-service/configuration`，均需正式登录且在目标组织启用。配置管理权与接单资格分别校验。

| 方法与后缀                                                             | 参数/用途                                                                                                    |
| ---------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------ |
| `GET ?topOrgId={org}`                                                  | 读取完整配置，未保存时 version=0，返回四个默认组                                                             |
| `GET /directory/groups?topOrgId={org}&limit=100`                       | 用户中心现有组织分组；可加 keyword、afterCursor                                                              |
| `GET /directory/accounts?topOrgId={org}&groupId={用户中心组}&limit=50` | 读取成员候选；可加 keyword、afterCursor；只含 accountId/displayName                                          |
| `PUT ?topOrgId={org}`                                                  | 整体保存以下结构，返回新版本；旧版本409                                                                      |
| `POST /preview?topOrgId={org}`                                         | `{assistantConfigId,context}`，context为1—16000字符；返回 classification、groupId、configVersion，不创建申请 |

保存结构（组ID先用GET取得，下面仅为字段示意）：

```json
{
    "version": 0,
    "groups": [
        {
            "id": "exhibitor-group",
            "category": "EXHIBITOR",
            "name": "展商组",
            "members": [{ "accountId": "组织内有效账号", "displayName": "展示名" }]
        },
        { "id": "service-group", "category": "VISITOR", "name": "客服组", "members": [] },
        { "id": "media-group", "category": "MEDIA", "name": "媒体组", "members": [] },
        { "id": "other-group", "category": "OTHER", "name": "其他组", "members": [] }
    ],
    "routes": [
        {
            "assistantConfigId": "助手ID",
            "groups": {
                "EXHIBITOR": "exhibitor-group",
                "VISITOR": "service-group",
                "MEDIA": "media-group",
                "OTHER": "other-group"
            }
        }
    ]
}
```

四类枚举固定：`EXHIBITOR`展商、`VISITOR`观众/客服、`MEDIA`媒体、`OTHER`其他/不确定。展示名称可设置，不能把“客服组”作为新的枚举值。分类依据当前咨询主题和多轮上下文，不依据访客固定身份；不确定、低置信度或模型故障降 OTHER。分类只决定类别，目标组由保存的助手映射决定，并在申请时冻结；修改配置不会迁移历史申请。

用户中心组只是人员候选来源，与本地四类业务组不同；保存时可关联本组织任意经服务端实时核验有效的账号，同一人可加入多个业务组。`displayName` 不授予权限，服务端重新读取账号名称。目录返回 `status/groups或candidates/nextCursor/hasMore`，需处理不可用和分页，不能把不可用当空成员覆盖保存。

## 3. 正式营业时间

前缀 `/api/customer-service/configuration/calendar?topOrgId={org}`：

- `GET`：读取策略及数据库 version。未保存用部署缺省，version=0。
- `PUT`：完整保存下例；返回新 version。修改时间、时区或日期覆盖必须更换 calendarVersion。
- `POST /api/customer-service/configuration/calendar/preview?topOrgId={org}`：body `{calendar:完整草稿,at:"2026-11-11T10:00:00+08:00"}`，返回 `{open,nextOpensAt,closesAt,version}`；此处 version 为业务日历版本。

```json
{
    "version": 1,
    "calendarVersion": "office-20261111-v2",
    "zone": "Asia/Shanghai",
    "opens": "10:00",
    "closes": "17:00",
    "overrides": { "2026-11-14": true, "2026-11-16": false }
}
```

默认周一至周五；日期覆盖优先，最多1000项。开闭门支持 `HH:mm` 或 `HH:mm:ss`，同日开门早于闭门，区间左闭右开，不支持跨午夜。业务版本1—64字符。正式设置持久化、按租户/组织隔离，重启保留，只影响新初始化等待周期；已有周期保持冻结的UTC期限。无需启用 `customer-service-test`。`/api/customer-service/test-configuration` 是独立进程级联调沙箱，前端业务设置不要调用。

## 4. AI 会话与流式聊天

1. `GET /api/assistant/config/getRuntimeConfig?assistantConfigId={assistant}` 获取可信运行配置；按原平台信封读取 topOrgId、bventId、language 等。
2. `GET /api/agent/sessionId` 获取 `returnObj.sessionId`，作为后续 conversationId。
3. `POST /api/agent/chatStream`，额外头 `Accept: text/event-stream`、`x-currentOrgId:{org}`、`x-lang:{language}`；展会助手带 `x-bventId:{event}`。

```json
{
    "sessionId": "原会话ID",
    "userInput": "请问如何办理参展证？",
    "businessType": "event-assistant",
    "extensions": { "event-assistant": { "assistantConfigId": "助手ID", "bventId": "展会ID" } },
    "isReconnect": false,
    "speechEnabled": false
}
```

使用 fetch/ReadableStream 解析 SSE，支持任意网络分块、CRLF、多行data和结尾残帧。当前文本patch以 JSON 的 `v` 字段传递；`event:card` 是独立控制卡，不能拼入回答正文。`handoff.proposed` 提示需要用户确认的建议；`handoff.requested` 表示申请已记录。忽略心跳等非文本事件，`event:error` 应显示失败并恢复持久状态，不能仅凭HTTP200认定整轮成功。流结束后用恢复接口核对人工/调查状态。

恢复AI展示历史：`GET /api/agent/history_detail?sessionId={id}&businessType=event-assistant&assistantConfigId={assistant}`。AI与人工沿用同一会话ID；人工消息另按第7节补齐。人工REQUESTED/CLAIMED/ACTIVE期间，访客新消息发人工HTTP接口，不再调用AI生成。

## 5. 申请、建议、取消与状态

前缀 `/api/customer-service/conversations/{id}`：

| 方法与后缀                                      | 请求/结果                                                                                                             |
| ----------------------------------------------- | --------------------------------------------------------------------------------------------------------------------- |
| `POST /handoffs`                                | `{requestId}`；明确按钮或明确原话直接申请，不再二次确认；成功 code=REQUESTED，data含episodeId/status/version/replayed |
| `GET /handoffs/latest`                          | `data.episode`，无申请为null；含episodeId、triggerSource、reasonCode、status、version、requestedAt                    |
| `GET /reception`                                | 已有周期的当前接待快照；无周期可能403，应使用latest判断空状态                                                         |
| `POST /reception/cancel`                        | `{requestId,expectedVersion}`；仅原访客取消REQUESTED，原子恢复AI                                                      |
| `GET /handoff-proposals/pending`                | `data.proposals`，最多20张有效建议卡                                                                                  |
| `POST /handoff-proposals/{proposalId}/decision` | `{requestId,expectedProposalVersion,decision:"CONFIRM或DISMISS"}`；CONFIRM才申请，DISMISS只记录拒绝                   |

明确原话“转人工/找真人”由服务端确定性处理，分类模型失败仍可申请到 OTHER。AI建议卡不能自动确认；满意度不满意也不能自动申请。

接待状态：`REQUESTED`待接单→`CLAIMED`已认领→`ACTIVE`客服已首响→`RESOLVED`结束；`REQUESTED`也可→`CANCELLED`。`REQUESTED`不能显示“客服已接入”。结束或取消后恢复AI，同一会话可有新的人工周期；操作跟进工单时必须保留原episodeId。

## 6. 客服队列、认领与结束

前缀 `/api/customer-service/agents`，所有下列接口附 `topOrgId={org}`：

| 方法与路径                                                | 用途                                                                          |
| --------------------------------------------------------- | ----------------------------------------------------------------------------- |
| `GET /queue?assistantConfigId={assistant}&topOrgId={org}` | `{groupId,episodes}`；仅当前有资格组的待接单及本人接待，最多100条             |
| `GET /conversations/{id}/reception`                       | 读取完整接待快照                                                              |
| `POST /conversations/{id}/claim`                          | `{requestId,expectedVersion}`；同版本竞争只有一个成功，未上线也可在容量内接单 |
| `GET /conversations/{id}/history`                         | 仅当前认领者可读接待前AI最小文本历史                                          |
| `POST /conversations/{id}/resolve`                        | `{requestId,expectedVersion}`；仅当前认领者结束CLAIMED/ACTIVE；重试原body     |

接待快照示意：

```json
{
    "scope": {
        "tenantId": "服务端租户",
        "topOrgId": "组织ID",
        "conversationId": "会话ID",
        "assistantConfigId": "助手ID",
        "bventId": "展会ID"
    },
    "episodeId": "hde_x",
    "status": "CLAIMED",
    "version": 2,
    "principalId": "原访客",
    "agentId": "认领客服",
    "groupId": "冻结组",
    "requestedAt": "2026-11-11T02:00:00Z"
}
```

组员未认领可看摘要，不能看其他客服认领的正文。每次HTTP重新核验账号及组资格，撤权后拒绝。组织下默认容量5个并发接待（以部署配置为准）。

## 7. 人工消息、回执与刷新恢复

访客前缀 `/api/customer-service/conversations/{id}`；客服前缀 `/api/customer-service/agents/conversations/{id}`，客服附 `topOrgId={org}`。

| 方法与后缀                           | 请求/结果                                                                  |
| ------------------------------------ | -------------------------------------------------------------------------- |
| `POST /messages`                     | `{"clientMsgId":"稳定键","content":"1—4000字符纯文本"}`，返回首次持久消息  |
| `GET /messages?afterSeq=0&limit=100` | `{messages,receipt,peerReceipt}`；按seq增量，单页最多100                   |
| `POST /receipts`                     | `{"kind":"DELIVERED或READ","seq":2}`，只推进当前主体水位，不能确认未来消息 |

消息字段：`seq,messageId,episodeId,senderRole,senderId,clientMsgId,content,createdAt`，senderRole为VISITOR或AGENT。seq在整个会话内递增，跨人工周期不重置。前端按seq/messageId去重；发送成功仅代表已保存，不能显示对方已读。`receipt`是本人的deliveredSeq/readSeq，`peerReceipt`是对方的水位；READ同时推进DELIVERED，旧水位不回退。

刷新后先恢复AI展示历史和接待状态，再从0或本地已展示游标补齐人工消息。结束后新人工消息409；之前已成功消息的同键同内容重试仍返回原回执。客户端将内容修改后复用同一键会409。

## 8. 人工实时 WebSocket

地址：`ws://localhost:8081/ai_chat/api/customer-service/realtime/ws`；HTTPS部署选同源WSS。建立后5秒内发送唯一认证首帧：

```json
{
    "authorization": "Bearer <登录令牌>",
    "agent": false,
    "conversationId": "会话ID",
    "topOrgId": "组织ID",
    "assistantConfigId": "助手ID",
    "afterSeq": 0
}
```

- 访客 agent=false，conversationId必填，归属由会话owner决定。
- 客服 agent=true，topOrgId/assistantConfigId必填；conversationId为空订阅本人队列及跟进列表，非空订阅当前接待。
- 令牌只在首帧，后续不能发送更换订阅/游标帧；切会话关闭旧连接并新建。不能发送自报tenant/principal等额外身份字段。
- 服务端帧 `{"type":"snapshot","data":...}`：队列快照含queue/followups；客服会话含reception/timeline；访客含episode/waiting/survey/proposals，可选timeline。timeline结构同消息GET。
- 快照可覆盖状态，消息按seq去重，展示后通过HTTP明确回执。切会话要用本地代次拦截迟到的认证、快照、历史及回执。
- 15秒心跳；客户端35秒无帧重连，1—30秒退避，携带最后已处理seq。1008表示认证/权限不满足，先恢复登录及权限，避免无限重连。JWT到期关闭。
- 不使用旧人工SSE `/events`（已删除），不增加两秒HTTP轮询。当前服务器每秒按连接读取本地变化，账号/组织每60秒复核，属于有界服务器查询，并非数据库零轮询。

## 9. 等待与离线留言

`GET /api/customer-service/conversations/{id}/waiting` 返回：

```json
{
    "code": "OK",
    "data": {
        "state": "NEED_EMAIL",
        "version": 2,
        "closesAt": "2026-11-11T09:00:00Z",
        "nextOpensAt": "2026-11-12T02:00:00Z",
        "ticketId": null,
        "deliveries": [],
        "contactCollected": false
    }
}
```

等待状态：WAITING等待、BUSY繁忙、CONTINUING继续等待、NEED_EMAIL非营业待留言、FOLLOW_UP已建工单、ACCEPTED已接单、CANCELLED取消。非营业或闭班在当前会话读取/操作时取消未接单申请并恢复AI；已有接待不会因下班取消。当前没有全库等待扫描，无人访问时不保证截止瞬间主动通知。在线但暂无客服接单也可主动留言，不需等待10分钟。

保留选择入口 `POST /conversations/{id}/waiting`，body `{expectedVersion,action,email}`：CONTINUE继续等待、CONTACT保存邮箱但继续在线等待、EMAIL转邮件跟进。需要留言正文时优先用以下完整表单：

`POST /api/customer-service/conversations/{id}/followup-form`

```json
{
    "requestId": "form-stable-id",
    "expectedVersion": 2,
    "email": "visitor@example.com",
    "contactName": "称呼（可空）",
    "content": "请在上班后协助处理的问题"
}
```

expectedVersion来自waiting，称呼最多100字符，正文1—4000字符，邮箱必填。选择邮件跟进、创建唯一工单和留言在同一事务内；同键同内容重放，异键或异内容409。邮箱由等待owner保存，不包含在留言表单响应内。

`GET /api/customer-service/conversations/{id}/followup-form` 供原访客恢复：无表单为`data:{form:{}}`；有表单返回以下完整结构。客服认领前返回下节的摘要。

```json
{
    "scope": {
        "tenantId": "服务端租户",
        "topOrgId": "组织",
        "conversationId": "会话",
        "assistantConfigId": "助手",
        "bventId": "展会"
    },
    "episodeId": "原周期",
    "ticketId": "ticket_x",
    "requestId": "提交键",
    "contactName": "称呼",
    "content": "留言正文",
    "state": "OPEN",
    "version": 1,
    "assignedTo": null,
    "claimRequestId": null,
    "submittedAt": "2026-11-11T09:01:00Z",
    "claimedAt": null
}
```

前端保留ticketId与原episodeId供后续处理。若用户刷新/切会话，不要把旧留言迟到响应回填到新会话。

## 10. 上班认领留言、回复与结案

`GET /api/customer-service/agents/followups?topOrgId={org}&assistantConfigId={assistant}` 返回最多100条工单摘要，含scope、episodeId、ticketId；列表无邮箱和留言正文。

以下前缀为 `/api/customer-service/agents/conversations/{id}/followups/{原episodeId}`，均附 `?topOrgId={org}`：

| 方法与后缀    | 用途                                                                                                    |
| ------------- | ------------------------------------------------------------------------------------------------------- |
| `GET /form`   | 未认领/非本人认领仅`ticketId,state,version,assignedTo,submittedAt`；本人认领后返回完整表单              |
| `POST /claim` | `{requestId,expectedVersion}`，版本来自/form；OPEN→CLAIMED，同键本人重试安全；其他人/异键409            |
| `GET`         | 读取回复`ticketId,state,version,subject,content,attempts`；新留言必须先认领                             |
| `POST /reply` | `{requestId,expectedVersion,subject,content}`；主题1—200字符、不能换行，正文1—10000字符；保存后不可修改 |
| `POST /retry` | `{requestId,expectedVersion}`；仅DEAD回复重置投递，沿用原内容及投递键                                   |
| `POST /close` | `{requestId,expectedVersion}`；仅网关已受理SENT可结案                                                   |

认领只需当前正式登录和原组权限，无需上线、心跳或到点扫描；修改工作时间不会自动分派留言。认领后只能本人回复，历史无独立表单的邮件工单沿用原组权限规则。

**回复状态与表单认领状态分开：** 回复OPEN尚未提交（version=1），PENDING等待发送，CLAIMED投递处理中，SENT网关已受理，DEAD自动重试耗尽，CLOSED已结案。表单仍保持CLAIMED以保留认领事实，不用其状态判断邮件是否发送。PENDING/CLAIMED不能展示已送达或允许结案。当前验收关闭外发，因此真实邮件送达与SENT→CLOSED未在共享环境执行。

## 11. AI 满意度调查

前缀 `/api/customer-service/conversations/{id}/survey`：

| 方法与后缀                        | 请求/用途                                                                            |
| --------------------------------- | ------------------------------------------------------------------------------------ |
| `GET`                             | `data.survey`为空或`surveyId,status,handoffChoice,version,expiresAt`；读取不创建问卷 |
| `POST /{surveyId}/receipt`        | 页面展示后的显式回执，body可空，重复安全                                             |
| `POST /{surveyId}/answer`         | `{expectedVersion,answer:"SATISFIED或DISSATISFIED"}`                                 |
| `POST /{surveyId}/handoff-choice` | `{expectedVersion,choice:"OPTED_IN或DECLINED"}`；仅不满意后可选                      |

调查仅在有效AI回答、用户明确结束信号及完整资格门禁满足后产生；不根据用户沉默、人工结束或取消生成。一次会话最多一份。状态OPEN/SATISFIED/DISSATISFIED/EXPIRED；无回复和过期不能计为满意。满意显示感谢；不满意先记评价，再询问“需要人工/暂不需要”。OPTED_IN才创建同会话人工周期，来源固定SURVEY_DISSATISFIED_OPT_IN；DECLINED只保存评价。同参重试可恢复，反向修改已提交答案409。新请求用最近版本，重试保留首次版本。

## 12. 聊天与评价导出

`GET /api/customer-service/exports/conversations?topOrgId={org}&assistantConfigId={assistant}&page=1&size=20`

需目标组织配置管理权限及助手归属校验；page从1开始（≤100000），size为1—100。成功Content-Disposition为分页JSON附件，body仍为 `{"code":"OK","data":...}`；前端可下载data或完整信封，需与导入方约定。失败不返回attachment头。

```json
{
    "exportedAt": "2026-11-11T10:00:00Z",
    "topOrgId": "组织",
    "assistantConfigId": "助手",
    "page": 1,
    "size": 20,
    "hasMore": false,
    "conversations": [
        {
            "conversationId": "会话",
            "bventId": "展会",
            "aiMessages": [{ "id": "消息ID", "role": "user", "content": "原始问题" }],
            "humanMessages": [],
            "humanMessagesTruncated": false,
            "surveyId": "survey_x",
            "surveyStatus": "SATISFIED",
            "handoffChoice": "NOT_APPLICABLE",
            "respondedAt": "2026-11-11T09:59:00Z"
        }
    ]
}
```

AI仅含可展示user/assistant文本，不导出系统提示词、requestDto或认证凭据；人工消息按seq排序，结构见第7节。两类数组分别保留顺序，不声称合并绝对时间。无调查字段为null。每会话人工最多10000条，`humanMessagesTruncated=true`时需按消息分页补齐；留言正文和邮箱不在本导出范围。`hasMore=true`继续page+1，恰好满页时最后可能多请求一个空页；不要把第一页当作全量。当前导出为JSON，未提供XLSX/CSV格式。

## 13. 可选在线状态与同组改派

手动接单不依赖这些接口。确需展示在线或接受同事改派时，使用 `/api/customer-service/agents`：

| 方法与路径                                         | 额外参数/body                                                                        |
| -------------------------------------------------- | ------------------------------------------------------------------------------------ |
| `GET /availability`                                | query topOrgId、assistantConfigId；返回status/expiresAt/activeCount/capacity/version |
| `POST /availability`                               | 同query，body `{status:"ONLINE或AWAY或OFFLINE"}`；显式选择推进代次                   |
| `POST /availability/heartbeat`                     | 同query，body `{version:本次ONLINE返回版本}`，不得用旧页面续约覆盖下线               |
| `GET /transfer-candidates`                         | 同query，可加afterCursor，返回候选与分页信息                                         |
| `POST /conversations/{id}/transfer?topOrgId={org}` | `{requestId,expectedVersion,targetAccountId}`；同组目标须在线且有容量                |

改派后旧客服清空正文/停止旧订阅，目标客服按最新接待加载。2026-11-12已用两个独立真实账号验证并发接单200/409、正反改派、离线目标409、原键重试、旧客服HTTP403/Socket1008及新客服刷新恢复，详见验收报告。

## 14. 错误处理与可用入口

| HTTP / code                                      | 前端行为                                                      |
| ------------------------------------------------ | ------------------------------------------------------------- |
| 400 / INVALID_ARGUMENT                           | 修正字段、邮箱、时间、页码；不自动重复提交                    |
| 401或认证过滤器错误                              | 恢复现有平台登录，不签发新客服票据                            |
| 403 / FORBIDDEN                                  | 停止读取/命令，清除无权正文，核对组织、组和认领者             |
| 409 / STATE_CONFLICT                             | 恢复对应版本/状态；保留原提交供确认，不自动换键抢占           |
| 503 / SCHEMA_NOT_READY                           | 后端缺本期结构，执行对应方言迁移后重试；不要显示空配置        |
| 503 / DEPENDENCY_UNAVAILABLE或INTERNAL_RETRYABLE | 保留原幂等键，先恢复状态再决定重试；模型分类故障另有OTHER降级 |

开发入口：`/customer-service/debug.html`选择助手，`routing.html`设置与导出，`visitor.html`访客，`agent.html`客服。业务链接应附assistantConfigId；routing附topOrgId。正式前端可复用接口，页面角色只控制展示，不能替代服务器授权。
