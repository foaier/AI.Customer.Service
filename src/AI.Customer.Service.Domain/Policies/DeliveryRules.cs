using AI.Customer.Service.Common;
using AI.Customer.Service.Domain.Models;

namespace AI.Customer.Service.Domain.Policies;

/// <summary>可靠任务的人工恢复规则，保留首次来源、原载荷、累计尝试和人工审计。</summary>
public static class DeliveryRules
{
    /// <summary>仅死信允许正式客服恢复预算，不改变稳定投递键或原始 trace。</summary>
    public static void Retry(ReliableTask task, Actor actor, DateTime now)
    {
        if (task.State != DeliveryState.DEAD || task.TenantId != actor.TenantId)
        {
            throw BusinessException.Conflict("只有当前租户死信可以恢复");
        }
        task.State = DeliveryState.PENDING;
        task.Attempts = 0;
        task.DueAt = now;
        task.ManualRetryCount++;
        task.LastRetriedAt = now;
        task.LastRetriedBy = actor.AccountId;
        task.Version++;
    }
}
