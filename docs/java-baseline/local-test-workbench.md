# 本地 AI 聊天与转人工联调说明（2026-11-09）

## 业务入口与联调入口

访客咨询：/customer-service/visitor.html?assistantConfigId={id}；客服工作：/customer-service/agent.html?assistantConfigId={id}。均由服务端解析组织与大会，不提供Mock身份控件。访客URL自动保留conversationId以支持刷新恢复；客服跟进可回复并在网关受理后显式结案。详见 [followup-response.md](followup-response.md)。

## 使用入口

页面：<http://localhost:8081/ai_chat/test/formal-customer-service-workbench.html>。

本页用于访客端真实 AI 对话、人工申请及进度恢复，并只读查询用户中心维护的组和账号。内部系统仍是账号、组和成员的唯一 owner；本项目负责正式接单、接待和消息。页面使用真实客服登录核验，不创建本地账号/组。

本次已验证的配置：

| 字段              | 值                                     | 来源                       |
| ----------------- | -------------------------------------- | -------------------------- |
| assistantConfigId | `41fd45d2-bf35-4924-b9fc-634d6cbc72d4` | 用户提供的已发布配置       |
| topOrgId          | `3b450000-00f0-5254-27f5-08d6e8b65998` | runtime 接口可信解析       |
| scopeType         | `EVENT`                                | runtime 接口               |
| bventId           | `019d65e5-44d4-7056-8c5f-a66dcc92cc46` | runtime 接口；无需手工填写 |

1. 在同一个浏览器完成开发环境登录。右上角必须显示“已获取登录凭据”；没有登录态时点击“登录 / 重新登录”。无需手工复制 Token。
2. 填写 assistantConfigId，点击“加载运行配置”。服务端配置决定组织、大会和语言。
3. 点击“新建会话”，或者直接发送第一条消息；先完成一轮真实 AI 对话，再申请转人工。
4. 继续发送消息，验证同一 conversation 的多轮记忆；发送请求自动附加 `Authorization: Bearer ...`。
5. 点击“申请转人工”，随后点击“恢复进度”。成功时显示 `REQUESTED`、周期 ID、来源 `VISITOR_DIRECT_REQUEST` 和原因 `USER_REQUESTED`。
6. 未接单时点击“取消等待并恢复 AI”，应为 CANCELLED；已被客服接单时拒绝访客取消。AI 自主建议会显示确认卡，拒绝不建单；聊天中明确请求人工则直接申请。
7. 点击“读取客服组”，在返回组中点击“读取人员”。只读取指定组的成员，空组显示 0 个账号；普通组不等于具备智能客服接待资格。

页面是独立调试页。刷新后重新加载配置，在“恢复已有 conversationId”输入会话可恢复历史、卡片、接待和人工消息。客服测试端使用正式登录读取队列、接单、发送消息和结束；每 2 秒恢复持久消息和对方回执。

## 启动方式与配置范围

Java 21。在仓库根目录构建：

```powershell
mvn --% -o -pl byt-cloud-agent-web -am -DskipTests -Dmaven.test.skip=true package
```

此命令只证明生产代码能够编译、打包；不会编译或执行测试。当前全量测试编译有既有腾讯云 DTO/properties 缺失问题，定向验证见下文。

IDE 或 JAR 启动参数：

```text
--spring.profiles.active=dev,customer-service-test
--server.address=127.0.0.1
--server.port=8081
--spring.cloud.consul.discovery.register=false
```

`customer-service-test` 必须显式激活，不加入共享环境默认 profiles。本次实例只监听本机，并关闭本地实例向共享服务发现注册；仍使用 dev 的数据源、缓存、模型和下游服务发现。需要配置相应开发环境依赖，页面操作会产生真实测试会话和申请记录。

当前内置接待、等待、调查、在线、指标及后台任务均随应用运行。customer-service-test仅提供本机已确认的assistant→group路由，不承载功能开关或Mock授权。普通环境也固定使用用户中心目录，无account-directory.mode选择。

如需使用历史目录查询工具及X-CS-Mock-\*管理头，必须另外激活customer-service-directory-test Profile，仅限开发联调；该Profile不替代真实接单授权。外部通知和Rabbit转发仍默认关闭。配置项见 [configuration.md](configuration.md)。

## 2026-11-09 中断恢复后的最新结果

接单、客服/访客纯文本消息、接收/已读回执、未接单取消、客服结束与 AI 恢复均已有正式接口和联调控件。用户允许模拟客服身份后，新增 `CustomerServiceReceptionFlowTest`，通过真实 HTTP 适配器、准入规则、状态事务及隔离 H2 验证完整接待流程；外部目录和认证是模拟边界，未给 admin 增加正式接单特权。

真实 admin 浏览器已验证：明确请求直接申请；模型建议发送确认卡；确认前不建单；确认后来源正确；取消后继续真实 AI 聊天。还通过丢弃首次成功响应验证消息重试不重复入库。390px 无横向溢出。页面修复旧轮询覆盖新会话、消息重试换键两处问题；API 修复缺 kind 回执错误分类，并显式拒绝未知归属字段。

