using AI.Customer.Service.Api.Contracts;
using AI.Customer.Service.Common;
using AI.Customer.Service.Core;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;
using AI.Customer.Service.Service.Configuration;
using Microsoft.Extensions.Options;

namespace AI.Customer.Service.Service;

/// <summary>转人工申请和建议确认的应用编排，分类在事务外、路由冻结在事务内。</summary>
/// <param name="repository">客服申请 owner。</param>
/// <param name="configuration">本地路由 owner。</param>
/// <param name="transactions">共享事务。</param>
/// <param name="runtime">Conversation 回复权端口。</param>
/// <param name="model">主题语义分类端口。</param>
/// <param name="conversations">最小真实历史端口。</param>
/// <param name="journal">幂等回执及可靠事件。</param>
/// <param name="clock">UTC 时钟。</param>
/// <param name="options">建议有效期规则。</param>
public sealed class HandoffService(IReceptionRepository repository, IConfigurationRepository configuration, IUnitOfWork transactions,
    ConversationRuntime runtime, IChatModel model, IConversationRepository conversations, CommandJournal journal,
    TimeProvider clock, IOptions<CustomerServiceOptions> options)
{
    /// <summary>访客明确申请不经过建议二次确认；同键重放不重复切换回复权。</summary>
    public async Task<object> RequestAsync(Actor actor, Scope scope, string request, TriggerSource source, ReasonCode reason, CancellationToken ct, string? failedTurnId = null)
    {
        var classification = await PrepareAsync(scope, ct);
        return await transactions.ExecuteAsync(scope.TenantId, scope.TopOrgId, async () =>
        {
            var input = new { requestId = request, source, reason };
            var prior = await journal.ReplayAsync(scope, actor, "REQUEST", request, input, ct);
            if (prior is not null)
            {
                return prior;
            }
            var conversation = await runtime.RequireAsync(scope, ct);
            if (source == TriggerSource.SYSTEM_POLICY)
            {
                var failed = failedTurnId is null ? null : await conversations.TurnAsync(scope, failedTurnId, ct);
                if (failed?.Status != "FAILED" || conversation.Epoch != failed.Epoch || conversation.HumanEpisodeId is not null || conversation.CurrentTurnId is not null)
                {
                    return new { skipped = true, reason = "SOURCE_SUPERSEDED" };
                }
            }
            var episode = await CreateInTransactionAsync(scope, conversation.PrincipalId, source, reason, classification, actor.TraceId, ct);
            var result = new { requestId = request, episodeId = episode.Id, status = episode.Status, version = episode.Version, replayed = false };
            return journal.Save(scope, actor, conversation.PrincipalId, "REQUEST", request, input, result);
        }, ct);
    }

    /// <summary>读取最多二十张有效建议，不创建申请。</summary>
    public async Task<List<Proposal>> PendingAsync(Scope scope, CancellationToken ct)
    {
        var conversation = await runtime.RequireAsync(scope, ct);
        return (await repository.PendingAsync(scope, clock.GetUtcNow().UtcDateTime, ct))
            .Where(p => conversation.HumanEpisodeId is null && p.Epoch == conversation.Epoch).ToList();
    }

    /// <summary>读取最新人工周期，返回空时不产生写入。</summary>
    public Task<Episode?> LatestAsync(Scope scope, CancellationToken ct) => repository.LatestAsync(scope, ct);

    /// <summary>访客决定建议卡，确认才创建申请，拒绝只保存选择。</summary>
    public async Task<object> DecideAsync(Actor actor, Scope scope, string proposalId, ProposalDecision command, CancellationToken ct)
    {
        if (command.Decision is not "CONFIRM" and not "DISMISS")
        {
            throw BusinessException.Invalid("建议选择只能为 CONFIRM 或 DISMISS");
        }
        var classification = await PrepareAsync(scope, ct);
        return await transactions.ExecuteAsync(scope.TenantId, scope.TopOrgId, async () =>
        {
            var prior = await journal.ReplayAsync(scope, actor, "PROPOSAL:" + proposalId, command.RequestId, command, ct);
            if (prior is not null)
            {
                return prior;
            }
            var proposal = await repository.ProposalAsync(scope, proposalId, ct) ?? throw BusinessException.Forbidden("建议不存在");
            Guard.Version(command.ExpectedProposalVersion, proposal.Version);
            if (proposal.Status != ProposalStatus.PENDING || proposal.ExpiresAt <= clock.GetUtcNow().UtcDateTime)
            {
                throw BusinessException.Conflict("建议已处理或过期");
            }
            var conversation = await runtime.RequireAsync(scope, ct);
            if (conversation.Epoch != proposal.Epoch || conversation.HumanEpisodeId is not null)
            {
                throw BusinessException.Conflict("会话代次已变化");
            }
            Episode? episode = null;
            if (command.Decision == "CONFIRM")
            {
                episode = await CreateInTransactionAsync(scope, conversation.PrincipalId, TriggerSource.AI_RECOMMENDATION_CONFIRMED,
                    proposal.ReasonCode, classification, actor.TraceId, ct);
            }
            proposal.Status = command.Decision == "CONFIRM" ? ProposalStatus.CONFIRMED : ProposalStatus.DISMISSED;
            proposal.Version++;
            return journal.Save(scope, actor, conversation.PrincipalId, "PROPOSAL:" + proposalId, command.RequestId, command,
                new { proposalId, status = proposal.Status, version = proposal.Version, episodeId = episode?.Id, replayed = false });
        }, ct);
    }

    /// <summary>受限工具保存建议，绑定服务端轮次及 epoch；模型不能指定身份、组或接收者。</summary>
    public Task<Proposal?> ProposeAsync(Scope scope, string turn, long epoch, ReasonCode reason, double confidence, string explanation, CancellationToken ct)
    {
        if (!Enum.IsDefined(reason) || reason is ReasonCode.SYSTEM_FAILURE or ReasonCode.SAFETY_OR_URGENT or ReasonCode.SURVEY_DISSATISFIED or ReasonCode.USER_REQUEST ||
            !double.IsFinite(confidence) || confidence < .75 || confidence > 1 || string.IsNullOrWhiteSpace(explanation) ||
            explanation.Length > 500 || explanation.Contains("://", StringComparison.Ordinal))
        {
            return Task.FromResult<Proposal?>(null);
        }
        return transactions.ExecuteAsync<Proposal?>(scope.TenantId, scope.TopOrgId, async () =>
        {
            var conversation = await runtime.RequireAsync(scope, ct);
            if (conversation.HumanEpisodeId is not null || conversation.Epoch != epoch)
            {
                return null;
            }
            var sourceTurn = await conversations.TurnAsync(scope, turn, ct);
            if (sourceTurn?.Status != "SUCCEEDED" || sourceTurn.Epoch != epoch)
            {
                return null;
            }
            var pending = (await repository.PendingAsync(scope, clock.GetUtcNow().UtcDateTime, ct)).Where(p => p.Epoch == epoch).ToList();
            if (pending.Count > 0)
            {
                return pending[0];
            }
            var proposal = ConversationRuntime.Bind<Proposal>(scope, conversation.PrincipalId, Guard.Id("proposal"));
            proposal.TurnId = turn;
            proposal.Epoch = epoch;
            proposal.ReasonCode = reason;
            proposal.Confidence = confidence;
            proposal.UserFacingReason = explanation;
            proposal.CreatedAt = clock.GetUtcNow().UtcDateTime;
            proposal.ExpiresAt = proposal.CreatedAt.AddMinutes(options.Value.ProposalTtlMinutes);
            repository.AddProposal(proposal);
            await journal.EnqueueAsync(scope, conversation.PrincipalId, "", "EVENT", "proposal:" + proposal.Id,
                new { type = "handoff.proposed", proposalId = proposal.Id, turnId = turn }, sourceTurn.TraceId, ct);
            return proposal;
        }, ct);
    }

    /// <summary>共享事务内创建周期、冻结路由并原子取得回复权，供调查和系统任务编排复用。</summary>
    public async Task<Episode> CreateInTransactionAsync(Scope scope, string principal, TriggerSource source, ReasonCode reason,
        Classification classification, string trace, CancellationToken ct)
    {
        var config = await configuration.RoutingAsync(scope.TenantId, scope.TopOrgId, ct);
        var route = config?.Routes.SingleOrDefault(r => r.AssistantConfigId == scope.AssistantConfigId);
        if (route is null || !route.Groups.TryGetValue(classification.Category, out var group) || !config!.Groups.Any(g => g.Id == group))
        {
            throw BusinessException.Conflict("助手尚未保存完整客服路由");
        }
        var episode = ConversationRuntime.Bind<Episode>(scope, principal, Guard.Id("episode"));
        episode.TriggerSource = source;
        episode.ReasonCode = reason;
        episode.GroupId = group;
        episode.Category = classification.Category;
        episode.ConfigVersion = config.Version;
        episode.ClassificationSource = classification.Source;
        episode.RequestedAt = clock.GetUtcNow().UtcDateTime;
        episode.TraceId = trace;
        await runtime.TakeHumanAsync(scope, episode.Id, ct);
        repository.Add(episode);
        await journal.EnqueueAsync(scope, principal, episode.Id, "EVENT", "requested:" + episode.Id,
            new { type = "handoff.requested", episodeId = episode.Id, source, reason, turnId = (string?)null }, trace, ct);
        return episode;
    }

    /// <summary>事务外根据最近成功历史识别主题，模型失败只能降 OTHER，不阻断明确申请。</summary>
    public async Task<Classification> PrepareAsync(Scope scope, CancellationToken ct)
    {
        var turns = await conversations.TurnsAsync(scope, 10, ct);
        var context = string.Join("\n", turns.Select(t => t.UserContent));
        return string.IsNullOrWhiteSpace(context)
            ? new(Category.OTHER, 0, "CONTEXT_MISSING")
            : await model.ClassifyAsync(scope, context.Length > 16000 ? context[^16000..] : context, ct);
    }
}
