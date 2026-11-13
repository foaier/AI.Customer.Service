using AI.Customer.Service.Api.Contracts;
using AI.Customer.Service.Common;
using AI.Customer.Service.Core;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Policies;
using AI.Customer.Service.Domain.Ports;

namespace AI.Customer.Service.Service;

/// <summary>按当前会话访问恢复等待期限，禁止全库定时扫描隐式关闭无人访问的请求。</summary>
/// <param name="repository">等待及周期 owner。</param>
/// <param name="configuration">组织日历应用端口。</param>
/// <param name="transactions">共享事务。</param>
/// <param name="runtime">回复权端口。</param>
/// <param name="journal">可靠提醒登记。</param>
/// <param name="clock">UTC 时钟。</param>
/// <param name="followups">同客服 owner 的正式跟进工作项。</param>
/// <param name="tasks">仅返回安全投递摘要。</param>
/// <param name="access">客服原组权限核验。</param>
public sealed class WaitingService(IReceptionRepository repository, ConfigurationService configuration, IUnitOfWork transactions,
    ConversationRuntime runtime, CommandJournal journal, TimeProvider clock, IFollowupRepository followups, ITaskRepository tasks, AccessService access)
{
    /// <summary>读取时初始化并按需补算期限，响应不包含邮箱。</summary>
    public Task<object?> GetAsync(Actor actor, Scope scope, CancellationToken ct) =>
        transactions.ExecuteAsync<object?>(scope.TenantId, scope.TopOrgId, async () =>
        {
            var episode = await repository.LatestAsync(scope, ct);
            if (episode is null)
            {
                return null;
            }
            var waiting = await RefreshInTransactionAsync(episode, actor.TraceId, ct);
            return await ViewAsync(waiting, ct);
        }, ct);

    /// <summary>访客继续等待、保存邮箱或转邮件，使用独立等待版本。</summary>
    public Task<object> ChooseAsync(Actor actor, Scope scope, WaitingChoice command, CancellationToken ct) =>
        transactions.ExecuteAsync(scope.TenantId, scope.TopOrgId, async () =>
        {
            var episode = await repository.LatestAsync(scope, ct) ?? throw BusinessException.Forbidden();
            var waiting = await RefreshInTransactionAsync(episode, actor.TraceId, ct);
            Guard.Version(command.ExpectedVersion, waiting.Version);
            switch (command.Action)
            {
                case "CONTINUE":
                    if (waiting.State != WaitingState.CONTINUING)
                    {
                        ReceptionRules.ChangeWaiting(waiting, WaitingState.CONTINUING);
                    }
                    break;
                case "CONTACT":
                    if (waiting.State is not WaitingState.WAITING and not WaitingState.BUSY and not WaitingState.CONTINUING)
                    {
                        throw BusinessException.Conflict("当前不处于在线等待");
                    }
                    var email = Guard.Email(command.Email);
                    if (waiting.Email != email)
                    {
                        waiting.Email = email;
                        waiting.Version++;
                    }
                    break;
                case "EMAIL":
                    await EmailInTransactionAsync(episode, waiting, command.Email, actor.TraceId, ct);
                    break;
                default:
                    throw BusinessException.Invalid("等待选择必须为 CONTINUE、CONTACT 或 EMAIL");
            }
            return await ViewAsync(waiting, ct);
        }, ct);

    /// <summary>客服按原周期恢复等待与安全通知摘要，仍需当前原组权限。</summary>
    public Task<object?> GetAgentAsync(Actor actor, Scope scope, string? episodeId, CancellationToken ct) =>
        transactions.ExecuteAsync<object?>(scope.TenantId, scope.TopOrgId, async () =>
        {
            var episode = episodeId is null ? await repository.LatestAsync(scope, ct) : await repository.EpisodeAsync(scope, episodeId, ct);
            if (episode is null)
            {
                return null;
            }
            await access.RequireGroupAsync(actor, episode, ct);
            return await ViewAsync(await RefreshInTransactionAsync(episode, actor.TraceId, ct), ct);
        }, ct);

    /// <summary>只恢复原周期 DEAD 邮件或内部通知，保留首次 eventKey 和载荷。</summary>
    public Task<object> RetryNotificationsAsync(Actor actor, Scope scope, string? episodeId, CancellationToken ct) =>
        transactions.ExecuteAsync<object>(scope.TenantId, scope.TopOrgId, async () =>
        {
            var episode = episodeId is null ? await repository.LatestAsync(scope, ct) : await repository.EpisodeAsync(scope, episodeId, ct);
            if (episode is null)
            {
                throw BusinessException.Forbidden("原接待周期不存在");
            }
            await access.RequireGroupAsync(actor, episode, ct);
            var waiting = await repository.WaitingAsync(scope, episode.Id, ct) ?? throw BusinessException.Conflict("该周期没有等待记录");
            foreach (var task in await DeliveriesAsync(waiting, ct))
            {
                if (task.State != DeliveryState.DEAD)
                {
                    continue;
                }
                DeliveryRules.Retry(task, actor, clock.GetUtcNow().UtcDateTime);
            }
            return await ViewAsync(waiting, ct);
        }, ct);

    /// <summary>同事务初始化并推进等待事实，闭班优先于过期提醒，已接单优先于营业时间。</summary>
    public async Task<Waiting> RefreshInTransactionAsync(Episode episode, string trace, CancellationToken ct)
    {
        var waiting = await repository.WaitingAsync(episode.Scope, episode.Id, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        if (waiting is null)
        {
            var calendar = await configuration.CalendarAsync(episode.TenantId, episode.TopOrgId, ct);
            var window = calendar.Evaluate(episode.RequestedAt);
            waiting = ConversationRuntime.Bind<Waiting>(episode.Scope, episode.PrincipalId, episode.Id);
            waiting.EpisodeId = episode.Id;
            waiting.State = window.Open ? WaitingState.WAITING : WaitingState.NEED_EMAIL;
            waiting.RemindAt = episode.RequestedAt.AddMinutes(5);
            waiting.BusyAt = episode.RequestedAt.AddMinutes(10);
            waiting.ClosesAt = window.ClosesAt;
            waiting.NextOpensAt = window.NextOpensAt;
            waiting.CalendarVersion = window.Version;
            repository.AddWaiting(waiting);
        }
        if (waiting.State is WaitingState.FOLLOW_UP or WaitingState.ACCEPTED or WaitingState.CANCELLED)
        {
            return waiting;
        }
        if (episode.Status is EpisodeStatus.CLAIMED or EpisodeStatus.ACTIVE)
        {
            if (waiting.State == WaitingState.NEED_EMAIL)
            {
                waiting.State = WaitingState.ACCEPTED;
                waiting.Version++;
            }
            else
            {
                ReceptionRules.ChangeWaiting(waiting, WaitingState.ACCEPTED);
            }
            return waiting;
        }
        if (episode.Status is EpisodeStatus.CANCELLED or EpisodeStatus.RESOLVED)
        {
            if (waiting.State != WaitingState.NEED_EMAIL)
            {
                ReceptionRules.ChangeWaiting(waiting, WaitingState.CANCELLED);
            }
            return waiting;
        }
        if (waiting.State == WaitingState.NEED_EMAIL || now >= waiting.ClosesAt)
        {
            if (waiting.State != WaitingState.NEED_EMAIL)
            {
                ReceptionRules.ChangeWaiting(waiting, WaitingState.NEED_EMAIL);
            }
            await CancelInTransactionAsync(episode, ct);
            return waiting;
        }
        if (!waiting.Reminded && now >= waiting.RemindAt)
        {
            await journal.EnqueueAsync(episode.Scope, episode.PrincipalId, episode.Id, "INTERNAL", "remind:" + episode.Id,
                new { type = "waiting.reminder", conversationId = episode.ConversationId, episodeId = episode.Id }, trace, ct);
            waiting.Reminded = true;
            waiting.Version++;
        }
        if (waiting.State == WaitingState.WAITING && now >= waiting.BusyAt)
        {
            ReceptionRules.ChangeWaiting(waiting, WaitingState.BUSY);
        }
        return waiting;
    }

    /// <summary>明确选择邮件后原子取消在线等待、恢复 AI、创建唯一工单和通知任务。</summary>
    public async Task EmailInTransactionAsync(Episode episode, Waiting waiting, string? email, string trace, CancellationToken ct, bool createWorkItem = true)
    {
        var address = Guard.Email(email);
        if (episode.Status is EpisodeStatus.CLAIMED or EpisodeStatus.ACTIVE or EpisodeStatus.RESOLVED)
        {
            throw BusinessException.Conflict("已接单或结束的周期不能改为邮件跟进");
        }
        if (waiting.State == WaitingState.FOLLOW_UP)
        {
            if (waiting.Email != address)
            {
                throw BusinessException.Conflict("已建工单不能替换回复邮箱");
            }
            return;
        }
        waiting.Email = address;
        waiting.TicketId = Guard.Id("ticket");
        ReceptionRules.ChangeWaiting(waiting, WaitingState.FOLLOW_UP);
        if (episode.Status == EpisodeStatus.REQUESTED)
        {
            await CancelInTransactionAsync(episode, ct);
        }
        if (createWorkItem)
        {
            var work = ConversationRuntime.Bind<Followup>(episode.Scope, episode.PrincipalId, Guard.Id("followup"));
            work.Origin = "EMAIL";
            work.EpisodeId = episode.Id;
            work.TicketId = waiting.TicketId;
            work.GroupId = episode.GroupId;
            work.RequestId = "email:" + waiting.TicketId;
            work.Fingerprint = Guard.Fingerprint(work.RequestId + "|" + address);
            work.SubmittedAt = clock.GetUtcNow().UtcDateTime;
            followups.Add(work);
        }
        await journal.EnqueueAsync(episode.Scope, episode.PrincipalId, episode.Id, "EMAIL", "followup:" + waiting.TicketId,
            new { type = "followup.created", ticketId = waiting.TicketId, conversationId = episode.ConversationId }, trace, ct);
    }

    /// <summary>取消与恢复回复权沿用同一状态规则及事务，绝不覆盖已先认领的周期。</summary>
    private async Task CancelInTransactionAsync(Episode episode, CancellationToken ct)
    {
        episode.Status = ReceptionRules.Transition(episode.Status, "CANCEL");
        episode.Version++;
        episode.FinishedAt = clock.GetUtcNow().UtcDateTime;
        await runtime.ResumeAiAsync(episode.Scope, episode.Id, ct);
    }

    /// <summary>访客等待投影不泄露邮箱及内部通知接收者。</summary>
    private async Task<object> ViewAsync(Waiting waiting, CancellationToken ct) => new
    {
        waiting.State,
        waiting.Version,
        waiting.ClosesAt,
        waiting.NextOpensAt,
        waiting.TicketId,
        contactCollected = waiting.Email is not null,
        deliveries = (await DeliveriesAsync(waiting, ct)).Select(t => new { kind = t.Kind, status = t.State, t.Attempts, t.Version })
    };

    /// <summary>使用固定业务键读取本周期提醒与建单通知，不暴露任务载荷、邮箱或内部接收者。</summary>
    private async Task<List<ReliableTask>> DeliveriesAsync(Waiting waiting, CancellationToken ct)
    {
        var result = new List<ReliableTask>();
        foreach (var key in new[] { "remind:" + waiting.EpisodeId, "followup:" + waiting.TicketId })
        {
            var value = await tasks.FindAsync(waiting.TenantId, waiting.TopOrgId, key, ct);
            if (value is not null && value.ConversationId == waiting.ConversationId && value.EpisodeId == waiting.EpisodeId && value.Kind is "EMAIL" or "INTERNAL")
            {
                result.Add(value);
            }
        }
        return result;
    }
}