最新契约与边界见 [reception.md](reception.md)；本地证据 `.codex/qa/customer-service-resume-20261109/`。下文启动修复和早期验收仅作历史记录。

## 本次早期启动修复

- `AiResolutionSignalService` 通过 `@Resource` 注入生产依赖，测试构造器支持固定时钟。用户沉默不再安排期限或触发调查，只有用户明确结束信号参与资格判断。
- `/sessionId` 只生成标识。首次聊天现在由 Conversation owner 同步绑定会话，再建立 generation 和分配 Turn，避免会话主表不存在导致 HTTP 500。
- 页面读取认证脚本实际导出的词法 `authManager`；Token 检查通过后才显示已登录，修复请求漏带 Token 和空登录态误报。
- 安全上下文使用线程隔离；异步任务提交时复制认证身份，通过 TTL 传递业务归属，不再继承已回收 Servlet。任务完成或抛错时恢复工作线程原上下文。

## 验收证据与限制

2026-11-09，本机 Edge、真实开发环境登录、真实模型和开发数据源：

| 项目              | 结果                                                                                                                                                                |
| ----------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 主应用启动        | `dev,customer-service-test`，8081 正常启动                                                                                                                          |
| 两轮 AI 对话      | 第二轮正确回答第一轮的测试代号“青竹七号”                                                                                                                            |
| 回复持久化        | 历史接口 HTTP 200，读回 2 条 user 和 2 条 assistant 消息，状态均 `success`                                                                                          |
| 转人工及恢复      | 同一 episode 为 `REQUESTED`，来源与原因正确                                                                                                                         |
| 取消意图          | `PENDING_OWNER_ACK`，保留申请状态且禁用重复取消                                                                                                                     |
| 用户中心目录      | 最新返回 35 个组；抽查两组的人员接口均正常返回空列表，非空成员展示仍待有数据的组验证                                                                                |
| 桌面及 390px 页面 | 页面可操作，小屏无横向溢出，输入焦点可见                                                                                                                            |
| 定向 Java 回归    | 36 项中 35 通过；启动装配、首聊顺序、异步隔离和目录/转人工 HTTP 契约均通过                                                                                          |
| Java 已有失败     | `eventAssistantUsesCompositeMemoryKeyWithoutChangingSessionId` 期望 `assistant-1:session-1`，现有实现返回 `session-1`；本次未改记忆键生成逻辑，也未删除或弱化该测试 |
| 前端定向回归      | 7/7 通过，包含从词法 authManager 读取 Token 并实际附到 fetch                                                                                                        |
| 构建及差异检查    | 生产打包成功；`git diff --check` 通过                                                                                                                               |

本轮会话：`584f92f1-60d4-4a0f-b194-da62334bc5da`。人工周期：`hde_5287369cba3e4d24996f140344254362`。这些是开发库中的测试事实，未自动清理。

任务本地证据位于 `.codex/qa/customer-service-workbench-live-20261109/`，包括 `multiround.json`、`handoff-r3.json`、`directory-members.json`、截图及 JUnit 报告。初始未登录浏览器报告失败，不能替代此处已登录会话的验收证据。

## 后续验收与尚未接通的能力

2026-11-10当前入口为`/customer-service/visitor.html`和`/customer-service/agent.html`，通过URL指定assistantConfigId，访客URL保留conversationId以刷新恢复。在线/容量/代次心跳/改派/系统策略/指标和邮件最终回复均已有实现及隔离浏览器证据；正式依赖与发布验收见[tasks.md](tasks.md)。下方为旧阶段历史清单，不表示当前仍缺这些仓库能力。

2026-11-09 最新更新：MySQL 5.7 开发库已部署等待和调查增量；本机测试 profile 开启调查与等待，共享配置默认关闭。真实 admin/模型/MySQL 调查闭环 5 项和模拟身份/目录、真实 Tomcat/H2/SSE 双浏览器接待 9 项通过。跟进列表、原周期通知重试及 CONTACT 已接通；本项目完整人工历史门禁不再以 LOCAL_ONLY 阻断。通知发送关闭，EMAIL 保持 PENDING；知识纠错通知不在本期。以下清单为早期历史，现行状态以 [survey-waiting-realtime.md](survey-waiting-realtime.md) 和 tasks.md 顶部为准。

整个 013 状态仍为 **PARTIAL**，本轮接口与 Tool 验证结果见上文。

1. 用组内真实客服账号进行双账号浏览器接待验收；模拟 HTTP/JDBC 只证明规则和事务，不能替代真实身份测试。
2. 人工消息目前每 2 秒轮询持久日志；独立 SSE/WS 订阅、目标 Kingbase、多节点与故障恢复仍待验收。
3. 权威工作日历、T+5/T+10、营业结束及邮件跟进可靠投递仍待接通。
4. AI 结束到单次 Survey 的完整资格、卡片投递、回答及不满意 opt-in 转人工仍未接通，Tool 保持关闭。
5. 正式管理授权、TAPD 原型，以及既有记忆键断言和腾讯云全量测试编译阻断仍需后续处理。
