# 配置与对接

配置按 `appsettings.json`、环境对应 appsettings、本机 `appsettings.Local.json`、环境变量依次覆盖。环境变量以双下划线表示层级，例如 `Model__ApiKey`。所有密钥只在服务端注入，不进入前端配置、日志或数据库任务。

## 认证和助手

| 键                            | 默认值 / 范围                      | 作用域和缺省行为                                           |
| ----------------------------- | ---------------------------------- | ---------------------------------------------------------- |
| `Authentication:Authority`    | 空；OIDC HTTP(S) 地址，生产 HTTPS  | 全应用；有值时加载发现文档和签名密钥，浏览器用于统一登录   |
| `Authentication:Issuer`       | 空；精确 JWT issuer                | 全应用；本地签名校验时必填，必须与登录平台一致             |
| `Authentication:Audience`     | 空；精确 audience                  | 全应用；未配正确受众不能访问业务 API                       |
| `Authentication:SigningKey`   | 空；可选 HS256 密钥，至少 32 字节  | 全应用；仅已有登录系统使用共享签名时配置，不用于签发新身份 |
| `Authentication:TenantClaim`  | `tenantId`；非空声明名             | 身份解析；缺失声明拒绝，最长 64 字符                       |
| `Authentication:AccountClaim` | `accountId`；非空声明名            | 身份解析；缺失声明拒绝，最长 128 字符                      |
| `Authentication:ClientId`     | 空；正式 OIDC 客户端 ID            | 浏览器登录；为空时显示未配置，不自动赋身份                 |
| `Authentication:Scope`        | `openid profile`；平台许可的 scope | 浏览器登录；不从此字段推导客服组权限                       |
| `Assistants:Items`            | 空数组                             | 全应用可信注册表；未知助手拒绝，不能由请求或模型补造       |
| `Assistants:Items:*:Id`       | 必填，非空 ≤128 字符               | 助手引用，同租户唯一                                       |
| `Assistants:Items:*:TenantId` | 必填，非空 ≤64 字符                | 助手的数据租户，必须匹配签名主体                           |
| `Assistants:Items:*:TopOrgId` | 必填，非空 ≤64 字符                | 助手的唯一所属组织                                         |
| `Assistants:Items:*:BventId`  | null；非空时 ≤128 字符             | 可选大会引用，空表示组织助手                               |
| `Assistants:Items:*:Name`     | `智能客服`                         | 助手展示名，不参与权限                                     |
| `Assistants:Items:*:Language` | `zh-CN`                            | 返回给页面的默认语言，不改变身份                           |
| `AllowedHosts`                | `*`                                | 主机过滤；部署时可限定实际服务域名                         |

API 和 WebSocket 均验证 issuer、audience、签名和 exp，时钟容差为 0。生产不提供发放客服 JWT 的接口，也不信任 `X-CS-Mock-*` 头。统一登录客户端需登记实际域名的 `/auth/callback.html` 与 `/auth/silent.html`。

## 用户中心与模型

模型执行框架固定为 **Microsoft Agent Framework 1.28.0**，无需配置开关。现有 `Model:*` 键配置官方 OpenAI provider，生成、分类和工具续轮均通过框架执行，详见 [框架说明](agent-framework.md)。

| 键                         | 默认值 / 范围                       | 作用域和缺省行为                                         |
| -------------------------- | ----------------------------------- | -------------------------------------------------------- |
| `UserCenter:BaseUrl`       | 空；受控 HTTP(S) 根地址             | 外部账号和目录；缺省时明确返回依赖不可用                 |
| `UserCenter:Authorization` | 空；可选服务间认证头                | 仅发送用户中心，不持久化                                 |
| `Model:BaseUrl`            | 空；OpenAI 兼容根地址，通常含 `/v1` | 文本生成与主题分类；未配置不伪造 AI 回答                 |
| `Model:ApiKey`             | 空；可选 Bearer 密钥                | 只在出站 Authorization 使用                              |
| `Model:Model`              | 空；实际模型名称                    | 文本与分类共用；空值拒绝模型调用                         |
| `Model:TimeoutSeconds`     | 120 秒；整数 5—240                  | 单轮流式生成；超时记录失败并登记可靠恢复任务             |
| `Model:SystemPrompt`       | 模板中的中文展会客服提示词          | 全应用；服务端控制，访客不能覆盖；不是原 Java 知识库接入 |

