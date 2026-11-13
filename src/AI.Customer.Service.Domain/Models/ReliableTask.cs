namespace AI.Customer.Service.Domain.Models;

/// <summary>带租约退避死信的副作用，核心事务只登记任务。</summary>
public sealed class ReliableTask : ConversationAggregate
{
    /// <summary>INTERNAL、EMAIL、EMAIL_REPLY、SYSTEM_HANDOFF 或 EVENT。</summary>
    public string Kind { get; set; } = "";

    /// <summary>Worker 与网关共用的稳定幂等键。</summary>
    public string EventKey { get; set; } = "";

    /// <summary>原周期；系统任务登记时可为空。</summary>
    public string EpisodeId { get; set; } = "";

    /// <summary>受控 JSON 业务载荷，不含认证密钥。</summary>
    public string Payload { get; set; } = "";

    /// <summary>可靠任务集中状态。</summary>
    public DeliveryState State { get; set; } = DeliveryState.PENDING;

    /// <summary>当前人工恢复周期领取次数，上限五次。</summary>
    public int Attempts { get; set; }

    /// <summary>包括历次人工恢复周期在内的累计领取次数，仅递增。</summary>
    public long TotalAttempts { get; set; }

    /// <summary>正式客服人工恢复预算的累计次数，仅递增。</summary>
    public long ManualRetryCount { get; set; }

    /// <summary>最近一次人工恢复的账号引用，自动重试不覆盖。</summary>
    public string? LastRetriedBy { get; set; }

    /// <summary>最近一次人工恢复的 UTC 时间，初次任务为空。</summary>
    public DateTime? LastRetriedAt { get; set; }

    /// <summary>下次可领取 UTC 时间。</summary>
    public DateTime DueAt { get; set; }

    /// <summary>本次随机租约，完成时需匹配。</summary>
    public string? LeaseId { get; set; }

    /// <summary>租约 UTC 截止，崩溃后可恢复。</summary>
    public DateTime? LeaseUntil { get; set; }

    /// <summary>不含正文邮箱的稳定失败分类。</summary>
    public string? LastError { get; set; }

    /// <summary>原始追踪标识，跨 Worker 保持。</summary>
    public string TraceId { get; set; } = "";

    /// <summary>任务登记 UTC 时间。</summary>
    public DateTime CreatedAt { get; set; }
}
