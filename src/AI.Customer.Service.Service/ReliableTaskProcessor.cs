using System.Text.Json;
using AI.Customer.Service.Common;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Policies;
using AI.Customer.Service.Domain.Ports;
using Microsoft.Extensions.Logging;

namespace AI.Customer.Service.Service;

/// <summary>可靠任务领取、事务外投递及租约确认，崩溃重启后保持至少一次恢复。</summary>
/// <param name="tasks">任务 owner。</param>
/// <param name="followups">最终回复 owner。</param>
/// <param name="transactions">短事务边界。</param>
/// <param name="notification">出站协议端口。</param>
/// <param name="handoff">系统转人工应用端口。</param>
/// <param name="conversations">系统故障来源轮次核验。</param>
/// <param name="clock">UTC 时钟。</param>
/// <param name="logger">不记录载荷的结构化日志。</param>
public sealed class ReliableTaskProcessor(ITaskRepository tasks, IFollowupRepository followups, IUnitOfWork transactions,
    INotificationPort notification, HandoffService handoff, IConversationRepository conversations, TimeProvider clock, ILogger<ReliableTaskProcessor> logger)
{
    /// <summary>每轮仅领取支持通道及内部系统任务，有限批次避免阻塞关闭流程。</summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        var kinds = notification.SupportedKinds.Append("SYSTEM_HANDOFF").ToArray();
        var due = await tasks.DueAsync(kinds, clock.GetUtcNow().UtcDateTime, 100, ct);
        foreach (var candidate in due)
        {
            var lease = await ClaimAsync(candidate, ct);
            if (lease is null)
            {
                continue;
            }
            string? error = null;
            try
            {
                if (lease.Kind == "SYSTEM_HANDOFF")
                {
                    await ApplySystemAsync(lease, ct);
                }
                else
                {
                    await notification.SendAsync(lease, ct);
                }
            }
            catch (Exception failure) when (!ct.IsCancellationRequested)
            {
                error = failure is BusinessException business ? business.Code : failure.GetType().Name;
                logger.LogWarning("任务待恢复 EventKey={EventKey} SessionId={SessionId} TraceId={TraceId} TopOrgId={TopOrgId} BventId={BventId} Attempt={Attempt} Failure={Failure}",
                    lease.EventKey, lease.ConversationId, lease.TraceId, lease.TopOrgId, lease.BventId, lease.Attempts, error);
            }
            await CompleteAsync(lease, error, ct);
        }
    }

    /// <summary>只允许最终失败任务人工恢复，内容、原始来源和稳定投递键保持。</summary>
    public Task<object> RetrySystemAsync(Actor actor, Scope scope, string eventKey, CancellationToken ct) =>
        transactions.ExecuteAsync<object>(scope.TenantId, scope.TopOrgId, async () =>
        {
            var value = await tasks.FindAsync(scope.TenantId, scope.TopOrgId, eventKey, ct) ?? throw BusinessException.Forbidden();
            if (value.ConversationId != scope.ConversationId || value.Kind != "SYSTEM_HANDOFF" || value.State != DeliveryState.DEAD)
            {
                throw BusinessException.Conflict("只有本会话最终失败的系统任务可重试");
            }
            DeliveryRules.Retry(value, actor, clock.GetUtcNow().UtcDateTime);
            return new { value.EventKey, value.State, value.Version };
        }, ct);

    /// <summary>持久租约原子领取，失败达到五次后进入死信，过期租约也计入次数。</summary>
    private Task<ReliableTask?> ClaimAsync(ReliableTask candidate, CancellationToken ct) =>
        transactions.ExecuteAsync<ReliableTask?>(candidate.TenantId, candidate.TopOrgId, async () =>
        {
            var value = await tasks.FindAsync(candidate.TenantId, candidate.TopOrgId, candidate.EventKey, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            if (value is null || !(value.State == DeliveryState.PENDING && value.DueAt <= now || value.State == DeliveryState.CLAIMED && value.LeaseUntil <= now))
            {
                return null;
            }
            if (value.Attempts >= 5)
            {
                value.State = DeliveryState.DEAD;
                value.LeaseId = null;
                value.LeaseUntil = null;
                value.Version++;
                await SyncResponseAsync(value, ct);
                return null;
            }
            value.State = DeliveryState.CLAIMED;
            value.Attempts++;
            value.TotalAttempts++;
            value.LeaseId = Guard.Id("lease");
            value.LeaseUntil = now.AddSeconds(30);
            value.Version++;
            await SyncResponseAsync(value, ct);
            return value;
        }, ct);

    /// <summary>仅匹配当前租约才能确认，失败按 5、10、20、40 秒退避并最多尝试五次。</summary>
    private Task<bool> CompleteAsync(ReliableTask lease, string? error, CancellationToken ct) =>
        transactions.ExecuteAsync(lease.TenantId, lease.TopOrgId, async () =>
        {
            var value = await tasks.FindAsync(lease.TenantId, lease.TopOrgId, lease.EventKey, ct);
            if (value?.State != DeliveryState.CLAIMED || value.LeaseId != lease.LeaseId)
            {
                return false;
            }
            value.State = error is null ? DeliveryState.SENT : value.Attempts >= 5 ? DeliveryState.DEAD : DeliveryState.PENDING;
            value.LastError = error;
            value.LeaseId = null;
            value.LeaseUntil = null;
            value.DueAt = clock.GetUtcNow().UtcDateTime.AddSeconds(Math.Min(60, 5 * Math.Pow(2, value.Attempts - 1)));
            value.Version++;
            await SyncResponseAsync(value, ct);
            return true;
        }, ct);

    /// <summary>最终回复投递状态与任务同事务推进，不以日志代替受理事实。</summary>
    private async Task SyncResponseAsync(ReliableTask task, CancellationToken ct)
    {
        if (task.Kind != "EMAIL_REPLY")
        {
            return;
        }
        var response = await followups.ResponseAsync(task.Scope, task.EpisodeId, ct);
        if (response is not null && response.State != task.State)
        {
            response.State = task.State;
            response.Version++;
        }
    }

    /// <summary>核验真实失败轮次后使用稳定系统命令键，迟到旧任务不抢占后续轮次。</summary>
    private async Task ApplySystemAsync(ReliableTask task, CancellationToken ct)
    {
        using var payload = JsonDocument.Parse(task.Payload);
        var turnId = payload.RootElement.GetProperty("turnId").GetString()!;
        var turn = await conversations.TurnAsync(task.Scope, turnId, ct);
        var current = await conversations.FindAsync(task.TenantId, task.ConversationId, ct);
        if (turn?.Status != "FAILED" || current is null || current.Epoch != turn.Epoch ||
            current.HumanEpisodeId is not null || current.CurrentTurnId is not null)
        {
            return;
        }
        await handoff.RequestAsync(new(task.TenantId, task.PrincipalId, task.TraceId), task.Scope, task.EventKey,
            TriggerSource.SYSTEM_POLICY, ReasonCode.SYSTEM_FAILURE, ct, failedTurnId: turnId);
    }
}
