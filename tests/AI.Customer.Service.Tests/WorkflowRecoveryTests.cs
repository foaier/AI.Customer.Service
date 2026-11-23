using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
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

namespace AI.Customer.Service.Tests;

/// <summary>
/// 补充旧代次隔离、历史人工排除、候选证据和正式邮件工作项回归。
/// </summary>
public sealed partial class WorkflowTests
{
    /// <summary>
    /// 客户端读到首个增量后断开会清理生成占用，不自动创建系统人工申请。
    /// </summary>
    [TestMethod]
    public async Task DisposedAiStreamReleasesTurnWithoutSystemHandoff()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        var id = await SessionAsync(visitor);
        await using var scope = application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>();
        var conversation = await db.Set<Conversation>().SingleAsync();
        var stream = scope.ServiceProvider.GetRequiredService<ChatService>().StreamAsync(new("visitor", "disconnect-trace"), conversation.Scope, "请问如何办理参展证？", default).GetAsyncEnumerator();
        Assert.IsTrue(await stream.MoveNextAsync());
        await stream.DisposeAsync();
        Assert.AreEqual("FAILED", (await db.Set<AiTurn>().SingleAsync()).Status);
        Assert.IsNull((await db.Set<Conversation>().SingleAsync()).CurrentTurnId);
        Assert.AreEqual(0, await db.Set<ReliableTask>().CountAsync(x => x.Kind == "SYSTEM_HANDOFF"));
    }

    /// <summary>
    /// 重复完成回调不能将已成功轮次改为失败或覆盖原展示正文。
    /// </summary>
    [TestMethod]
    public async Task CompletedAiTurnIsImmutableOnRepeatedCallback()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        var id = await SessionAsync(visitor);
        await ChatAsync(visitor, id, "请问如何办理参展证？");
        await using var scope = application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>();
        var turn = await db.Set<AiTurn>().SingleAsync();
        var original = turn.AssistantContent;
        var runtime = scope.ServiceProvider.GetRequiredService<ConversationRuntime>();
        Assert.IsFalse(await runtime.FinishAsync(turn.Scope, turn.Id, turn.Epoch, null, "FAILED", default));
        var saved = await db.Set<AiTurn>().SingleAsync();
        Assert.AreEqual("SUCCEEDED", saved.Status);
        Assert.AreEqual(original, saved.AssistantContent);
    }

    /// <summary>
    /// 运营指标按完整助手范围和固定窗口对账，未发生首响不能补零秒样本。
    /// </summary>
    [TestMethod]
    public async Task MetricsUseFullWindowAndAuthenticatedPrometheus()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        using var agent = application.Client("agent-a");
        var id = await SessionAsync(visitor);
        await RequestAsync(visitor, id);
        application.Clock.Advance(TimeSpan.FromMinutes(6));
        var metrics = await GetAsync(agent, "/api/customer-service/agents/metrics?assistantConfigId=assistant-a");
        var reception = metrics.GetProperty("reception");
        Assert.AreEqual(1L, reception.GetProperty("requested").GetProperty("VISITOR_DIRECT_REQUEST:USER_REQUEST").GetInt64());
        Assert.AreEqual(1L, reception.GetProperty("backlog").GetProperty("waiting_over_5m").GetInt64());
        Assert.AreEqual(0L, reception.GetProperty("timings").GetProperty("first_response").GetProperty("count").GetInt64());
        using var response = await agent.GetAsync("/api/customer-service/agents/metrics/prometheus?assistantConfigId=assistant-a");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(text, "# TYPE cs_handoff_backlog gauge");
        StringAssert.Contains(text, "cs_handoff_backlog{kind=\"waiting_over_5m\"} 1");
        Assert.IsFalse(text.Contains(id, StringComparison.Ordinal));
        application.Clock.Advance(TimeSpan.FromDays(2));
        var old = await GetAsync(agent, "/api/customer-service/agents/metrics?assistantConfigId=assistant-a");
        Assert.AreEqual(0, old.GetProperty("reception").GetProperty("requested").EnumerateObject().Count());
        Assert.AreEqual(1L, old.GetProperty("reception").GetProperty("backlog").GetProperty("waiting_over_5m").GetInt64());
    }

    /// <summary>
    /// 数据库拒绝非法并发版本，不能仅靠应用层约定保证版本有效。
    /// </summary>
    [TestMethod]
    public async Task DatabaseRejectsInvalidVersions()
    {
        using var visitor = application.Client();
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>();
            db.Set<Availability>().Add(new Availability { Id = "invalid-version", Version = 0 });
            await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    /// <summary>
    /// 新用户轮次使旧建议失效，查询隐藏旧卡且确认不能创建人工周期。
    /// </summary>
    [TestMethod]
    public async Task NewTurnSupersedesUnconfirmedProposal()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        var id = await SessionAsync(visitor);
        application.Model.Tool = new(null, "proposeHandoff", """{"reasonCode":"KNOWLEDGE_GAP","confidence":0.9,"userFacingReason":"建议由客服核实"}""");
        await ChatAsync(visitor, id, "请问如何办理参展证？");
        var proposal = (await GetAsync(visitor, VisitorPath(id) + "/handoff-proposals/pending")).GetProperty("proposals")[0];
        application.Model.Tool = null;
        application.Clock.Advance(TimeSpan.FromSeconds(1));
        await ChatAsync(visitor, id, "请问展馆在哪里？");
        Assert.AreEqual(0, (await GetAsync(visitor, VisitorPath(id) + "/handoff-proposals/pending")).GetProperty("proposals").GetArrayLength());
        await SendAsync(visitor, HttpMethod.Post, VisitorPath(id) + "/handoff-proposals/" + proposal.GetProperty("proposalId").GetString() + "/decision",
            new { requestId = "stale", expectedProposalVersion = 1, decision = "CONFIRM", email = "visitor@example.com" }, HttpStatusCode.Conflict);
    }

    /// <summary>
    /// 新轮次关闭旧调查的未决选择；历史回答回执仍可恢复而不能创建新副作用。
    /// </summary>
    [TestMethod]
    public async Task NewTurnSupersedesPendingSurveyChoice()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        var id = await SessionAsync(visitor);
        await ChatAsync(visitor, id, "请问如何办理参展证？");
        application.Clock.Advance(TimeSpan.FromSeconds(1));
        await ChatAsync(visitor, id, "已经解决了");
        var survey = (await GetAsync(visitor, VisitorPath(id) + "/survey")).GetProperty("survey");
        var path = VisitorPath(id) + "/survey/" + survey.GetProperty("surveyId").GetString();
        var answer = new { expectedVersion = 1, answer = "DISSATISFIED" };
        var result = await SendAsync(visitor, HttpMethod.Post, path + "/answer", answer);
        application.Clock.Advance(TimeSpan.FromSeconds(1));
        await ChatAsync(visitor, id, "现在我想咨询展馆地址？");
        Assert.AreEqual(JsonValueKind.Null, (await GetAsync(visitor, VisitorPath(id) + "/survey")).GetProperty("survey").ValueKind);
        await SendAsync(visitor, HttpMethod.Post, path + "/answer", answer);
        await SendAsync(visitor, HttpMethod.Post, path + "/handoff-choice",
            new { expectedVersion = result.GetProperty("version").GetInt64(), choice = "OPTED_IN", email = "visitor@example.com" }, HttpStatusCode.Conflict);
    }

    /// <summary>
    /// 曾申请人工的会话在取消后恢复 AI，也不能进入纯 AI 满意度调查。
    /// </summary>
    [TestMethod]
    public async Task CancelledHumanHistoryExcludesAiSurvey()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        var id = await SessionAsync(visitor);
        await RequestAsync(visitor, id);
        await SendAsync(visitor, HttpMethod.Post, VisitorPath(id) + "/reception/cancel", new { requestId = "cancel", expectedVersion = 1 });
        await ChatAsync(visitor, id, "请问如何办理参展证？");
        application.Clock.Advance(TimeSpan.FromSeconds(1));
        await ChatAsync(visitor, id, "已经解决了");
        Assert.AreEqual(JsonValueKind.Null, (await GetAsync(visitor, VisitorPath(id) + "/survey")).GetProperty("survey").ValueKind);
    }

    /// <summary>
    /// 模型候选不是用户结束证据，同轮同参数幂等且不允许覆写首次置信度。
    /// </summary>
    [TestMethod]
    public async Task ResolutionCandidateIsEvidenceOnlyAndImmutable()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        var id = await SessionAsync(visitor);
        application.Model.Tool = new(null, "proposeResolution", """{"confidence":0.95}""");
        await ChatAsync(visitor, id, "请问如何办理参展证？");
        Assert.AreEqual(JsonValueKind.Null, (await GetAsync(visitor, VisitorPath(id) + "/survey")).GetProperty("survey").ValueKind);
        await using var scope = application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>();
        var turn = await db.Set<AiTurn>().SingleAsync();
        var service = scope.ServiceProvider.GetRequiredService<SurveyService>();
        var evidence = await service.RecordCandidateAsync(turn.Scope, turn, .95, default);
        Assert.IsNotNull(evidence);
        await Assert.ThrowsExactlyAsync<BusinessException>(() => service.RecordCandidateAsync(turn.Scope, turn, .96, default));
        Assert.AreEqual(1, await db.Set<ResolutionEvidence>().CountAsync());
    }

    /// <summary>
    /// 崩溃后的成功轮次可按持久游标重评估；没有用户结束证据时任意沉默都不建调查。
    /// </summary>
    [TestMethod]
    public async Task SurveyRecoveryDoesNotUseIdleTime()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        var id = await SessionAsync(visitor);
        await ChatAsync(visitor, id, "请问如何办理参展证？");
        application.Clock.Advance(TimeSpan.FromDays(10));
        await using var scope = application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>();
        var conversation = await db.Set<Conversation>().SingleAsync();
        conversation.SurveyCheckedTurnId = null;
        await db.SaveChangesAsync();
        var repository = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
        Assert.AreEqual(1, (await repository.SurveyCandidatesAsync(100, default)).Count);
        Assert.IsNull(await scope.ServiceProvider.GetRequiredService<SurveyService>().EvaluateAsync(conversation.Scope, default));
        Assert.AreEqual(0, (await repository.SurveyCandidatesAsync(100, default)).Count);
    }

    /// <summary>
    /// 迟到系统故障任务不能接管随后已经成功的 AI 轮次。
    /// </summary>
    [TestMethod]
    public async Task OldSystemFailureCannotTakeNewTurn()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        var id = await SessionAsync(visitor);
        application.Model.Fail = true;
        await ChatAsync(visitor, id, "请问如何办理参展证？");
        application.Model.Fail = false;
        application.Clock.Advance(TimeSpan.FromSeconds(1));
        await ChatAsync(visitor, id, "请问展馆在哪里？");
        await RunWorkerAsync();
        Assert.AreEqual(JsonValueKind.Null, (await GetAsync(visitor, VisitorPath(id) + "/handoffs/latest")).GetProperty("episode").ValueKind);
    }

    /// <summary>
    /// 只留邮箱的正式工单可认领且正文保持为空；死信通知重试保留投递键。
    /// </summary>
    [TestMethod]
    public async Task EmailOnlyCreatesClaimableWorkAndNotificationCanRecover()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        using var agent = application.Client("agent-a");
        var id = await SessionAsync(visitor);
        var requested = await RequestAsync(visitor, id);
        var episode = requested.GetProperty("episodeId").GetString()!;
        var waiting = await GetAsync(visitor, VisitorPath(id) + "/waiting");
        await SendAsync(visitor, HttpMethod.Post, VisitorPath(id) + "/waiting", new { expectedVersion = waiting.GetProperty("version").GetInt64(), action = "EMAIL", email = "visitor@example.com" });
        var queue = await GetAsync(agent, "/api/customer-service/agents/followups?assistantConfigId=assistant-a");
        Assert.AreEqual(1, queue.GetArrayLength());
        var path = AgentPath(id) + "/followups/" + episode;
        await SendAsync(agent, HttpMethod.Post, path + "/claim", new { requestId = "email-claim", expectedVersion = 1 });
        var form = await GetAsync(agent, path + "/form");
        Assert.AreEqual("EMAIL", form.GetProperty("origin").GetString());
        Assert.AreEqual("", form.GetProperty("content").GetString());
        application.Notification.Reject = true;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await RunWorkerAsync();
            application.Clock.Advance(TimeSpan.FromMinutes(2));
        }
        var query = "?episodeId=" + episode;
        var dead = await GetAsync(agent, AgentPath(id) + "/waiting" + query);
        Assert.AreEqual("DEAD", dead.GetProperty("deliveries")[0].GetProperty("status").GetString());
        Assert.IsFalse(dead.TryGetProperty("email", out _));
        await SendAsync(agent, HttpMethod.Post, AgentPath(id) + "/notifications/retry" + query, new { });
        application.Notification.Reject = false;
        await RunWorkerAsync();
        var sent = await GetAsync(visitor, VisitorPath(id) + "/waiting");
        Assert.AreEqual("SENT", sent.GetProperty("deliveries")[0].GetProperty("status").GetString());
        await using var auditScope = application.Services.CreateAsyncScope();
        var task = await auditScope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>().Set<ReliableTask>().SingleAsync(x => x.Kind == "EMAIL");
        Assert.AreEqual(6L, task.TotalAttempts);
        Assert.AreEqual(1L, task.ManualRetryCount);
        Assert.AreEqual("agent-a", task.LastRetriedBy);
        Assert.IsNotNull(task.LastRetriedAt);
    }

    /// <summary>
    /// 收件箱汇总按真实访客消息、持久READ和独立留言SEEN回执变化。
    /// </summary>
    [TestMethod]
    public async Task InboxSummaryTracksUnreadAndFollowupReceipts()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        using var agent = application.Client("agent-a");
        var id = await SessionAsync(visitor);
        await RequestAsync(visitor, id, "inbox-request");
        const string summaryPath = "/api/customer-service/agents/inbox/summary?assistantConfigId=assistant-a";
        var summary = await GetAsync(agent, summaryPath);
        Assert.AreEqual(1, summary.GetProperty("pendingReceptionCount").GetInt32());
        var socketClient = application.Server.CreateWebSocketClient();
        var token = Uri.EscapeDataString("Bearer " + Token("agent-a"));
        using (var socket = await socketClient.ConnectAsync(new Uri($"ws://localhost/api/customer-service/realtime/ws?authorization={token}&agent=true&assistantConfigId=assistant-a&afterSeq=0"), default))
        {
            var buffer = new byte[16384];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var snapshotResult = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
            using var snapshot = JsonDocument.Parse(buffer.AsMemory(0, snapshotResult.Count));
            Assert.AreEqual("snapshot", snapshot.RootElement.GetProperty("type").GetString());
            var inboxResult = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
            using var inboxFrame = JsonDocument.Parse(buffer.AsMemory(0, inboxResult.Count));
            Assert.AreEqual("inbox.notifications", inboxFrame.RootElement.GetProperty("type").GetString());
            Assert.AreEqual(1, inboxFrame.RootElement.GetProperty("data").GetProperty("pendingReceptionCount").GetInt32());
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "测试完成", timeout.Token);
        }

        await SendAsync(agent, HttpMethod.Post, AgentPath(id) + "/claim", new { requestId = "inbox-claim", expectedVersion = 1 });
        await SendAsync(visitor, HttpMethod.Post, VisitorPath(id) + "/messages", new { clientMsgId = "visitor-unread", content = "请帮我看一下订单" });
        summary = await GetAsync(agent, summaryPath);
        Assert.AreEqual(1, summary.GetProperty("unreadMessageCount").GetInt32());
        Assert.AreEqual(1, summary.GetProperty("unreadConversationCount").GetInt32());
        await SendAsync(agent, HttpMethod.Post, AgentPath(id) + "/receipts", new { kind = "READ", seq = 1L });
        summary = await GetAsync(agent, summaryPath);
        Assert.AreEqual(0, summary.GetProperty("unreadMessageCount").GetInt32());

        var followupId = await SessionAsync(visitor);
        var handoff = await RequestAsync(visitor, followupId, "followup-request");
        var episodeId = handoff.GetProperty("episodeId").GetString()!;
        var waiting = await GetAsync(visitor, VisitorPath(followupId) + "/waiting");
        var form = await SendAsync(visitor, HttpMethod.Post, VisitorPath(followupId) + "/followup-form", new
        {
            requestId = "followup-form",
            expectedVersion = waiting.GetProperty("version").GetInt64(),
            email = "visitor@example.com",
            contactName = "访客",
            content = "请通过邮箱继续跟进"
        });
        var ticketId = form.GetProperty("ticketId").GetString()!;
        summary = await GetAsync(agent, summaryPath);
        Assert.AreEqual(1, summary.GetProperty("newFollowupCount").GetInt32());
        await SendAsync(agent, HttpMethod.Post,
            AgentPath(followupId) + "/followups/" + episodeId + "/receipts",
            new { kind = "SEEN", ticketId });
        summary = await GetAsync(agent, summaryPath);
        Assert.AreEqual(0, summary.GetProperty("newFollowupCount").GetInt32());
    }

    /// <summary>
    /// 客服回复启动访客超时结单，迟到新消息结案且原消息重试仍幂等。
    /// </summary>
    [TestMethod]
    public async Task VisitorInactivityAutoClosesActiveReception()
    {
        await application.ConfigureAsync();
        using var visitor = application.Client();
        using var agent = application.Client("agent-a");
        var id = await SessionAsync(visitor);
        await RequestAsync(visitor, id, "auto-close-request");
        await SendAsync(agent, HttpMethod.Post, AgentPath(id) + "/claim", new { requestId = "auto-close-claim", expectedVersion = 1 });
        var reply = new { clientMsgId = "auto-close-reply", content = "我来帮您处理" };
        var original = await SendAsync(agent, HttpMethod.Post, AgentPath(id) + "/messages", reply);
        var reception = await GetAsync(visitor, VisitorPath(id) + "/handoffs/latest");
        Assert.IsNotNull(reception.GetProperty("episode").GetProperty("autoCloseAt").GetString());

        application.Clock.Advance(TimeSpan.FromMinutes(5));
        await SendAsync(visitor, HttpMethod.Post, VisitorPath(id) + "/messages", new { clientMsgId = "late-visitor", content = "还在吗" }, HttpStatusCode.Conflict);
        var replay = await SendAsync(agent, HttpMethod.Post, AgentPath(id) + "/messages", reply);
        Assert.AreEqual(original.GetProperty("seq").GetInt64(), replay.GetProperty("seq").GetInt64());
        var ended = await GetAsync(visitor, VisitorPath(id) + "/handoffs/latest");
        Assert.AreEqual("RESOLVED", ended.GetProperty("episode").GetProperty("status").GetString());
        Assert.AreEqual("VISITOR_INACTIVE_5M", ended.GetProperty("episode").GetProperty("resolutionReason").GetString());
        Assert.AreEqual(JsonValueKind.Null, ended.GetProperty("episode").GetProperty("autoCloseAt").ValueKind);
    }
}