用户中心连接超时 2 秒、请求 5 秒，禁止跟随重定向。依赖既有四个只读接口：

| 方法和路径                                                     | 请求/响应关键约定                                                                |
| -------------------------------------------------------------- | -------------------------------------------------------------------------------- |
| POST `/api/AccountUser/v1/Info`                                | `{accountId}`；验证 returnObj 的 accountId/tenantId                              |
| GET `/api/Organization/v1/GetOrgAccountLiteList?accountId=...` | returnObj 中 orgId 匹配且 status=1（兼容 `ENABLED`）                             |
| POST `/api/accountGroup/v1/list`                               | `OrgId=null, OrganizationId, Search, PageIndex, PageSize, Skip`，保留 PascalCase |
| POST `/api/accountGroup/v1/accountDetails`                     | `AccountGroupId, Search, PageIndex, PageSize, Skip`，先核验组归属                |

四接口要求 `isSuccess=true` 和非空 `returnObj`。目录 pager 必须提供 pageIndex/pageSize/totalPage，游标绑定租户、组织、组、关键词和页大小，拒绝跨范围复用。外部账号组与本地四类客服业务组是不同事实，本地不修改用户中心数据。

主题分类单次最多等待 5 秒，置信度达到 0.75 才采用分类，否则 OTHER。`TimeoutSeconds` 覆盖本次 Agent 运行的全部工具续轮，不是每次 HTTP 单独计时。框架工具循环固定最多 4 次模型响应，最多 8 个工具调用，单工具参数最多 4000 字符；不并行执行工具。SDK 自动重试关闭，业务可靠恢复沿用已有任务机制。

每次模型响应都必须明确 stop 或 tool_calls，Agent 最终必须 stop；缺失结束、length、content_filter 或工具循环耗尽不能伪装成功。AI 输入/回答长度见接口 DTO 和领域校验，不接受模型携带身份或组信息。生成及所有工具续轮出站携带 session/turn/trace 和租户组织大会上下文；独立分类携带完整会话范围。无密钥时不发送占位 Authorization。

## 客服规则和营业日历

| 键                                         | 默认值 / 范围                       | 作用域和缺省行为                           |
| ------------------------------------------ | ----------------------------------- | ------------------------------------------ |
| `CustomerService:Capacity`                 | 5 个；整数 1—100                    | 同租户组织客服跨助手总并发；不自动上线     |
| `CustomerService:ProposalTtlMinutes`       | 10 分钟；1—1440                     | 建议卡有效期，查询和确认时核验             |
| `CustomerService:SurveyTtlMinutes`         | 30 分钟；1—1440                     | 未答调查有效期，过期不计为满意             |
| `CustomerService:Calendar:CalendarVersion` | `cn-office-v1`；非空 ≤64 字符       | 部署缺省日历业务版本；修改规则必须升级版本 |
| `CustomerService:Calendar:Zone`            | `Asia/Shanghai`；合法系统/IANA 时区 | 解释组织本地营业时间，期限保存为 UTC       |
| `CustomerService:Calendar:Opens`           | `10:00`；HH:mm 或 HH:mm:ss          | 含开始边界，同日必须早于 closes            |
| `CustomerService:Calendar:Closes`          | `17:00`；HH:mm 或 HH:mm:ss          | 不含结束边界，不支持跨日营业区间           |
| `CustomerService:Calendar:Overrides`       | 空字典；最多 1000 项，ISO日期→布尔  | 日期覆盖优先，未覆盖时周一至周五营业       |

