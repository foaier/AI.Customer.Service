namespace AI.Customer.Service.Domain.Models;

/// <summary>客服 owner 的只读聚合投影，不携带会话、账号、正文或邮箱。</summary>
/// <param name="Requested">24 小时内按来源和原因聚合的申请数。</param>
/// <param name="Statuses">同历史窗口内的接待当前状态数。</param>
/// <param name="Timings">接单、首次响应和解决的有效样本与秒数总和。</param>
/// <param name="Backlog">不受历史窗口截断的当前积压和死信。</param>
public sealed record ReceptionMetrics(IReadOnlyDictionary<string, long> Requested, IReadOnlyDictionary<string, long> Statuses,
    IReadOnlyDictionary<string, TimingMetric> Timings, IReadOnlyDictionary<string, long> Backlog);

/// <summary>固定窗口内的耗时统计，没有事实的样本不按零秒补齐。</summary>
/// <param name="Count">有持久时间事实的样本数。</param>
/// <param name="SecondsSum">从申请开始的非负耗时总秒数。</param>
public sealed record TimingMetric(long Count, double SecondsSum);

/// <summary>跨 owner 的一致性只读快照，全部值为窗口或当前存量 gauge。</summary>
/// <param name="From">最近 24 小时窗口的 UTC 起点，包含。</param>
/// <param name="Through">快照 UTC 终点，不包含。</param>
/// <param name="Reception">客服 owner 的聚合。</param>
/// <param name="Surveys">Conversation owner 按打开时间统计的调查当前状态。</param>
public sealed record MetricsSnapshot(DateTime From, DateTime Through, ReceptionMetrics Reception, IReadOnlyDictionary<string, long> Surveys);
