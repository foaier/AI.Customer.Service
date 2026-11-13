using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AI.Customer.Service.Common;
using AI.Customer.Service.Core;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Policies;
using AI.Customer.Service.Domain.Ports;
using Microsoft.Extensions.Logging;

namespace AI.Customer.Service.Service;

/// <summary>AI 文本生成编排，网络调用在事务外，受限工具不得直接覆盖人工事实。</summary>
/// <param name="runtime">会话 owner 的轮次围栏。</param>
/// <param name="conversations">真实历史读取端口。</param>
/// <param name="model">模型协议端口。</param>
/// <param name="handoff">受限人工申请工具应用端口。</param>
/// <param name="surveys">真实结束证据门禁。</param>
/// <param name="journal">系统故障任务登记。</param>
/// <param name="transactions">短事务。</param>
/// <param name="logger">结构化日志。</param>
public sealed class ChatService(ConversationRuntime runtime, IConversationRepository conversations, IChatModel model,
    HandoffService handoff, SurveyService surveys, CommandJournal journal, IUnitOfWork transactions, ILogger<ChatService> logger)
{
    /// <summary>同会话生成 AI SSE 事件，失败有明确事件及可靠系统恢复，不写入迟到正文。</summary>
    public async IAsyncEnumerable<ChatEvent> StreamAsync(Actor actor, Scope scope, string input, [EnumeratorCancellation] CancellationToken ct)
    {
        if (ExplicitHandoffIntent.Matches(input))
        {
            var result = await handoff.RequestAsync(actor, scope, Guard.Id("direct"), TriggerSource.VISITOR_DIRECT_REQUEST, ReasonCode.USER_REQUEST, ct);
            yield return new("card", new { status = "handoff.requested", data = result });
            yield break;
        }
        var turn = await runtime.BeginAsync(actor, scope, input, ct);
        var finalized = false;
        try
        {
            var history = (await conversations.TurnsAsync(scope, 21, ct)).Where(t => t.Id != turn.Id && t.Status == "SUCCEEDED").TakeLast(20).ToList();
            var text = new StringBuilder();
            var tools = new List<ModelDelta>();
            Exception? failure = null;
            await using (var source = model.StreamAsync(new(scope, turn.Id, actor.TraceId, history, input), ct).GetAsyncEnumerator(ct))
            {
                while (true)
                {
                    ModelDelta? delta = null;
                    try
                    {
                        if (!await source.MoveNextAsync())
                        {
                            break;
                        }
                        delta = source.Current;
                        if (!await runtime.IsCurrentAsync(scope, turn.Id, turn.Epoch, ct))
                        {
                            break;
                        }
                    }
                    catch (Exception error) when (error is HttpRequestException or JsonException or BusinessException or OperationCanceledException or InvalidOperationException)
                    {
                        failure = error;
                        break;
                    }
                    if (delta.Text is not null)
                    {
                        text.Append(delta.Text);
                        if (text.Length > 65535)
                        {
                            failure = BusinessException.Unavailable("模型回答超出长度限制");
                            break;
                        }
                        yield return new("message", new { v = delta.Text, turnId = turn.Id });
                    }
                    if (delta.ToolName is not null)
                    {
                        tools.Add(delta);
                    }
                }
            }
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            if (failure is not null || text.Length == 0 && tools.Count == 0)
            {
                await runtime.FinishAsync(scope, turn.Id, turn.Epoch, null, "FAILED", cleanup.Token);
                finalized = true;
                if (!ct.IsCancellationRequested)
                {
                    await RegisterFailureAsync(actor, scope, turn, cleanup.Token);
                }
                logger.LogWarning("AI轮次失败 SessionId={SessionId} TurnId={TurnId} TraceId={TraceId} TopOrgId={TopOrgId} BventId={BventId} Failure={Failure}",
                    scope.ConversationId, turn.Id, actor.TraceId, scope.TopOrgId, scope.BventId, failure?.GetType().Name ?? "EMPTY");
                yield return new("error", new { code = "DEPENDENCY_UNAVAILABLE", message = "AI 暂不可用，人工恢复任务已登记", turnId = turn.Id });
                yield break;
            }
            var completed = await runtime.FinishAsync(scope, turn.Id, turn.Epoch, text.Length == 0 ? "您可以选择人工协助。" : text.ToString(), "SUCCEEDED", cleanup.Token);
            finalized = true;
            if (!completed)
            {
                yield return new("status", new { status = "generation.fenced", turnId = turn.Id });
                yield break;
            }
            foreach (var tool in tools)
            {
                var proposal = await ApplyToolAsync(scope, turn, tool, cleanup.Token);
                if (proposal is not null)
                {
                    yield return new("card", new { status = "handoff.proposed", data = ProposalView(proposal) });
                }
            }
            try
            {
                await surveys.EvaluateAsync(scope, cleanup.Token);
            }
            catch (Exception error) when (error is BusinessException or InvalidOperationException)
            {
                logger.LogWarning("调查资格待恢复 SessionId={SessionId} TurnId={TurnId} TraceId={TraceId} Failure={Failure}",
                    scope.ConversationId, turn.Id, actor.TraceId, error.GetType().Name);
            }
            yield return new("complete", new { turnId = turn.Id });
        }
        finally
        {
            if (!finalized)
            {
                await AbandonTurnAsync(actor, scope, turn);
            }
        }
    }

    /// <summary>客户端断流或提前释放迭代器时独立清理轮次，不把主动取消登记为系统转人工。</summary>
    private async Task AbandonTurnAsync(Actor actor, Scope scope, AiTurn turn)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await runtime.FinishAsync(scope, turn.Id, turn.Epoch, null, "FAILED", timeout.Token);
        }
        catch (Exception error)
        {
            logger.LogWarning("取消轮次待租约恢复 SessionId={SessionId} TurnId={TurnId} TraceId={TraceId} TopOrgId={TopOrgId} BventId={BventId} Failure={Failure}",
                scope.ConversationId, turn.Id, actor.TraceId, scope.TopOrgId, scope.BventId, error.GetType().Name);
        }
    }

    /// <summary>工具严格字段白名单，非法工具或非法参数仅忽略候选，不破坏成功主回答。</summary>
    private async Task<Proposal?> ApplyToolAsync(Scope scope, AiTurn turn, ModelDelta tool, CancellationToken ct)
    {
        if (tool.ToolName is "proposeResolution" or "propose_resolution")
        {
            try
            {
                using var candidate = JsonDocument.Parse(tool.Arguments ?? "{}");
                var root = candidate.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.EnumerateObject().All(x => x.Name == "confidence"))
                {
                    await surveys.RecordCandidateAsync(scope, turn, root.GetProperty("confidence").GetDouble(), ct);
                }
            }
            catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or BusinessException)
            {
                // 模型候选错误仅忽略，不回滚已提交的主回答。
            }
            return null;
        }
        if (tool.ToolName != "proposeHandoff")
        {
            return null;
        }
        try
        {
            using var json = JsonDocument.Parse(tool.Arguments ?? "{}");
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(x => x.Name is not "reasonCode" and not "confidence" and not "userFacingReason") ||
                !Enum.TryParse<ReasonCode>(root.GetProperty("reasonCode").GetString(), out var reason))
            {
                return null;
            }
            return await handoff.ProposeAsync(scope, turn.Id, turn.Epoch, reason, root.GetProperty("confidence").GetDouble(),
                root.GetProperty("userFacingReason").GetString() ?? "", ct);
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or BusinessException)
        {
            return null;
        }
    }

    /// <summary>可信最终故障在短事务内登记任务，Worker 恢复时再尝试人工申请。</summary>
    private Task<bool> RegisterFailureAsync(Actor actor, Scope scope, AiTurn turn, CancellationToken ct) =>
        transactions.ExecuteAsync(scope.TenantId, scope.TopOrgId, async () =>
        {
            await journal.EnqueueAsync(scope, turn.PrincipalId, "", "SYSTEM_HANDOFF", "system:" + turn.Id,
                new { type = "AI_FINAL_FAILURE", turnId = turn.Id, epoch = turn.Epoch }, actor.TraceId, ct);
            return true;
        }, ct);

    /// <summary>卡片安全字段与 Java 前端协议一致。</summary>
    public static object ProposalView(Proposal proposal) => new
    {
        proposalId = proposal.Id,
        proposal.ReasonCode,
        proposal.UserFacingReason,
        proposal.Confidence,
        proposal.Status,
        proposal.Version,
        proposal.ExpiresAt
    };
}

/// <summary>与传输协议无关的 AI 流事件。</summary>
/// <param name="Event">message、card、error 或 complete。</param>
/// <param name="Data">由 Web SSE Adapter 序列化的业务投影。</param>
public sealed record ChatEvent(string Event, object Data);
