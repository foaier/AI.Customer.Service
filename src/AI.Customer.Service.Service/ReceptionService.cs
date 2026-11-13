using AI.Customer.Service.Api.Contracts;
using AI.Customer.Service.Common;
using AI.Customer.Service.Core;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Policies;
using AI.Customer.Service.Domain.Ports;
using AI.Customer.Service.Service.Configuration;
using Microsoft.Extensions.Options;

namespace AI.Customer.Service.Service;

/// <summary>认领、结束、取消和改派短事务，先复核权限再处理幂等回执。</summary>
/// <param name="repository">接待 owner。</param>
/// <param name="configuration">组织级运行与配置 owner。</param>
/// <param name="transactions">跨 owner 共享事务。</param>
/// <param name="runtime">Conversation 回复权端口。</param>
/// <param name="access">当前成员权限。</param>
/// <param name="journal">回执及审计事实。</param>
/// <param name="clock">UTC 时钟。</param>
/// <param name="options">同组织容量。</param>
public sealed class ReceptionService(IReceptionRepository repository, IConfigurationRepository configuration, IUnitOfWork transactions,
    ConversationRuntime runtime, AccessService access, CommandJournal journal, TimeProvider clock, IOptions<CustomerServiceOptions> options)
{
    /// <summary>读取当前或原始接待，无法访问时不泄露其它组织记录。</summary>
    public async Task<Episode> RequireAsync(Scope scope, string? episode, CancellationToken ct) =>
        (episode is null ? await repository.LatestAsync(scope, ct) : await repository.EpisodeAsync(scope, episode, ct))
        ?? throw BusinessException.Forbidden("人工周期不存在或不可访问");

    /// <summary>当前成员只读待接单及本人进行中的接待。</summary>
    public async Task<List<Episode>> QueueAsync(Actor actor, string org, string assistant, CancellationToken ct) =>
        await repository.QueueAsync(actor.TenantId, org, assistant, await access.GroupsAsync(actor, org, ct), actor.AccountId, ct);

    /// <summary>同版本并发认领只有一人成功，默认离线和过期心跳均不阻止手动接单。</summary>
    public Task<object> ClaimAsync(Actor actor, Scope scope, StateCommand command, CancellationToken ct) =>
        transactions.ExecuteAsync(scope.TenantId, scope.TopOrgId, async () =>
        {
            var episode = await RequireAsync(scope, null, ct);
            await access.RequireGroupAsync(actor, episode, ct);
            var prior = await journal.ReplayAsync(scope, actor, "CLAIM", command.RequestId, command, ct);
            if (prior is not null)
            {
                return prior;
            }
            Guard.Version(command.ExpectedVersion, episode.Version);
            var next = ReceptionRules.Transition(episode.Status, "CLAIM");
            await RequireCapacityAsync(actor, scope.TopOrgId, false, ct);
            var conversation = await runtime.RequireAsync(scope, ct);
            if (conversation.HumanEpisodeId != episode.Id)
            {
                throw BusinessException.Conflict("人工回复权已变化");
            }
            episode.Status = next;
            episode.AgentId = actor.AccountId;
            episode.ClaimedAt = clock.GetUtcNow().UtcDateTime;
            episode.Version++;
            return await SaveAsync(actor, episode, "CLAIM", command.RequestId, command, ct);
        }, ct);

    /// <summary>访客取消未接单或认领客服结束已接单，与恢复 AI 原子提交。</summary>
    public Task<object> FinishAsync(Actor actor, Scope scope, StateCommand command, bool visitor, CancellationToken ct) =>
        transactions.ExecuteAsync(scope.TenantId, scope.TopOrgId, async () =>
        {
            var episode = await RequireAsync(scope, null, ct);
            if (visitor)
            {
                if (episode.PrincipalId != actor.AccountId)
                {
                    throw BusinessException.Forbidden();
                }
            }
            else
            {
                await access.RequireGroupAsync(actor, episode, ct);
                ReceptionRules.RequireAssignee(episode, actor.AccountId);
            }
            var action = visitor ? "CANCEL" : "RESOLVE";
            var prior = await journal.ReplayAsync(scope, actor, action, command.RequestId, command, ct);
            if (prior is not null)
            {
                return prior;
            }
            Guard.Version(command.ExpectedVersion, episode.Version);
            episode.Status = ReceptionRules.Transition(episode.Status, action);
            episode.Version++;
            episode.FinishedAt = clock.GetUtcNow().UtcDateTime;
            await runtime.ResumeAiAsync(scope, episode.Id, ct);
            return await SaveAsync(actor, episode, action, command.RequestId, command, ct);
        }, ct);

    /// <summary>同组改派保留原周期及首次来源，目标必须实时外部有效且主动在线有容量。</summary>
    public Task<object> TransferAsync(Actor actor, Scope scope, TransferCommand command, CancellationToken ct) =>
        transactions.ExecuteAsync(scope.TenantId, scope.TopOrgId, async () =>
        {
            var episode = await RequireAsync(scope, null, ct);
            await access.RequireGroupAsync(actor, episode, ct);
            var prior = await journal.ReplayAsync(scope, actor, "TRANSFER", command.RequestId, command, ct);
            if (prior is not null)
            {
                return prior;
            }
            ReceptionRules.RequireAssignee(episode, actor.AccountId);
            Guard.Version(command.ExpectedVersion, episode.Version);
            if (actor.AccountId == command.TargetAccountId)
            {
                throw BusinessException.Invalid("不能改派给自己");
            }
            var target = actor with { AccountId = Guard.Text(command.TargetAccountId, "改派目标") };
            await access.RequireGroupAsync(target, episode, ct);
            await RequireCapacityAsync(target, scope.TopOrgId, true, ct);
            var oldAgent = episode.AgentId;
            episode.Status = ReceptionRules.Transition(episode.Status, "TRANSFER");
            episode.AgentId = target.AccountId;
            episode.Version++;
            await journal.EnqueueAsync(scope, episode.PrincipalId, episode.Id, "EVENT", "transfer:" + episode.Id + ":" + command.RequestId,
                new
                {
                    type = "handoff.transferred",
                    fromAgent = oldAgent,
                    targetAgent = target.AccountId,
                    actorId = actor.AccountId,
                    requestId = command.RequestId,
                    fromVersion = command.ExpectedVersion,
                    toVersion = episode.Version
                }, actor.TraceId, ct);
            return journal.Save(scope, actor, episode.PrincipalId, "TRANSFER", command.RequestId, command, Result(episode, command.RequestId));
        }, ct);

    /// <summary>组织级容量在共享写锁内重新统计；首次运行行默认离线，不伪装上线。</summary>
    private async Task RequireCapacityAsync(Actor actor, string org, bool online, CancellationToken ct)
    {
        var availability = await configuration.AvailabilityAsync(actor.TenantId, org, actor.AccountId, ct);
        if (availability is null)
        {
            availability = new Availability { Id = actor.AccountId, TenantId = actor.TenantId, TopOrgId = org };
            configuration.AddAvailability(availability);
        }
        if (online && (availability.Status != AvailabilityStatus.ONLINE || availability.ExpiresAt <= clock.GetUtcNow().UtcDateTime || availability.ExpiresAt is null))
        {
            throw BusinessException.Conflict("改派目标未在线或心跳已失效");
        }
        if (await repository.ActiveCountAsync(actor.TenantId, org, actor.AccountId, ct) >= options.Value.Capacity)
        {
            throw BusinessException.Conflict("客服同组织接待容量已满");
        }
    }

    /// <summary>状态、首次回执和可靠审计事件同事务保存。</summary>
    private async Task<object> SaveAsync<T>(Actor actor, Episode episode, string action, string key, T input, CancellationToken ct) where T : notnull
    {
        await journal.EnqueueAsync(episode.Scope, episode.PrincipalId, episode.Id, "EVENT", action + ":" + episode.Id + ":" + key,
            new { type = "handoff." + action.ToLowerInvariant(), episodeId = episode.Id, actorId = actor.AccountId }, actor.TraceId, ct);
        return journal.Save(episode.Scope, actor, episode.PrincipalId, action, key, input, Result(episode, key));
    }

    /// <summary>安全命令投影不返回邮箱、内部跟踪或模型数据。</summary>
    private static object Result(Episode episode, string request) =>
        new { requestId = request, episodeId = episode.Id, status = episode.Status, version = episode.Version, replayed = false };
}
