namespace AI.Customer.Service.Domain.Models;

/// <summary>组织级客服在线代次和租约，不授予成员资格。</summary>
public sealed class Availability : Aggregate
{
    /// <summary>明确选择的展示状态。</summary>
    public AvailabilityStatus Status { get; set; } = AvailabilityStatus.OFFLINE;

    /// <summary>ONLINE 心跳 UTC 截止，旧代次不能续约。</summary>
    public DateTime? ExpiresAt { get; set; }
}
