namespace AI.Customer.Service.Domain.Models;

/// <summary>单一会话主体的接收及已读水位，不能代替对方确认。</summary>
public sealed class MessageReceipt : ConversationAggregate
{
    /// <summary>回执所属认证账号。</summary>
    public string ActorId { get; set; } = "";

    /// <summary>只增不减的接收序列。</summary>
    public long DeliveredSeq { get; set; }

    /// <summary>不超过接收水位的已读序列。</summary>
    public long ReadSeq { get; set; }
}
