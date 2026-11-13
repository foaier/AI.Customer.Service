using AI.Customer.Service.Domain.Models;

namespace AI.Customer.Service.Domain.Ports;

/// <summary>客服 owner 的指标端口，不允许跨上下文访问会话或调查持久结构。</summary>
public interface IReceptionMetricsQuery
{
    /// <summary>在完整租户组织助手范围内聚合最近历史和全部当前积压。</summary>
    Task<ReceptionMetrics> ReadAsync(string tenant, string org, string assistant, DateTime from, DateTime through, CancellationToken ct);
}

/// <summary>Conversation owner 的满意度只读指标端口。</summary>
public interface ISurveyMetricsQuery
{
    /// <summary>按调查打开时间及完整范围统计状态，不把未回答计为满意。</summary>
    Task<IReadOnlyDictionary<string, long>> CountAsync(string tenant, string org, string assistant, DateTime from, DateTime through, CancellationToken ct);
}