正式 `/configuration/calendar` 保存的组织日历优先于部署默认，只影响新周期；等待按首次申请时刻冻结窗口。未来最多查找三年营业日，无营业日配置拒绝。等待 5/10 分钟提醒、在线租约 45 秒、客户端在线心跳 15 秒、任务租约 30 秒均为当前实现固定规则，不是可由访客配置的字段。

## 数据源

| 键                          | 默认值 / 范围                                  | 作用域和缺省行为                                                   |
| --------------------------- | ---------------------------------------------- | ------------------------------------------------------------------ |
| `Database:Provider`         | `Sqlite`；Sqlite / MySql / Kingbase            | 应用唯一共享数据源；差异仅在基础设施装配                           |
| `Database:ConnectionString` | `Data Source=data/runtime/customer-service.db` | 必须是本项目独立库；不允许与 Java 共写                             |
| `Database:Initialize`       | true；布尔                                     | 只为空库自动建表；已有表不会升级，生产先执行对应建库脚本后设 false |

连接串示例（仅替换占位符，不能直接连接原库）：

```text
Sqlite:   Data Source=data/runtime/customer-service.db;Default Timeout=5
MySql:    Server=YOUR-HOST;Database=ai_customer_service;User=YOUR-USER;Password=YOUR-SECRET
Kingbase: Host=YOUR-HOST;Port=54321;Database=ai_customer_service;Username=YOUR-USER;Password=YOUR-SECRET
```

MySQL 使用官方 EF Provider，建议 UTF8MB4 和区分大小写的标识符排序规则，版本需 8.0.16+ 强制 CHECK。Kingbase 需目标版本支持 PostgreSQL 协议、DDL 和 Npgsql 类型；默认端口只是示例。当前已生成两种方言脚本，尚未连接对应实库运行。

所有会话事实的主键和 FK 包含 tenant_id/top_org_id；组织门闩与乐观 version 保护并发。表为物理保留，无默认删除任务；会话、邮件正文、邮箱与审计数据的保留周期由部署方设定，归档前必须保证任务终结和引用完整。各字段含义见 [字典](schema.md)。

## 可靠通知

| 键                             | 默认值 / 范围               | 作用域和缺省行为                                             |
| ------------------------------ | --------------------------- | ------------------------------------------------------------ |
| `Notifications:Enabled`        | false；布尔                 | 外发 EMAIL/EMAIL_REPLY/INTERNAL 总开关；关闭不领取、不耗次数 |
| `Notifications:Endpoint`       | 空；受控 HTTP(S) 网关       | 通知和最终邮件；空地址不发送                                 |
| `Notifications:Authorization`  | 空；可选服务间认证头        | 网关认证，不写入任务或日志                                   |
| `Notifications:WeComWebhook`   | 空；官方 HTTPS 群机器人 URL | 只发 INTERNAL，优先于普通网关，不包含邮箱正文                |
| `Notifications:EventsEnabled`  | false；布尔                 | 可选 EVENT 审计转发；不影响本地接待                          |
| `Notifications:EventsEndpoint` | 空；受控 HTTP(S) 事件网关   | 开启且有地址才领取 EVENT                                     |

HTTP 网关连接 2 秒、请求 5 秒。请求含 `Idempotency-Key: eventKey` 和 traceId，JSON 包含 eventKey、kind、sessionId、episodeId、tenantId、topOrgId、bventId、traceId、payload。网关必须持久去重并返回 `{"accepted":true}`；HTTP 200 本身不算受理。

EMAIL 为建工单通知，payload 含 ticketId/conversationId，由网关根据组织路由找到内部接收者；EMAIL_REPLY 的 payload 含服务端原邮箱、subject、content、ticketId，网关按原邮箱发给访客；INTERNAL 为等待提醒；EVENT 为可选业务审计。不得把缺失接收人规则当成成功发信。

企微仅允许 `https://qyapi.weixin.qq.com/cgi-bin/webhook/send`，必须返回 `errcode=0`；官方机器人不保证事件键去重，可能重复提醒。需要严格去重时使用自有持久幂等网关。任务最多自动尝试 5 次，DEAD 需工作台人工恢复；关闭通道只暂停发送，不丢任务。
