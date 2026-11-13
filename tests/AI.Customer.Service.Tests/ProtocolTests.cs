using System.Net;
using System.Text;
using System.Text.Json;
using AI.Customer.Service.Common;
using AI.Customer.Service.Core.Adapters;
using AI.Customer.Service.Core.Models;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Policies;
using AI.Customer.Service.Domain.Ports;
using AI.Customer.Service.Infrastructure.Adapters;
using AI.Customer.Service.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AI.Customer.Service.Tests;

/// <summary>真实 HTTP 协议适配器回归，外部网络由消息处理器替代。</summary>
[TestClass]
public sealed class ProtocolTests
{
    /// <summary>用户中心组织状态必须启用，账号租户不可被外部目录名称替代。</summary>
    [TestMethod]
    public async Task AccountInfoAndOrganizationMustAgree()
    {
        using var http = new HttpClient(new ProtocolHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/Info", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Assert.AreEqual("account-a", body.RootElement.GetProperty("accountId").GetString());
                return Json("""{"isSuccess":true,"returnObj":{"accountId":"account-a","tenantId":"tenant-a","fullName":"正式客服"}}""");
            }
            Assert.AreEqual("?accountId=account-a", request.RequestUri.Query);
            return Json("""{"isSuccess":true,"returnObj":[{"orgId":"org-a","status":1}]}""");
        }));
        var adapter = new UserCenterAdapter(http, Options.Create(new UserCenterOptions { BaseUrl = "https://directory.invalid" }));
        Assert.AreEqual("正式客服", (await adapter.RequireAsync("tenant-a", "org-a", "account-a", default)).DisplayName);
        await Assert.ThrowsExactlyAsync<BusinessException>(() => adapter.RequireAsync("tenant-b", "org-a", "account-a", default));
        await Assert.ThrowsExactlyAsync<BusinessException>(() => adapter.RequireAsync("tenant-a", "org-b", "account-a", default));
    }

    /// <summary>旧用户中心要求 PascalCase 目录参数，游标必须固定组织和关键词。</summary>
    [TestMethod]
    public async Task DirectoryProtocolPreservesPascalCaseAndCursorScope()
    {
        using var http = new HttpClient(new ProtocolHandler(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var root = body.RootElement;
            Assert.AreEqual("org-a", root.GetProperty("OrganizationId").GetString());
            Assert.AreEqual(JsonValueKind.Null, root.GetProperty("OrgId").ValueKind);
            Assert.AreEqual("客服", root.GetProperty("Search").GetString());
            Assert.AreEqual(2, root.GetProperty("PageSize").GetInt32());
            Assert.IsFalse(root.TryGetProperty("pageSize", out _));
            var page = root.GetProperty("PageIndex").GetInt32();
            Assert.AreEqual((page - 1) * 2, root.GetProperty("Skip").GetInt32());
            return Json(JsonSerializer.Serialize(new { isSuccess = true, returnObj = new[] { new { id = "group-a", orgId = "org-a", name = "客服组" } }, pager = new { pageIndex = page, pageSize = 2, totalPage = 2, totalCount = 3 } }));
        }));
        var adapter = new UserCenterAdapter(http, Options.Create(new UserCenterOptions { BaseUrl = "https://directory.invalid" }));
        var first = await adapter.GroupsAsync("tenant-a", "org-a", "客服", null, 2, default);
        Assert.IsTrue(first.HasMore);
        Assert.IsFalse((await adapter.GroupsAsync("tenant-a", "org-a", "客服", first.NextCursor, 2, default)).HasMore);
        await Assert.ThrowsExactlyAsync<BusinessException>(() => adapter.GroupsAsync("tenant-a", "org-b", "客服", first.NextCursor, 2, default));
    }

    /// <summary>流式工具参数跨多帧累计，中文文本与正文之外的工具候选都完整恢复。</summary>
    [TestMethod]
    public async Task ModelSseReassemblesToolArguments()
    {
        var frames = new[]
        {
            """{"index":0,"delta":{"role":"assistant","content":"您好"}}""",
            """{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call-a","type":"function","function":{"name":"proposeResolution","arguments":"{\"confidence\":"}}]}}""",
            """{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"0.95}"}}]},"finish_reason":"tool_calls"}""",
            "[DONE]"
        };
        var requests = 0;
        using var http = new HttpClient(new ProtocolHandler(async request =>
        {
            Assert.AreEqual("session-a", request.Headers.GetValues("X-Session-Id").Single());
            Assert.AreEqual("turn-a", request.Headers.GetValues("X-Turn-Id").Single());
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.IsTrue(body.RootElement.GetProperty("stream").GetBoolean());
            requests++;
            if (requests == 2)
            {
                var tool = body.RootElement.GetProperty("messages").EnumerateArray().Single(x => x.GetProperty("role").GetString() == "tool");
                StringAssert.Contains(tool.GetProperty("content").GetString(), "PROPOSAL_ONLY");
                return AgentFrameworkTests.Stream("""{"index":0,"delta":{"role":"assistant","content":"请继续确认是否解决。"},"finish_reason":"stop"}""");
            }
            Assert.AreEqual(1, requests, "框架只需一次工具续轮");
            Assert.AreEqual(3, body.RootElement.GetProperty("tools").GetArrayLength());
            return AgentFrameworkTests.Stream(frames);
        }));
        var adapter = Model(http);
        var deltas = new List<ModelDelta>();
        await foreach (var delta in adapter.StreamAsync(new(new("tenant-a", "org-a", "session-a", "assistant-a", null), "turn-a", "trace-a", [], "请问怎么入场？"), default))
        {
            deltas.Add(delta);
        }
        Assert.AreEqual("您好", deltas[0].Text);
        Assert.AreEqual(2, requests, "必须由框架实际执行工具并把结果送回模型");
        var candidate = deltas.Single(x => x.ToolName is not null);
        Assert.AreEqual("proposeResolution", candidate.ToolName);
        Assert.AreEqual(.95, JsonDocument.Parse(candidate.Arguments!).RootElement.GetProperty("confidence").GetDouble());
    }

    /// <summary>截断的模型流不能伪装成功，不够置信的语义分类必须降 OTHER。</summary>
    [TestMethod]
    public async Task IncompleteStreamFailsAndClassificationFallsBack()
    {
        using var truncatedHttp = new HttpClient(new ProtocolHandler(_ => Task.FromResult(AgentFrameworkTests.Stream("""{"index":0,"delta":{"role":"assistant","content":"半句"}}"""))));
        await Assert.ThrowsExactlyAsync<BusinessException>(async () =>
        {
            await foreach (var delta in Model(truncatedHttp).StreamAsync(new(new("t", "o", "c", "a", null), "turn", "trace", [], "问题"), default))
            {
                Assert.AreEqual("半句", delta.Text);
            }
        });
        using var limitedHttp = new HttpClient(new ProtocolHandler(_ => Task.FromResult(AgentFrameworkTests.Stream("""{"index":0,"delta":{},"finish_reason":"length"}"""))));
        await Assert.ThrowsExactlyAsync<BusinessException>(async () =>
        {
            await foreach (var delta in Model(limitedHttp).StreamAsync(new(new("t", "o", "c", "a", null), "turn", "trace", [], "问题"), default))
            {
                Assert.Fail("被截断的回答不能产生成功增量");
            }
        });
        using var classificationHttp = new HttpClient(new ProtocolHandler(_ => Task.FromResult(AgentFrameworkTests.ClassificationResponse("""{"category":"EXHIBITOR","confidence":0.74}"""))));
        Assert.AreEqual(Category.OTHER, (await Model(classificationHttp).ClassifyAsync(new("t", "o", "c", "a", null), "展位", default)).Category);
    }

    /// <summary>HTTP 200 仍需明确 accepted 回执；重试使用同一持久投递键。</summary>
    [TestMethod]
    public async Task NotificationRequiresExplicitAcceptance()
    {
        var accepted = false;
        using var http = new HttpClient(new ProtocolHandler(request =>
        {
            Assert.AreEqual("followup:ticket-a", request.Headers.GetValues("Idempotency-Key").Single());
            return Task.FromResult(Json(accepted ? "{\"accepted\":true}" : "{\"accepted\":false}"));
        }));
        var adapter = new NotificationHttpAdapter(http, Options.Create(new NotificationOptions { Enabled = true, Endpoint = "https://gateway.invalid" }));
        var task = new ReliableTask { Kind = "EMAIL", EventKey = "followup:ticket-a", Payload = "{}" };
        await Assert.ThrowsExactlyAsync<BusinessException>(() => adapter.SendAsync(task, default));
        accepted = true;
        await adapter.SendAsync(task, default);
    }

    /// <summary>沿用 Java 的明确请求样例，否定、引用和假设不能直接授权。</summary>
    [TestMethod]
    public void ExplicitHandoffRequiresOriginalConsent()
    {
        foreach (var input in new[] { "转人工", "请直接转人工", "我要人工客服", "可以转人工吗？", "麻烦帮我转接人工客服，谢谢", "还是没解决，请直接转人工", "你好，我要人工客服" })
        {
            Assert.IsTrue(ExplicitHandoffIntent.Matches(input), input);
        }
        foreach (var input in new[] { "不要转人工", "我不想转人工", "你回复得太差了", "如果需要转人工怎么办", "页面写着转人工", "请翻译：转人工", "有转人工功能吗", "他说转人工", "这个问题还没有解决" })
        {
            Assert.IsFalse(ExplicitHandoffIntent.Matches(input), input);
        }
    }

    /// <summary>创建真实 Microsoft Agent Framework 适配器，仅替代供应商 HTTP 传输。</summary>
    private static AgentFrameworkChatModel Model(HttpClient http) => new(http, Options.Create(new ModelOptions { BaseUrl = "https://model.invalid/v1", Model = "test" }));

    /// <summary>生成上游 JSON 响应，避免生产实现被测试数据污染。</summary>
    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
}

/// <summary>单次 HTTP 协议测试处理器；不注册到生产依赖容器。</summary>
/// <param name="respond">检查出站请求并生成替身响应。</param>
internal sealed class ProtocolHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    /// <summary>交由具体协议场景检查路径、字段和响应信封。</summary>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
}
