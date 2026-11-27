using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using AI.Customer.Service.Api.Contracts;
using AI.Customer.Service.Common;
using AI.Customer.Service.Core;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;
using AI.Customer.Service.Infrastructure.Persistence;
using AI.Customer.Service.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static AI.Customer.Service.Tests.TestApplication;

[assembly: DoNotParallelize]

namespace AI.Customer.Service.Tests;

/// <summary>
/// 真实 HTTP、数据库事务和 WebSocket 下的客服业务回归。
/// </summary>
[TestClass]
public sealed partial class WorkflowTests
{
    /// <summary>
    /// 每项测试使用独立应用和数据库。
    /// </summary>
    private TestApplication application = null!;

    /// <summary>
    /// 初始化正式分层应用，外部依赖仅在测试替换。
    /// </summary>
    [TestInitialize]
    public void Initialize() => application = new TestApplication();

    /// <summary>
    /// 关闭宿主并清理独立临时数据库。
    /// </summary>
    [TestCleanup]
    public async Task Cleanup() => await application.DisposeAsync();

    /// <summary>
    /// 匿名和过期凭据访问失败。
    /// </summary>
    [TestMethod]
    public async Task AuthenticationRejectsAnonymousAndExpired()
    {
        using var anonymous = application.CreateClient();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/agent/sessionId")).StatusCode);
        using var expired = application.Client();
        expired.DefaultRequestHeaders.Authorization = new("Bearer", Token("visitor", expired: true));
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await expired.GetAsync("/api/agent/sessionId")).StatusCode);
        using var visitor = application.Client();
        var id = await SessionAsync(visitor);
        await GetAsync(visitor, VisitorPath(id) + "/handoffs/latest");
    }

    /// <summary>
    /// 默认配置只读不创建组，保存时外部名称覆盖客户端展示快照且旧版本冲突。
    /// </summary>
    [TestMethod]
    public async Task RoutingReadAndCompareAndSwap()
    {
        using var manager = application.Client("agent-a");
        var initial = await GetAsync(manager, "/api/customer-service/configuration");
        Assert.AreEqual(0L, initial.GetProperty("version").GetInt64());
        await using (var scope = application.Services.CreateAsyncScope())
        {
            Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>().Set<RoutingConfiguration>().CountAsync());
        }
        await application.ConfigureAsync();
        var saved = await GetAsync(manager, "/api/customer-service/configuration");
        Assert.AreEqual("权威名称-agent-a", saved.GetProperty("groups")[0].GetProperty("members")[0].GetProperty("displayName").GetString());
        await SendAsync(manager, HttpMethod.Put, "/api/customer-service/configuration",
            new { version = 0, groups = saved.GetProperty("groups"), routes = saved.GetProperty("routes") }, HttpStatusCode.Conflict);
    }

    /// <summary>
    /// 直接申请不二次确认，同键返回首次周期且不再次推进 generation。
    /// </summary>
    [TestMethod]
    public async Task DirectHandoffIsDurableAndIdempotent()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        var id = await SessionAsync(visitor);
        var first = await RequestAsync(visitor, id, "direct-one");
        var replay = await RequestAsync(visitor, id, "direct-one");
        Assert.AreEqual(first.GetProperty("episodeId").GetString(), replay.GetProperty("episodeId").GetString());
        Assert.IsTrue(replay.GetProperty("replayed").GetBoolean());
        await using var scope = application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>();
        Assert.AreEqual(1, await db.Set<Episode>().CountAsync());
        var conversation = await db.Set<Conversation>().SingleAsync();
        Assert.AreEqual(2L, conversation.Epoch);
        Assert.AreEqual(TriggerSource.VISITOR_DIRECT_REQUEST, (await db.Set<Episode>().SingleAsync()).TriggerSource);
        Assert.AreEqual("visitor@example.com", (await db.Set<Waiting>().SingleAsync()).Email);
        await SendAsync(visitor, HttpMethod.Post, VisitorPath(id) + "/handoffs", new { requestId = "spoof", forgedField = "forged" }, HttpStatusCode.BadRequest);
        await SendAsync(visitor, HttpMethod.Post, VisitorPath(id) + "/handoffs", new { requestId = "missing-email" }, HttpStatusCode.BadRequest);
        Assert.AreEqual(1, await db.Set<Episode>().CountAsync(), "缺少邮箱时不得创建接待周期");
    }

    /// <summary>
    /// 两位客服同版本竞争只能一个成功，未上线仍允许手动接单。
    /// </summary>
    [TestMethod]
    public async Task ConcurrentClaimHasOneWinnerWithoutOnline()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        var id = await SessionAsync(visitor);
        await RequestAsync(visitor, id);
        using var first = application.Client("agent-a");
        using var second = application.Client("agent-b");
        var responses = await Task.WhenAll(
            first.PostAsJsonAsync(AgentPath(id) + "/claim", new { requestId = "claim-a", expectedVersion = 1 }),
            second.PostAsJsonAsync(AgentPath(id) + "/claim", new { requestId = "claim-b", expectedVersion = 1 }));
        CollectionAssert.AreEquivalent(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, responses.Select(x => x.StatusCode).ToArray());
        foreach (var response in responses)
        {
            response.Dispose();
        }
        await using var scope = application.Services.CreateAsyncScope();
        Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>().Set<Episode>().CountAsync(x => x.Status == EpisodeStatus.CLAIMED));
    }

    /// <summary>
    /// 访客读取已认领周期时只看到原组已核验客服的展示名，不会得到账号标识。
    /// </summary>
    [TestMethod]
    public async Task VisitorReceptionShowsAssignedAgentNameWithoutAccountId()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        using var agent = application.Client("agent-a");
        var id = await SessionAsync(visitor);
        await RequestAsync(visitor, id);
        await SendAsync(agent, HttpMethod.Post, AgentPath(id) + "/claim", new { requestId = "claim-name", expectedVersion = 1 });

        var latest = await GetAsync(visitor, VisitorPath(id) + "/handoffs/latest");
        var episode = latest.GetProperty("episode");
        Assert.AreEqual("权威名称-agent-a", episode.GetProperty("agentDisplayName").GetString());
        Assert.IsFalse(episode.TryGetProperty("agentId", out _));
    }

    /// <summary>
    /// 一个客服跨两个会话竞争最后容量时只有一个成功，容量不是单会话计数。
    /// </summary>
    [TestMethod]
    public async Task OrganizationCapacitySerializesAcrossConversations()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        var one = await SessionAsync(visitor);
        var two = await SessionAsync(visitor);
        await RequestAsync(visitor, one, "one");
        await RequestAsync(visitor, two, "two");
        using var agent = application.Client("agent-a");
        var responses = await Task.WhenAll(
            agent.PostAsJsonAsync(AgentPath(one) + "/claim", new { requestId = "one", expectedVersion = 1 }),
            agent.PostAsJsonAsync(AgentPath(two) + "/claim", new { requestId = "two", expectedVersion = 1 }));
        CollectionAssert.AreEquivalent(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, responses.Select(x => x.StatusCode).ToArray());
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    /// <summary>
    /// 取消和接单共用同一事务围栏，不出现 HUMAN 回复权和 CANCELLED 半写。
    /// </summary>
    [TestMethod]
    public async Task CancelVersusClaimIsAtomic()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        using var agent = application.Client("agent-a");
        var id = await SessionAsync(visitor);
        await RequestAsync(visitor, id);
        var results = await Task.WhenAll(
            visitor.PostAsJsonAsync(VisitorPath(id) + "/reception/cancel", new { requestId = "cancel", expectedVersion = 1 }),
            agent.PostAsJsonAsync(AgentPath(id) + "/claim", new { requestId = "claim", expectedVersion = 1 }));
        Assert.AreEqual(1, results.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.AreEqual(1, results.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        await using var scope = application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>();
        var episode = await db.Set<Episode>().SingleAsync();
        var conversation = await db.Set<Conversation>().SingleAsync();
        Assert.AreEqual(episode.Status == EpisodeStatus.CANCELLED, conversation.HumanEpisodeId is null);
        foreach (var response in results)
        {
            response.Dispose();
        }
    }

    /// <summary>
    /// 验证纯文本消息幂等、首响、单调回执和终态重放。
    /// </summary>
    [TestMethod]
    public async Task MessagesReceiptsAndTerminalReplay()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        using var agent = application.Client("agent-a");
        var id = await SessionAsync(visitor);
        await RequestAsync(visitor, id);
        await SendAsync(agent, HttpMethod.Post, AgentPath(id) + "/claim", new { requestId = "claim", expectedVersion = 1 });
        var first = await SendAsync(agent, HttpMethod.Post, AgentPath(id) + "/messages", new { clientMsgId = "hello", content = "<script>纯文本</script>" });
        Assert.AreEqual(1L, first.GetProperty("seq").GetInt64());
        var replay = await SendAsync(agent, HttpMethod.Post, AgentPath(id) + "/messages", new { clientMsgId = "hello", content = "<script>纯文本</script>" });
        Assert.AreEqual(first.GetProperty("messageId").GetString(), replay.GetProperty("messageId").GetString());
        await SendAsync(agent, HttpMethod.Post, AgentPath(id) + "/messages", new { clientMsgId = "hello", content = "改写" }, HttpStatusCode.Conflict);
        await SendAsync(visitor, HttpMethod.Post, VisitorPath(id) + "/receipts", new { kind = "READ", seq = 2 }, HttpStatusCode.BadRequest);
        var receipt = await SendAsync(visitor, HttpMethod.Post, VisitorPath(id) + "/receipts", new { kind = "READ", seq = 1 });
        Assert.AreEqual(1L, receipt.GetProperty("deliveredSeq").GetInt64());
        await SendAsync(visitor, HttpMethod.Post, VisitorPath(id) + "/receipts", new { kind = "READ", seq = 0 });
        var timeline = await GetAsync(visitor, VisitorPath(id) + "/messages");
        Assert.AreEqual(1L, timeline.GetProperty("receipt").GetProperty("readSeq").GetInt64());
        var current = await GetAsync(agent, AgentPath(id) + "/reception");
        Assert.AreEqual("ACTIVE", current.GetProperty("status").GetString());
        await SendAsync(agent, HttpMethod.Post, AgentPath(id) + "/resolve", new { requestId = "resolve", expectedVersion = current.GetProperty("version").GetInt64() });
        await SendAsync(agent, HttpMethod.Post, AgentPath(id) + "/messages", new { clientMsgId = "hello", content = "<script>纯文本</script>" });
        await SendAsync(visitor, HttpMethod.Post, VisitorPath(id) + "/messages", new { clientMsgId = "late", content = "新消息" }, HttpStatusCode.Conflict);
    }

    /// <summary>
    /// 验证已完成接待固定原周期读取及全量 XLSX 包结构。
    /// </summary>
    [TestMethod]
    public async Task CompletedReceptionAndConversationXlsxAreAvailable()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        using var agent = application.Client("agent-a");
        var id = await SessionAsync(visitor);
        await RequestAsync(visitor, id);
        await SendAsync(agent, HttpMethod.Post, AgentPath(id) + "/claim", new { requestId = "claim-history", expectedVersion = 1 });
        var visitorSent = await SendAsync(visitor, HttpMethod.Post, VisitorPath(id) + "/messages", new { clientMsgId = "visitor-history", content = "历史\u0001消息正文" });
        var sent = await SendAsync(agent, HttpMethod.Post, AgentPath(id) + "/messages", new { clientMsgId = "agent-history", content = "已完成接待回复" });
        var current = await GetAsync(agent, AgentPath(id) + "/reception");
        await SendAsync(agent, HttpMethod.Post, AgentPath(id) + "/resolve", new
        {
            requestId = "resolve-history",
            expectedVersion = current.GetProperty("version").GetInt64()
        });

        var completed = await GetAsync(agent, "/api/customer-service/agents/receptions/completed?assistantConfigId=assistant-a&page=1&size=10");
        var episode = completed.GetProperty("episodes").EnumerateArray().Single();
        var episodeId = episode.GetProperty("episodeId").GetString()!;
        Assert.AreEqual("RESOLVED", episode.GetProperty("status").GetString());
        var historical = await GetAsync(agent, AgentPath(id) + $"/episodes/{episodeId}/messages?afterSeq={visitorSent.GetProperty("seq").GetInt64()}&limit=1");
        Assert.AreEqual(sent.GetProperty("messageId").GetString(), historical.GetProperty("messages")[0].GetProperty("messageId").GetString());
        Assert.IsFalse(historical.GetProperty("hasMore").GetBoolean());

        using var otherAgent = application.Client("agent-b");
        var otherHistory = await GetAsync(otherAgent, "/api/customer-service/agents/receptions/completed?assistantConfigId=assistant-a");
        Assert.AreEqual(0, otherHistory.GetProperty("episodes").GetArrayLength());
        await GetAsync(otherAgent, AgentPath(id) + $"/episodes/{episodeId}/reception", HttpStatusCode.Forbidden);
        await GetAsync(otherAgent, AgentPath(id) + $"/episodes/{episodeId}/messages", HttpStatusCode.Forbidden);
        await GetAsync(agent, "/api/customer-service/agents/receptions/completed?assistantConfigId=assistant-a&email=missing@example.com");

        using var anonymous = application.CreateClient();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/customer-service/exports/conversations?assistantConfigId=assistant-a&format=xlsx")).StatusCode);
        using var unlistedAccount = application.Client("unlisted");
        await GetAsync(unlistedAccount, "/api/customer-service/exports/conversations?assistantConfigId=assistant-a&format=xlsx", HttpStatusCode.Forbidden);

        var response = await agent.GetAsync("/api/customer-service/exports/conversations?assistantConfigId=assistant-a&format=xlsx&all=true");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("false", response.Headers.GetValues("X-Has-More").Single());
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        Assert.IsNotNull(workbookEntry);
        using var workbookStream = workbookEntry.Open();
        var workbookXml = await System.Xml.Linq.XDocument.LoadAsync(workbookStream, System.Xml.Linq.LoadOptions.None, CancellationToken.None);
        Assert.AreEqual(3, workbookXml.Root!.Element(System.Xml.Linq.XName.Get("sheets", "http://schemas.openxmlformats.org/spreadsheetml/2006/main"))!.Elements().Count());
        Assert.IsTrue(archive.Entries.Any(entry => entry.FullName == "xl/worksheets/sheet1.xml"));
        using var transcriptStream = archive.GetEntry("xl/worksheets/sheet2.xml")!.Open();
        var transcriptXml = await System.Xml.Linq.XDocument.LoadAsync(transcriptStream, System.Xml.Linq.LoadOptions.None, CancellationToken.None);
        StringAssert.Contains(transcriptXml.ToString(), "历史消息正文");

        await RequestAsync(visitor, id);
        var oldTimeline = await GetAsync(agent, AgentPath(id) + $"/episodes/{episodeId}/messages");
        Assert.AreEqual(2, oldTimeline.GetProperty("messages").GetArrayLength());
        Assert.IsTrue(oldTimeline.GetProperty("messages").EnumerateArray().All(message => message.GetProperty("episodeId").GetString() == episodeId));
    }

    /// <summary>
    /// 非营业时明确转人工只返回带下次营业时间的留言卡，不预先创建接待周期。
    /// </summary>
    [TestMethod]
    public async Task ExplicitHandoffDuringClosedHoursReturnsOfflineFollowupCard()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        using var manager = application.Client("agent-a");
        var id = await SessionAsync(visitor);
        var calendar = await GetAsync(manager, "/api/customer-service/configuration/calendar");
        await SendAsync(manager, HttpMethod.Post, "/api/customer-service/configuration/calendar", new
        {
            version = calendar.GetProperty("version").GetInt64(),
            calendarVersion = "closed-hours-test",
            zone = "Asia/Shanghai",
            opens = "08:00",
            closes = "10:00",
            overrides = new Dictionary<string, bool>()
        });

        var stream = await ChatAsync(visitor, id, "请转人工客服");
        StringAssert.Contains(stream, "handoff.offline_followup_required");
        StringAssert.Contains(stream, "nextOpensAt");
        StringAssert.Contains(stream, "serviceHours");
        StringAssert.Contains(stream, "workingDays");
        await using var scope = application.Services.CreateAsyncScope();
        Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>().Set<Episode>().CountAsync());
    }

    /// <summary>
    /// 配置接口保存并返回自定义工作日及多个营业时段。
    /// </summary>
    [TestMethod]
    public async Task CalendarSettingsPersistMultipleWeeklySlots()
    {
        await application.ConfigureAsync();
        using var manager = application.Client("agent-a");
        var current = await GetAsync(manager, "/api/customer-service/configuration/calendar");

        using var legacyPut = await manager.PutAsJsonAsync("/api/customer-service/configuration/calendar", new
        {
            version = current.GetProperty("version").GetInt64(),
            calendarVersion = "legacy-put",
            zone = "Asia/Shanghai",
            opens = "09:00",
            closes = "17:00",
            overrides = new Dictionary<string, bool>()
        });
        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, legacyPut.StatusCode);

        var saved = await SendAsync(manager, HttpMethod.Post, "/api/customer-service/configuration/calendar", new
        {
            version = current.GetProperty("version").GetInt64(),
            calendarVersion = "weekly-hours-v2",
            zone = "Asia/Shanghai",
            opens = (string?)null,
            closes = (string?)null,
            overrides = new Dictionary<string, bool>(),
            workingDays = new[] { 1, 2, 3, 4, 5, 6 },
            slots = new[] { new { opens = "09:00", closes = "12:00" }, new { opens = "13:00", closes = "17:00" } }
        });

        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, saved.GetProperty("workingDays").EnumerateArray().Select(x => x.GetInt32()).ToArray());
        Assert.AreEqual(JsonValueKind.Null, saved.GetProperty("opens").ValueKind);
        Assert.AreEqual("09:00", saved.GetProperty("slots")[0].GetProperty("opens").GetString());
        Assert.AreEqual("17:00", saved.GetProperty("slots")[1].GetProperty("closes").GetString());

        await using var scope = application.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>().Set<CalendarPolicy>().SingleAsync();
        Assert.AreEqual(2, stored.EffectiveSlots().Count);
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, stored.EffectiveWorkingDays().ToArray());
    }

    /// <summary>
    /// 模型识别的明确人工意图发送邮箱采集卡，访客提交前不创建接待周期。
    /// </summary>
    [TestMethod]
    public async Task ModelRecognizedHandoffIntentReturnsEmailCardWithoutCreatingEpisode()
    {
        await application.ConfigureAsync();
        application.Model.Tool = new(null, "requestHandoff", "{}");
        using var visitor = application.Client();
        var id = await SessionAsync(visitor);

        var stream = await ChatAsync(visitor, id, "我还没解决这个问题，想请真人客服帮忙核实一下。");

        StringAssert.Contains(stream, "handoff.email_required");
        StringAssert.Contains(stream, "event: complete");
        await using var scope = application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>();
        Assert.AreEqual(0, await db.Set<Episode>().CountAsync());
        Assert.AreEqual("SUCCEEDED", (await db.Set<AiTurn>().SingleAsync()).Status);
    }

    /// <summary>
    /// 邮箱卡提交复用首次分类，避免重复模型判断改变冻结路由。
    /// </summary>
    [TestMethod]
    public async Task DirectHandoffSubmissionUsesTheCategoryReturnedByItsCard()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        var id = await SessionAsync(visitor);
        var stream = await ChatAsync(visitor, id, "请转人工客服");
        var cardLine = stream.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.StartsWith("data:", StringComparison.Ordinal) && line.Contains("handoff.email_required", StringComparison.Ordinal));
        using var card = JsonDocument.Parse(cardLine["data:".Length..].Trim());
        var cardData = card.RootElement.GetProperty("data");
        var category = cardData.GetProperty("category").GetString();
        Assert.IsNotNull(category);
        var classifications = application.Model.ClassificationCalls;

        await SendAsync(visitor, HttpMethod.Post, VisitorPath(id) + "/handoffs", new
        {
            requestId = cardData.GetProperty("requestId").GetString(),
            email = "visitor@example.com",
            category
        });

        Assert.AreEqual(classifications, application.Model.ClassificationCalls);
        await using var scope = application.Services.CreateAsyncScope();
        var episode = await scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>().Set<Episode>().SingleAsync();
        Assert.AreEqual(Enum.Parse<Category>(category), episode.Category);
        Assert.AreEqual("CARD", episode.ClassificationSource);
    }

    /// <summary>
    /// 改派需目标在线，成功后旧客服不能读取历史或正文；原键重放仍恢复首次回执。
    /// </summary>
    [TestMethod]
    public async Task TransferRevokesPreviousAssigneeAndKeepsSource()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        using var first = application.Client("agent-a");
        using var second = application.Client("agent-b");
        var id = await SessionAsync(visitor);
        await RequestAsync(visitor, id);
        await SendAsync(first, HttpMethod.Post, AgentPath(id) + "/claim", new { requestId = "claim", expectedVersion = 1 });
        var transfer = new { requestId = "transfer", expectedVersion = 2, targetAccountId = "agent-b" };
        await SendAsync(first, HttpMethod.Post, AgentPath(id) + "/transfer", transfer, HttpStatusCode.Conflict);
        await SendAsync(second, HttpMethod.Post, "/api/customer-service/agents/availability?assistantConfigId=assistant-a", new { status = "ONLINE" });
        await SendAsync(first, HttpMethod.Post, AgentPath(id) + "/transfer", transfer);
        var replay = await SendAsync(first, HttpMethod.Post, AgentPath(id) + "/transfer", transfer);
        Assert.IsTrue(replay.GetProperty("replayed").GetBoolean());
        await GetAsync(first, AgentPath(id) + "/messages", HttpStatusCode.Forbidden);
        await GetAsync(second, AgentPath(id) + "/messages");
        var latest = await GetAsync(visitor, VisitorPath(id) + "/handoffs/latest");
        Assert.AreEqual("VISITOR_DIRECT_REQUEST", latest.GetProperty("episode").GetProperty("triggerSource").GetString());
    }

    /// <summary>
    /// 旧上线代次不能通过心跳覆盖用户主动离线。
    /// </summary>
    [TestMethod]
    public async Task HeartbeatCannotResurrectOfflineSelection()
    {
        await application.ConfigureAsync();
        using var agent = application.Client("agent-a");
        const string path = "/api/customer-service/agents/availability?assistantConfigId=assistant-a";
        var online = await SendAsync(agent, HttpMethod.Post, path, new { status = "ONLINE" });
        await SendAsync(agent, HttpMethod.Post, path, new { status = "OFFLINE" });
        await SendAsync(agent, HttpMethod.Post, "/api/customer-service/agents/availability/heartbeat?assistantConfigId=assistant-a",
            new { version = online.GetProperty("version").GetInt64() }, HttpStatusCode.Conflict);
    }

    /// <summary>
    /// 等待在访问时补算五分钟提醒、十分钟繁忙及闭班恢复，不依赖全局扫描。
    /// </summary>
    [TestMethod]
    public async Task WaitingDeadlinesAreLazyAndCloseRestoresAi()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        var id = await SessionAsync(visitor);
        await RequestAsync(visitor, id);
        application.Clock.Advance(TimeSpan.FromMinutes(5));
        var initial = await GetAsync(visitor, VisitorPath(id) + "/waiting");
        Assert.AreEqual("WAITING", initial.GetProperty("state").GetString());
        application.Clock.Advance(TimeSpan.FromMinutes(5));
        var busy = await GetAsync(visitor, VisitorPath(id) + "/waiting");
        Assert.AreEqual("BUSY", busy.GetProperty("state").GetString());
        await SendAsync(visitor, HttpMethod.Post, VisitorPath(id) + "/waiting", new { expectedVersion = busy.GetProperty("version").GetInt64(), action = "CONTINUE" });
        application.Clock.Advance(TimeSpan.FromHours(6));
        var closed = await GetAsync(visitor, VisitorPath(id) + "/waiting");
        Assert.AreEqual("NEED_EMAIL", closed.GetProperty("state").GetString());
        await using var scope = application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>();
        Assert.IsNull((await db.Set<Conversation>().SingleAsync()).HumanEpisodeId);
        Assert.AreEqual(0, await db.Set<ReliableTask>().CountAsync(x => x.Kind == "INTERNAL"));
    }

    /// <summary>
    /// 留言正文必须先认领，最终回复经网关受理才可结案。
    /// </summary>
    [TestMethod]
    public async Task FollowupClaimReplyDeliveryAndExplicitClose()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        using var agent = application.Client("agent-a");
        var id = await SessionAsync(visitor);
        var requested = await RequestAsync(visitor, id);
        var episode = requested.GetProperty("episodeId").GetString()!;
        var waiting = await GetAsync(visitor, VisitorPath(id) + "/waiting");
        var body = new { requestId = "form", expectedVersion = waiting.GetProperty("version").GetInt64(), email = "visitor@example.com", contactName = "访客", content = "请协助处理参展证问题" };
        var form = await SendAsync(visitor, HttpMethod.Post, VisitorPath(id) + "/followup-form", body);
        await SendAsync(visitor, HttpMethod.Post, VisitorPath(id) + "/followup-form", body);
        var path = AgentPath(id) + "/followups/" + episode;
        var summary = await GetAsync(agent, path + "/form");
        Assert.IsFalse(summary.TryGetProperty("content", out _));
        await GetAsync(agent, path + "", HttpStatusCode.Forbidden);
        await SendAsync(agent, HttpMethod.Post, path + "/claim", new { requestId = "form-claim", expectedVersion = 1 });
        await SendAsync(agent, HttpMethod.Post, path + "/reply",
            new { requestId = "reply", expectedVersion = 1, subject = "咨询回复", content = "请携带证件前往服务台。" });
        await SendAsync(agent, HttpMethod.Post, path + "/close", new { requestId = "close", expectedVersion = 2 }, HttpStatusCode.Conflict);
        await RunWorkerAsync();
        var sent = await GetAsync(agent, path + "");
        Assert.AreEqual("SENT", sent.GetProperty("state").GetString());
        await SendAsync(agent, HttpMethod.Post, path + "/close", new { requestId = "close", expectedVersion = sent.GetProperty("version").GetInt64() });
        Assert.IsTrue(application.Notification.Accepted.Contains("reply:" + form.GetProperty("ticketId").GetString()));
    }

    /// <summary>
    /// 网关拒绝按退避有限重试，最终死信不伪装 SENT。
    /// </summary>
    [TestMethod]
    public async Task NotificationRetryExhaustionIsDurable()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        var id = await SessionAsync(visitor);
        await RequestAsync(visitor, id);
        var waiting = await GetAsync(visitor, VisitorPath(id) + "/waiting");
        await SendAsync(visitor, HttpMethod.Post, VisitorPath(id) + "/waiting",
            new { expectedVersion = waiting.GetProperty("version").GetInt64(), action = "EMAIL", email = "visitor@example.com" });
        application.Notification.Reject = true;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await RunWorkerAsync();
            application.Clock.Advance(TimeSpan.FromMinutes(2));
        }
        await using var scope = application.Services.CreateAsyncScope();
        var task = await scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>().Set<ReliableTask>().SingleAsync(x => x.Kind == "EMAIL");
        Assert.AreEqual(DeliveryState.DEAD, task.State);
        Assert.AreEqual(5, task.Attempts);
        Assert.IsNull(task.LeaseId);
    }

    /// <summary>
    /// 真实结束证据才能创建单次调查；不满意先询问，明确同意后才申请人工。
    /// </summary>
    [TestMethod]
    public async Task SurveyRequiresEvidenceAndExplicitOptIn()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        var id = await SessionAsync(visitor);
        await ChatAsync(visitor, id, "请问如何办理参展证？");
        Assert.AreEqual(JsonValueKind.Null, (await GetAsync(visitor, VisitorPath(id) + "/survey")).GetProperty("survey").ValueKind);
        application.Clock.Advance(TimeSpan.FromSeconds(1));
        await ChatAsync(visitor, id, "没有了");
        var survey = (await GetAsync(visitor, VisitorPath(id) + "/survey")).GetProperty("survey");
        var surveyId = survey.GetProperty("surveyId").GetString();
        var answer = await SendAsync(visitor, HttpMethod.Post, VisitorPath(id) + "/survey/" + surveyId + "/answer",
            new { expectedVersion = 1, answer = "DISSATISFIED" });
        Assert.AreEqual(JsonValueKind.Null, (await GetAsync(visitor, VisitorPath(id) + "/handoffs/latest")).GetProperty("episode").ValueKind);
        var choice = new { expectedVersion = answer.GetProperty("version").GetInt64(), choice = "OPTED_IN", email = "visitor@example.com" };
        await SendAsync(visitor, HttpMethod.Post, VisitorPath(id) + "/survey/" + surveyId + "/handoff-choice", choice);
        await SendAsync(visitor, HttpMethod.Post, VisitorPath(id) + "/survey/" + surveyId + "/handoff-choice", choice);
        await using var scope = application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>();
        Assert.AreEqual(1, await db.Set<Survey>().CountAsync());
        Assert.AreEqual(TriggerSource.SURVEY_DISSATISFIED_OPT_IN, (await db.Set<Episode>().SingleAsync()).TriggerSource);
    }

    /// <summary>
    /// 单独致谢不能触发调查；AI 建议卡只有明确确认后才申请。
    /// </summary>
    [TestMethod]
    public async Task AiProposalNeedsConfirmationAndThanksIsNotResolution()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        var id = await SessionAsync(visitor);
        application.Model.Tool = new(null, "proposeHandoff", """{"reasonCode":"KNOWLEDGE_GAP","confidence":0.9,"userFacingReason":"建议由客服核实"}""");
        await ChatAsync(visitor, id, "请问如何办理参展证？");
        var pending = (await GetAsync(visitor, VisitorPath(id) + "/handoff-proposals/pending")).GetProperty("proposals");
        Assert.AreEqual(1, pending.GetArrayLength());
        Assert.AreEqual(JsonValueKind.Null, (await GetAsync(visitor, VisitorPath(id) + "/handoffs/latest")).GetProperty("episode").ValueKind);
        application.Model.Tool = null;
        var proposal = pending[0];
        await SendAsync(visitor, HttpMethod.Post, VisitorPath(id) + "/handoff-proposals/" + proposal.GetProperty("proposalId").GetString() + "/decision",
            new { requestId = "confirm", expectedProposalVersion = 1, decision = "CONFIRM", email = "visitor@example.com" });
        await using var scope = application.Services.CreateAsyncScope();
        Assert.AreEqual(TriggerSource.AI_RECOMMENDATION_CONFIRMED, (await scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>().Set<Episode>().SingleAsync()).TriggerSource);
        var thanksSession = await SessionAsync(visitor);
        await ChatAsync(visitor, thanksSession, "请问如何办理参展证？");
        application.Clock.Advance(TimeSpan.FromSeconds(1));
        await ChatAsync(visitor, thanksSession, "谢谢");
        Assert.AreEqual(JsonValueKind.Null, (await GetAsync(visitor, VisitorPath(thanksSession) + "/survey")).GetProperty("survey").ValueKind);
    }

    /// <summary>
    /// 人工接管后旧 AI epoch 不能提交正文。
    /// </summary>
    [TestMethod]
    public async Task LateAiCompletionIsFenced()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        var id = await SessionAsync(visitor);
        AiTurn turn;
        Scope bound;
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var runtime = scope.ServiceProvider.GetRequiredService<ConversationRuntime>();
            bound = (await runtime.VisitorAsync(new("visitor", "trace"), id, default)).Scope;
            turn = await runtime.BeginAsync(new("visitor", "trace"), bound, "请问如何办理？", default);
        }
        await RequestAsync(visitor, id);
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var completed = await scope.ServiceProvider.GetRequiredService<ConversationRuntime>().FinishAsync(bound, turn.Id, turn.Epoch, "迟到回复", "SUCCEEDED", default);
            Assert.IsFalse(completed);
            var persisted = await scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>().Set<AiTurn>().SingleAsync();
            Assert.AreEqual("FENCED", persisted.Status);
            Assert.IsNull(persisted.AssistantContent);
        }
    }

    /// <summary>
    /// 模型最终失败登记可靠系统任务，恢复时来源为 SYSTEM_POLICY。
    /// </summary>
    [TestMethod]
    public async Task ModelFailureCreatesRecoverableSystemHandoff()
    {
        await application.ConfigureAsync();
        application.Model.Fail = true;
        using var visitor = application.Client();
        var id = await SessionAsync(visitor);
        var stream = await ChatAsync(visitor, id, "请问如何办理参展证？");
        StringAssert.Contains(stream, "event: error");
        await RunWorkerAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var episode = await scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>().Set<Episode>().SingleAsync();
        Assert.AreEqual(TriggerSource.SYSTEM_POLICY, episode.TriggerSource);
    }

    /// <summary>
    /// 真实 WebSocket 首帧登录后收到持久快照，未知身份字段和过期 JWT 以 1008 关闭。
    /// </summary>
    [TestMethod]
    public async Task WebSocketUsesLoginJwtAndRejectsSpoofedFields()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        var id = await SessionAsync(visitor);
        await RequestAsync(visitor, id);
        var client = application.Server.CreateWebSocketClient();
        using var socket = await client.ConnectAsync(new Uri("ws://localhost/api/customer-service/realtime/ws"), default);
        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { authorization = "Bearer " + Token("visitor"), agent = false, conversationId = id, assistantConfigId = "assistant-a", afterSeq = 0 }), WebSocketMessageType.Text, true, default);
        var buffer = new byte[16384];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
        using var snapshot = JsonDocument.Parse(buffer.AsMemory(0, result.Count));
        Assert.AreEqual("snapshot", snapshot.RootElement.GetProperty("type").GetString());
        Assert.AreEqual("REQUESTED", snapshot.RootElement.GetProperty("data").GetProperty("episode").GetProperty("status").GetString());
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "测试完成", timeout.Token);
        var queryToken = Uri.EscapeDataString("Bearer " + Token("visitor"));
        using var querySocket = await client.ConnectAsync(
            new Uri($"ws://localhost/api/customer-service/realtime/ws?authorization={queryToken}&agent=false&conversationId={Uri.EscapeDataString(id)}&assistantConfigId=assistant-a&afterSeq=0"),
            default);
        var queryResult = await querySocket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
        using var querySnapshot = JsonDocument.Parse(buffer.AsMemory(0, queryResult.Count));
        Assert.AreEqual("snapshot", querySnapshot.RootElement.GetProperty("type").GetString());
        await querySocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "测试完成", timeout.Token);
        using var invalid = await client.ConnectAsync(new Uri("ws://localhost/api/customer-service/realtime/ws"), default);
        await invalid.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { authorization = "Bearer " + Token("visitor"), agent = false, conversationId = id, afterSeq = 0, forgedField = "fake" }), WebSocketMessageType.Text, true, default);
        var close = await invalid.ReceiveAsync(buffer.AsMemory(), timeout.Token);
        Assert.AreEqual(WebSocketMessageType.Close, close.MessageType);
        Assert.AreEqual(WebSocketCloseStatus.PolicyViolation, invalid.CloseStatus);
    }

    /// <summary>
    /// 格式化后的原客服静态页面和依赖均可由新服务独立读取。
    /// </summary>
    [TestMethod]
    public async Task StaticPagesAndOpenApiAreServed()
    {
        using var client = application.CreateClient();
        foreach (var path in new[] { "/health", "/customer-service/visitor.html", "/customer-service/agent.html", "/customer-service/routing.html",
                     "/auth/js/auth.js", "/test/js/formal-customer-service-workbench.js", "/openapi/v1.json" })
        {
            using var response = await client.GetAsync(path);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, path);
        }
    }

    /// <summary>
    /// 稳定申请辅助入口，默认请求键仅在单会话内复用。
    /// </summary>
    private static Task<JsonElement> RequestAsync(HttpClient visitor, string id, string key = "handoff") =>
        SendAsync(visitor, HttpMethod.Post, VisitorPath(id) + "/handoffs", new { requestId = key, email = "visitor@example.com" });

    /// <summary>
    /// 访客完整客服资源路径。
    /// </summary>
    private static string VisitorPath(string id) => "/api/customer-service/conversations/" + id;

    /// <summary>
    /// 客服完整资源路径。
    /// </summary>
    private static string AgentPath(string id) => "/api/customer-service/agents/conversations/" + id;

    /// <summary>
    /// 运行单轮正式 Worker 处理，控制租约和网关替身结果。
    /// </summary>
    private async Task RunWorkerAsync()
    {
        await using var scope = application.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReliableTaskProcessor>().RunOnceAsync(default);
    }

    /// <summary>
    /// 通过真实 SSE HTTP 入口消费完整一轮。
    /// </summary>
    private static async Task<string> ChatAsync(HttpClient visitor, string id, string input)
    {
        using var response = await visitor.PostAsJsonAsync("/api/agent/chatStream", new
        {
            sessionId = id,
            userInput = input,
            businessType = "event-assistant",
            extensions = new Dictionary<string, object> { ["event-assistant"] = new { assistantConfigId = "assistant-a" } }
        });
        var content = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, content);
        return content;
    }
}
