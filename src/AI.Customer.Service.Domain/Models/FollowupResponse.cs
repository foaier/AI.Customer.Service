namespace AI.Customer.Service.Domain.Models;

/// <summary>客服最终邮件回复，内容与可靠任务原子保存。</summary>
public sealed class FollowupResponse : ConversationAggregate
{
    /// <summary>固定原周期，不能被新申请替换。</summary>
    public string EpisodeId { get; set; } = "";

    /// <summary>唯一原工单引用。</summary>
    public string TicketId { get; set; } = "";

    /// <summary>首次回复的认领客服。</summary>
    public string ActorId { get; set; } = "";

    /// <summary>最多 200 字符，禁止换行。</summary>
    public string Subject { get; set; } = "";

    /// <summary>最多 10000 字符的最终纯文本回复。</summary>
    public string Content { get; set; } = "";

    /// <summary>受理、失败及显式结案状态。</summary>
    public DeliveryState State { get; set; } = DeliveryState.PENDING;

    /// <summary>首次回复 UTC 时间。</summary>
    public DateTime CreatedAt { get; set; }
}
