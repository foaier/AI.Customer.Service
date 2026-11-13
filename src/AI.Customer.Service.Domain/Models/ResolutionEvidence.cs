namespace AI.Customer.Service.Domain.Models;

/// <summary>Conversation owner 的模型结束候选或确定性用户结束信号，只保存引用及摘要，不复制正文。</summary>
public sealed class ResolutionEvidence : ConversationAggregate
{
    /// <summary>MODEL_POSSIBLE_RESOLUTION、USER_EXPLICIT_END 或 USER_CONFIRMED_NO_FURTHER_QUESTION。</summary>
    public string Kind { get; set; } = "";
    /// <summary>证据来自的实际成功轮次。</summary>
    public string TurnId { get; set; } = "";
    /// <summary>当前 AI 回复代次，后续用户输入和人工接管后不能恢复为当前信号。</summary>
    public long Epoch { get; set; }
    /// <summary>模型候选置信度，用户确定性信号为空。</summary>
    public double? Confidence { get; set; }
    /// <summary>版本化策略来源，模型候选不能代替用户明确结束。</summary>
    public string Policy { get; set; } = "";
    /// <summary>绑定真实引用、代次和参数的摘要，异参数不能覆盖首条证据。</summary>
    public string Fingerprint { get; set; } = "";
    /// <summary>首次记录 UTC 时间，同参数重放不改写。</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>来源轮次的追踪标识，跨恢复任务保持。</summary>
    public string TraceId { get; set; } = "";
}
