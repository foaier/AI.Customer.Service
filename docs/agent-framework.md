# Microsoft Agent Framework 接入

本项目使用 **Microsoft Agent Framework 1.28.0 稳定版**，目标框架为 **.NET 10**。Core 项目引用 `Microsoft.Agents.AI.OpenAI`，传递依赖包括 `Microsoft.Agents.AI 1.28.0`、`Microsoft.Extensions.AI.OpenAI 10.11.0` 和 `OpenAI 2.14.0`。框架负责实际 Agent 运行、流式输出、函数调用与工具结果续轮。

官方资料：[Agent Framework 仓库](https://github.com/microsoft/agent-framework)、[OpenAI provider 的 C# 文档](https://learn.microsoft.com/en-us/agent-framework/agents/providers/openai?pivots=programming-language-csharp)、[1.28.0 NuGet 包](https://www.nuget.org/packages/Microsoft.Agents.AI.OpenAI/1.28.0)。

## 执行路径

`ChatService → IChatModel → AgentFrameworkChatModel → ChatClientAgent → FunctionInvokingChatClient → AgentResponseGuard → 官方 OpenAI provider → 模型端点`。

- `AgentFrameworkChatModel`：使用 `ChatClientAgent.RunStreamingAsync` 执行客服，使用 `RunAsync` 执行主题分类；框架返回的文本映射为原 SSE 业务增量。
- `AgentProposalTools`：通过 `AIFunctionFactory.Create` 注册实际可执行函数；MAF 负责绑定参数、执行、生成工具结果并继续请求模型。
- `AgentResponseGuard`：在每次模型响应进入工具执行前检查结束证据、工具名称、参数白名单、参数类型及数量。它装饰官方类型化客户端，不自行解析 HTTP/SSE。
- `ModelContextPolicy`：官方 SDK 管线中的请求策略；每次生成和工具续轮显式传播租户、组织、助手、会话、轮次、trace 与大会。
- `Program.cs`：通过 `IHttpClientFactory` 装配真实 MAF Adapter，连接池共享，但身份头和 session 状态按运行创建。

`IChatModel` 仍是稳定领域端口，Domain 和客服应用编排不依赖框架类型。数据库、会话状态和人工业务 owner 保持原 Java 拆分逻辑。

## Session 与业务提交

每次 Agent 运行创建独立 `AgentSession`，显式传入同租户、组织、会话、助手、大会的最多 20 个成功轮次。失败历史不进入上下文，框架 session 不跨请求保存第二份会话事实；唯一持久历史仍由 Conversation owner 管理。

工具函数无 Repository、DbContext 或外部投递依赖，只保存当前运行的内存候选：

| 工具                | 框架执行结果                                 | 业务提交规则                                        |
| ------------------- | -------------------------------------------- | --------------------------------------------------- |
| `proposeHandoff`    | 校验并记录人工建议候选，返回 `PROPOSAL_ONLY` | 完整生成与 epoch 围栏成功后创建建议卡，仍需访客确认 |
| `proposeResolution` | 校验并记录可能解决候选，返回 `PROPOSAL_ONLY` | 成功轮次提交后按业务阈值保存，不代替用户结束证据    |
| `requestHandoff`    | 返回 `USER_CONSENT_REQUIRED`，不记录申请     | 明确原话授权或访客按钮由应用入口独立处理            |

框架将这些实际工具结果送回模型继续生成。仅最终正常 stop 时，Adapter 才输出候选给 `ChatService`；应用层先提交真实成功轮次，再按旧有 owner 接口和 epoch 围栏处理候选。取消、模型截断、续轮失败或人工接管后迟到的响应无法直接写入业务状态。

## 固定运行约束

模型配置继续使用 `Model:BaseUrl/ApiKey/Model/TimeoutSeconds/SystemPrompt`，含默认值、范围与缺省行为的说明见 [配置文档](configuration.md)。当前 provider 使用 OpenAI 兼容 Chat Completions 端点，不要求原 Java 进程。

- 客服单轮总超时默认 120 秒，可配置 5—240 秒，包含全部工具续轮；分类固定 5 秒。
- MAF 工具循环最多 4 次模型响应，不并行调用；每次运行最多 8 个工具调用，单次参数最多 4000 字符。
- 不向模型回传工具执行异常明细，工具连续错误上限为 1；SDK 自动 HTTP 重试为 0，最终故障由业务可靠恢复链路处理。
- 每次模型响应必须 stop 或 tool_calls；最终 Agent 必须 stop。缺失结束、长度截断、内容过滤及循环耗尽明确失败。
- 正文累计最多 65535 字符；模型工具不得携带额外身份字段，分类只接受 category/confidence 白名单。
- 缺失端点或模型名时明确报依赖不可用；未配置密钥的兼容端点不发送 SDK 占位 Authorization。

## 验证依据

`AgentFrameworkTests` 使用真实 MAF Agent、官方 SDK 和 HTTP 消息处理器验证工具调用与第二轮结果、授权边界、成功/失败提交、上下文隔离、取消、循环上限及分类。两项联动测试贯穿签名 JWT、MVC、MAF 与独立 SQLite，确认成功只建建议、失败登记可靠任务。

`ProtocolTests` 验证 SDK 跨帧工具参数恢复和截断拒绝；Edge 浏览器流程使用正式 DI 装配的 MAF Adapter，只有外部模型 HTTP 服务由测试端点替代。全部业务与协议测试 **50 项通过**，浏览器 **4 组通过**，见 [验证记录](verification.md)。真实模型供应商、OIDC、用户中心及邮件网关仍需配置后联调。
