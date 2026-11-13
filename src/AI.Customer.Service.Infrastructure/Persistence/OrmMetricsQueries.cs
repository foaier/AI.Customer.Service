using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;
using Microsoft.EntityFrameworkCore;

namespace AI.Customer.Service.Infrastructure.Persistence;

/// <summary>只使用客服 owner 表的 ORM 聚合，历史窗口与当前积压分别查询。</summary>
/// <param name="db">共享一致性事务上下文。</param>
public sealed class ReceptionMetricsQuery(CustomerServiceDbContext db) : IReceptionMetricsQuery
{
    /// <inheritdoc />
    public async Task<ReceptionMetrics> ReadAsync(string tenant, string org, string assistant, DateTime from, DateTime through, CancellationToken ct)
    {
        var episodes = db.Set<Episode>().AsNoTracking().Where(x => x.TenantId == tenant && x.TopOrgId == org && x.AssistantConfigId == assistant);
        var history = episodes.Where(x => x.RequestedAt >= from && x.RequestedAt < through);
        var requests = await history.GroupBy(x => new { x.TriggerSource, x.ReasonCode })
            .Select(g => new { g.Key.TriggerSource, g.Key.ReasonCode, Count = g.LongCount() }).ToListAsync(ct);
        var states = await history.GroupBy(x => x.Status).Select(g => new { State = g.Key, Count = g.LongCount() }).ToListAsync(ct);
        var timings = new Dictionary<string, TimingMetric> { ["claim"] = new(0, 0), ["first_response"] = new(0, 0), ["resolve"] = new(0, 0) };
        // 仅流式读取时间投影，在 .NET 计算差值，避免按数据库方言手写日期差函数或加载正文。
        await foreach (var row in history.Select(x => new { x.RequestedAt, x.ClaimedAt, x.FirstResponseAt, x.FinishedAt, x.Status }).AsAsyncEnumerable().WithCancellation(ct))
        {
            Accumulate(timings, "claim", row.RequestedAt, row.ClaimedAt);
            Accumulate(timings, "first_response", row.RequestedAt, row.FirstResponseAt);
            if (row.Status == EpisodeStatus.RESOLVED)
            {
                Accumulate(timings, "resolve", row.RequestedAt, row.FinishedAt);
            }
        }
        var tasks = db.Set<ReliableTask>().AsNoTracking().Where(x => x.TenantId == tenant && x.TopOrgId == org && x.AssistantConfigId == assistant);
        var old = through.AddMinutes(-5);
        var backlog = new Dictionary<string, long>
        {
            ["waiting_over_5m"] = await episodes.LongCountAsync(x => x.Status == EpisodeStatus.REQUESTED && x.RequestedAt <= old, ct),
            ["notification_dead"] = await tasks.LongCountAsync(x => (x.Kind == "EMAIL" || x.Kind == "INTERNAL") && x.State == DeliveryState.DEAD, ct),
            ["reply_dead"] = await tasks.LongCountAsync(x => x.Kind == "EMAIL_REPLY" && x.State == DeliveryState.DEAD, ct),
            ["system_dead"] = await tasks.LongCountAsync(x => x.Kind == "SYSTEM_HANDOFF" && x.State == DeliveryState.DEAD, ct),
            ["outbox_dead"] = await tasks.LongCountAsync(x => x.Kind == "EVENT" && x.State == DeliveryState.DEAD, ct),
            ["system_over_5m"] = await tasks.LongCountAsync(x => x.Kind == "SYSTEM_HANDOFF" && (x.State == DeliveryState.PENDING || x.State == DeliveryState.CLAIMED) && x.CreatedAt <= old, ct),
            ["outbox_over_5m"] = await tasks.LongCountAsync(x => x.Kind == "EVENT" && (x.State == DeliveryState.PENDING || x.State == DeliveryState.CLAIMED) && x.CreatedAt <= old, ct)
        };
        return new(requests.ToDictionary(x => x.TriggerSource + ":" + x.ReasonCode, x => x.Count),
            states.ToDictionary(x => x.State.ToString(), x => x.Count), timings, backlog);
    }

    /// <summary>缺失或逆序时间不计样本，不用零耗时填补缺失首响。</summary>
    private static void Accumulate(IDictionary<string, TimingMetric> output, string stage, DateTime requested, DateTime? occurred)
    {
        if (occurred is null || occurred < requested)
        {
            return;
        }
        var previous = output[stage];
        output[stage] = new(previous.Count + 1, previous.SecondsSum + (occurred.Value - requested).TotalSeconds);
    }
}

/// <summary>Conversation owner 的 ORM 满意度聚合，不返回调查原文或访客引用。</summary>
/// <param name="db">共享一致性事务上下文。</param>
public sealed class SurveyMetricsQuery(CustomerServiceDbContext db) : ISurveyMetricsQuery
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, long>> CountAsync(string tenant, string org, string assistant, DateTime from, DateTime through, CancellationToken ct)
    {
        var rows = await db.Set<Survey>().AsNoTracking().Where(x => x.TenantId == tenant && x.TopOrgId == org && x.AssistantConfigId == assistant &&
            x.OpenedAt >= from && x.OpenedAt < through).GroupBy(x => x.Status == SurveyStatus.OPEN && x.ExpiresAt <= through ? SurveyStatus.EXPIRED : x.Status)
            .Select(g => new { State = g.Key, Count = g.LongCount() }).ToListAsync(ct);
        var result = Enum.GetValues<SurveyStatus>().ToDictionary(x => x.ToString(), _ => 0L);
        foreach (var row in rows)
        {
            result[row.State.ToString()] = row.Count;
        }
        return result;
    }
}
