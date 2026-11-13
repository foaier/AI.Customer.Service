namespace AI.Customer.Service.Domain.Models;

/// <summary>Conversation owner 的单次满意度事实，人工结束不会创建调查。</summary>
public sealed class Survey : ConversationAggregate
{
    /// <summary>通过真实结束证据门禁并创建调查的 UTC 时间，用于固定窗口对账。</summary>
    public DateTime OpenedAt { get; set; }

    /// <summary>创建时的 AI 回复代次，新用户轮次或人工接管后禁止首次评价和人工选择。</summary>
    public long Epoch { get; set; }

    /// <summary>单次评价状态。</summary>
    public SurveyStatus Status { get; set; } = SurveyStatus.OPEN;

    /// <summary>不满意后独立的人工选择。</summary>
    public HandoffChoice HandoffChoice { get; set; } = HandoffChoice.NOT_APPLICABLE;

    /// <summary>用户结束证据所在成功轮次。</summary>
    public string SourceTurnId { get; set; } = "";

    /// <summary>结束证据类型和策略版本，不复制正文。</summary>
    public string Evidence { get; set; } = "";

    /// <summary>调查 UTC 失效时间。</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>首次明确回答 UTC 时间。</summary>
    public DateTime? RespondedAt { get; set; }

    /// <summary>实际展示回执时间，查询不能代填。</summary>
    public DateTime? DisplayedAt { get; set; }
}
