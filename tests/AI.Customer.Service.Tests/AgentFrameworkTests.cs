using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AI.Customer.Service.Common;
using AI.Customer.Service.Core.Adapters;
using AI.Customer.Service.Core.Models;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;
using AI.Customer.Service.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AI.Customer.Service.Tests;

/// <summary>通过官方 SDK 的真实 HTTP 序列化和 MAF 运行，验证工具执行、结束门禁及会话隔离。</summary>
[TestClass]
public sealed class AgentFrameworkTests
{
    /// <summary>模型申请工具实际执行拒绝结果，不能生成业务申请或冒充用户同意。</summary>
    [TestMethod]
    public async Task ModelCannotAuthorizeHandoff()
    {
        var requests = 0;
        using var http = new HttpClient(new ProtocolHandler(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            requests++;
            if (requests == 1)
            {
                return ToolResponse("requestHandoff", "{}");
            }
            Assert.AreEqual(2, requests);
            var tool = body.RootElement.GetProperty("messages").EnumerateArray().Single(x => x.GetProperty("role").GetString() == "tool");
            StringAssert.Contains(tool.GetProperty("content").GetString(), "USER_CONSENT_REQUIRED");
            return Answer();
        }));
        var deltas = await CollectAsync(Model(http), Input());
        Assert.AreEqual(2, requests);
        Assert.IsTrue(deltas.All(x => x.ToolName is null));
        Assert.AreEqual("完整答复", string.Concat(deltas.Select(x => x.Text)));
    }

    /// <summary>合法建议只在最终回答成功后输出，模型工具本身不持久化人工状态。</summary>
    [TestMethod]
    public async Task HandoffProposalIsReleasedOnlyAfterSuccessfulContinuation()
    {
        var requests = 0;
        using var http = new HttpClient(new ProtocolHandler(_ => Task.FromResult(++requests == 1
            ? ToolResponse("proposeHandoff", """{"reasonCode":"KNOWLEDGE_GAP","confidence":0.9,"userFacingReason":"需要专业客服核对"}""") : Answer())));
        var deltas = await CollectAsync(Model(http), Input());
        Assert.AreEqual(2, requests);
        Assert.AreEqual("完整答复", deltas[0].Text);
        Assert.AreEqual("proposeHandoff", deltas[^1].ToolName);
    }

    /// <summary>工具已在内存执行但续轮失败时不输出任何候选，确保失败轮次无法触发业务状态。</summary>
    [TestMethod]
    public async Task FailedContinuationDiscardsExecutedCandidates()
    {
        var requests = 0;
        var emitted = new List<ModelDelta>();
        using var http = new HttpClient(new ProtocolHandler(_ => Task.FromResult(++requests == 1
            ? ToolResponse("proposeResolution", "{\"confidence\":0.95}") : Stream("""{"index":0,"delta":{},"finish_reason":"content_filter"}"""))));
        await Assert.ThrowsExactlyAsync<BusinessException>(async () =>
        {
            await foreach (var delta in Model(http).StreamAsync(Input(), default))
            {
                emitted.Add(delta);
            }
        });
        Assert.AreEqual(2, requests);
        Assert.IsTrue(emitted.All(x => x.ToolName is null));
    }

    /// <summary>模型多带租户字段、未知工具、损坏 JSON 或过大参数时必须在自动绑定之前拒绝。</summary>
    [TestMethod]
    [DataRow("proposeResolution", "{\"confidence\":0.95,\"tenantId\":\"other\"}")]
    [DataRow("deleteConversation", "{}")]
    [DataRow("proposeResolution", "{")]
    [DataRow("proposeResolution", "{\"confidence\":\"forged\"}")]
    public async Task InvalidToolCannotReachAutomaticInvocation(string name, string arguments)
    {
        var requests = 0;
        using var http = new HttpClient(new ProtocolHandler(_ =>
        {
            requests++;
            return Task.FromResult(ToolResponse(name, arguments));
        }));
        await Assert.ThrowsExactlyAsync<BusinessException>(() => CollectAsync(Model(http), Input()));
        Assert.AreEqual(1, requests);
    }

    /// <summary>工具参数值非法时返回受限错误，保留完整主回答且不产生解决候选。</summary>
    [TestMethod]
    public async Task InvalidConfidenceProducesNoCandidate()
    {
        var requests = 0;
        using var http = new HttpClient(new ProtocolHandler(async request =>
        {
            if (++requests == 1)
            {
                return ToolResponse("proposeResolution", "{\"confidence\":2}");
            }
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            StringAssert.Contains(body.RootElement.GetProperty("messages").ToString(), "INVALID_PROPOSAL");
            return Answer();
        }));
        Assert.IsTrue((await CollectAsync(Model(http), Input())).All(x => x.ToolName is null));
        Assert.AreEqual(2, requests);
    }

    /// <summary>同一个 Adapter 可并发运行；每次框架 session 只读本范围成功历史，所有续轮保留上下文。</summary>
    [TestMethod]
    public async Task AgentSessionsAndHeadersStayIsolated()
    {
        using var http = new HttpClient(new ProtocolHandler(async request =>
        {
            var session = request.Headers.GetValues("X-Session-Id").Single();
            Assert.AreEqual("tenant-" + session, request.Headers.GetValues("X-Tenant-Id").Single());
            Assert.AreEqual("turn-" + session, request.Headers.GetValues("X-Turn-Id").Single());
            Assert.AreEqual("trace-" + session, request.Headers.GetValues("X-Trace-Id").Single());
            Assert.AreEqual("event-" + session, request.Headers.GetValues("X-Bvent-Id").Single());
            Assert.IsNull(request.Headers.Authorization, "无密钥的兼容端点不得收到占位认证头");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var messages = body.RootElement.GetProperty("messages").ToString();
            StringAssert.Contains(messages, "history-" + session);
            StringAssert.Contains(messages, "question-" + session);
            Assert.IsFalse(messages.Contains("foreign-secret", StringComparison.Ordinal));
            Assert.IsFalse(messages.Contains("failed-secret", StringComparison.Ordinal));
            Assert.IsFalse(messages.Contains("question-" + (session == "a" ? "b" : "a"), StringComparison.Ordinal));
            return Answer();
        }));
        var model = Model(http);
        await Task.WhenAll(new[] { "a", "b" }.Select(async session =>
        {
            var scope = new Scope("tenant-" + session, "org-a", session, "assistant-a", "event-" + session);
            var valid = new AiTurn
            {
                TenantId = scope.TenantId,
                TopOrgId = scope.TopOrgId,
                ConversationId = session,
                AssistantConfigId = scope.AssistantConfigId,
                BventId = scope.BventId,
                Status = "SUCCEEDED",
                UserContent = "history-" + session,
                AssistantContent = "answer-" + session
            };
            var foreign = new AiTurn { Status = "SUCCEEDED", UserContent = "foreign-secret", AssistantContent = "secret" };
            var failed = new AiTurn
            {
                TenantId = scope.TenantId,
                TopOrgId = scope.TopOrgId,
                ConversationId = session,
                AssistantConfigId = scope.AssistantConfigId,
                BventId = scope.BventId,
                Status = "FAILED",
                UserContent = "failed-secret"
            };
            await CollectAsync(model, new(scope, "turn-" + session, "trace-" + session, [valid, foreign, failed], "question-" + session));
        }));
    }

    /// <summary>成功的分类确实经过 Agent Framework，JSON 格式和合法置信结果不能被统一降级掩盖。</summary>
    [TestMethod]
    public async Task ClassificationUsesFrameworkAndStrictOutput()
    {
        var content = """{"category":"EXHIBITOR","confidence":0.9}""";
        using var http = new HttpClient(new ProtocolHandler(async request =>
        {
            Assert.AreEqual("session-a", request.Headers.GetValues("X-Session-Id").Single());
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.AreEqual("json_object", body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
            return ClassificationResponse(content);
        }));
        var result = await Model(http).ClassifyAsync(Input().Scope, "展位咨询", default);
        Assert.AreEqual(Category.EXHIBITOR, result.Category);
        Assert.AreEqual(.9, result.Confidence);
        content = """{"category":"MEDIA","confidence":0.99,"topOrgId":"forged"}""";
        Assert.AreEqual(Category.OTHER, (await Model(http).ClassifyAsync(Input().Scope, "展位咨询", default)).Category);
    }

    /// <summary>SDK HTTP 失败不隐式重试，转换为既有可靠恢复可识别的业务异常。</summary>
    [TestMethod]
    public async Task ProviderFailureHasNoHiddenRetry()
    {
        var requests = 0;
        using var http = new HttpClient(new ProtocolHandler(_ =>
        {
            requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{\"error\":{\"message\":\"test\"}}") });
        }));
        await Assert.ThrowsExactlyAsync<BusinessException>(() => CollectAsync(Model(http), Input()));
        Assert.AreEqual(1, requests);
    }

    /// <summary>客户端取消会中止框架续轮，并丢弃已执行但尚未提交的工具候选。</summary>
    [TestMethod]
    public async Task CancellationDiscardsToolCandidates()
    {
        using var cancellation = new CancellationTokenSource();
        var requests = 0;
        using var http = new HttpClient(new ProtocolHandler(_ =>
        {
            if (++requests == 1)
            {
                return Task.FromResult(ToolResponse("proposeResolution", "{\"confidence\":0.95}"));
            }
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }));
        var emitted = new List<ModelDelta>();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var delta in Model(http).StreamAsync(Input(), cancellation.Token))
            {
                emitted.Add(delta);
            }
        });
        Assert.AreEqual(2, requests);
        Assert.IsTrue(emitted.All(x => x.ToolName is null));
    }

    /// <summary>连续工具调用达到框架迭代上限时必须失败，不能把最后一个 tool_calls 当作成功回答。</summary>
    [TestMethod]
    public async Task ToolLoopHasBoundedIterations()
    {
        var requests = 0;
        using var http = new HttpClient(new ProtocolHandler(_ =>
        {
            requests++;
            return Task.FromResult(Stream(JsonSerializer.Serialize(new
            {
                index = 0,
                delta = new
                {
                    role = "assistant",
                    tool_calls = new[] { new { index = 0, id = "loop-" + requests, type = "function", function = new { name = "requestHandoff", arguments = "{}" } } }
                },
                finish_reason = "tool_calls"
            })));
        }));
        await Assert.ThrowsExactlyAsync<BusinessException>(() => CollectAsync(Model(http), Input()));
        Assert.IsTrue(requests is > 1 and <= 5, "框架必须有界结束工具循环");
    }

    /// <summary>真实 JWT、MVC、MAF、SQLite 全链路验证：建议仅随成功轮次提交，失败则登记可靠恢复。</summary>
    [TestMethod]
    [DataRow("stop", true)]
    [DataRow("length", false)]
    public async Task FrameworkRunCommitsThroughBusinessFence(string finish, bool succeeded)
    {
        var requests = 0;
        using var http = new HttpClient(new ProtocolHandler(_ => Task.FromResult(++requests == 1
            ? ToolResponse("proposeHandoff", """{"reasonCode":"KNOWLEDGE_GAP","confidence":0.9,"userFacingReason":"需要专业客服核对"}""")
            : Stream(JsonSerializer.Serialize(new { index = 0, delta = new { role = "assistant", content = "请确认是否需要人工协助。" }, finish_reason = finish })))));
        await using var application = new TestApplication { ModelPort = Model(http) };
        await application.ConfigureAsync();
        using var visitor = application.Client();
        var sessionId = await TestApplication.SessionAsync(visitor);
        using var response = await visitor.PostAsJsonAsync("/api/agent/chatStream", new
        {
            sessionId,
            userInput = "请问入场证如何补办？",
            businessType = "event-assistant",
            extensions = new Dictionary<string, object> { ["event-assistant"] = new { assistantConfigId = "assistant-a" } }
        });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var stream = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(stream, succeeded ? "event: complete" : "event: error");
        Assert.AreEqual(2, requests);
        await using var scope = application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>();
        Assert.AreEqual(succeeded ? "SUCCEEDED" : "FAILED", (await db.Set<AiTurn>().SingleAsync()).Status);
        Assert.AreEqual(succeeded ? 1 : 0, await db.Set<Proposal>().CountAsync());
        Assert.AreEqual(0, await db.Set<Episode>().CountAsync(), "建议和失败均不能直接产生人工申请");
        Assert.AreEqual(succeeded ? 0 : 1, await db.Set<ReliableTask>().CountAsync(x => x.Kind == "SYSTEM_HANDOFF"));
    }

    /// <summary>生成含完整 OpenAI 元数据的流响应，由官方 provider 解析每个网络事件。</summary>
    internal static HttpResponseMessage Stream(params string[] choices)
    {
        var frames = choices.Select(choice => choice == "[DONE]" ? "[DONE]" :
            "{\"id\":\"chat-test\",\"object\":\"chat.completion.chunk\",\"created\":1794441600,\"model\":\"test\",\"choices\":[" + choice + "]}");
        return new(HttpStatusCode.OK) { Content = new StringContent(string.Concat(frames.Select(x => "data: " + x + "\n\n")), Encoding.UTF8, "text/event-stream") };
    }

    /// <summary>构造 SDK 可解析的分类完成响应，含明确 stop 结束证据。</summary>
    internal static HttpResponseMessage ClassificationResponse(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            id = "classification-test",
            @object = "chat.completion",
            created = 1794441600,
            model = "test",
            choices = new[] { new { index = 0, message = new { role = "assistant", content }, finish_reason = "stop" } }
        }), Encoding.UTF8, "application/json")
    };

    /// <summary>生成自动工具调用响应，框架必须调用本地函数才能得到下一轮工具结果。</summary>
    private static HttpResponseMessage ToolResponse(string name, string arguments) => Stream(JsonSerializer.Serialize(new
    {
        index = 0,
        delta = new { role = "assistant", tool_calls = new[] { new { index = 0, id = "call-a", type = "function", function = new { name, arguments } } } },
        finish_reason = "tool_calls"
    }), "[DONE]");

    /// <summary>生成正常的最终文本回答。</summary>
    private static HttpResponseMessage Answer() => Stream("""{"index":0,"delta":{"role":"assistant","content":"完整答复"},"finish_reason":"stop"}""", "[DONE]");

    /// <summary>创建隔离框架适配器，不访问真实供应商。</summary>
    private static AgentFrameworkChatModel Model(HttpClient http) => new(http, Options.Create(new ModelOptions { BaseUrl = "https://model.invalid/v1", Model = "test" }));

    /// <summary>固定可信会话输入，模型不能修改这些归属字段。</summary>
    private static ModelRequest Input() => new(new("tenant-a", "org-a", "session-a", "assistant-a", null), "turn-a", "trace-a", [], "请问怎么入场？");

    /// <summary>完整枚举框架增量以覆盖最终完成与候选释放门禁。</summary>
    private static async Task<List<ModelDelta>> CollectAsync(IChatModel model, ModelRequest input)
    {
        var deltas = new List<ModelDelta>();
        await foreach (var delta in model.StreamAsync(input, default))
        {
            deltas.Add(delta);
        }
        return deltas;
    }
}
