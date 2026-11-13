namespace AI.Customer.Service.Domain.Models;

/// <summary>客服 owner 的一次人工申请及冻结路由，不复制账号主数据。</summary>
public sealed class Episode : ConversationAggregate
{
    /// <summary>接待状态，禁止客户端直接指定。</summary>
    public EpisodeStatus Status { get; set; } = EpisodeStatus.REQUESTED;

    /// <summary>首次确定性来源，改派不得覆盖。</summary>
    public TriggerSource TriggerSource { get; set; }

    /// <summary>首次业务原因，与触发来源分别保存。</summary>
    public ReasonCode ReasonCode { get; set; }

    /// <summary>申请时冻结的本地业务组。</summary>
    public string GroupId { get; set; } = "";

    /// <summary>申请时冻结的咨询主题。</summary>
    public Category Category { get; set; }

    /// <summary>申请时的路由配置版本。</summary>
    public long ConfigVersion { get; set; }

    /// <summary>MODEL 或明确的降级原因。</summary>
    public string ClassificationSource { get; set; } = "";

    /// <summary>真实认领客服账号。</summary>
    public string? AgentId { get; set; }

    /// <summary>首次申请 UTC 时间。</summary>
    public DateTime RequestedAt { get; set; }

    /// <summary>首次认领 UTC 时间。</summary>
    public DateTime? ClaimedAt { get; set; }

    /// <summary>客服首条有效消息 UTC 时间。</summary>
    public DateTime? FirstResponseAt { get; set; }

    /// <summary>取消或结束 UTC 时间。</summary>
    public DateTime? FinishedAt { get; set; }

    /// <summary>首次申请跨任务追踪标识。</summary>
    public string TraceId { get; set; } = "";
}
