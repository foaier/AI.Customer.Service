namespace AI.Customer.Service.Domain.Models;

/// <summary>Conversation owner 的人工消息，所有周期共享递增序列。</summary>
public sealed class HumanMessage : ConversationAggregate
{
    /// <summary>发送时的有效周期。</summary>
    public string EpisodeId { get; set; } = "";

    /// <summary>会话内唯一递增序列。</summary>
    public long Seq { get; set; }

    /// <summary>只允许 VISITOR 或 AGENT。</summary>
    public string SenderRole { get; set; } = "";

    /// <summary>认证和认领关系确定的发送账号。</summary>
    public string SenderId { get; set; } = "";

    /// <summary>发送主体下稳定的消息幂等键。</summary>
    public string ClientMsgId { get; set; } = "";

    /// <summary>1 至 4000 字符纯文本。</summary>
    public string Content { get; set; } = "";

    /// <summary>首次保存 UTC 时间。</summary>
    public DateTime CreatedAt { get; set; }
}
