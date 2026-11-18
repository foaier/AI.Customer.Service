namespace AI.Customer.Service.Domain.Models;

/// <summary>按客服账号隔离的离线留言首次查看事实。</summary>
public sealed class FollowupReceipt : ConversationAggregate
{
    /// <summary>固定原接待周期。</summary>
    public string EpisodeId { get; set; } = "";

    /// <summary>正式认证客服账号。</summary>
    public string SeenBy { get; set; } = "";

    /// <summary>首次确认查看的 UTC 时间。</summary>
    public DateTime SeenAt { get; set; }
}
