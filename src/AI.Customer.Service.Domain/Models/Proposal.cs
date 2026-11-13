namespace AI.Customer.Service.Domain.Models;

/// <summary>受限 AI 工具建议卡，访客确认后才创建人工周期。</summary>
public sealed class Proposal : ConversationAggregate
{
    /// <summary>服务端绑定的 AI 轮次。</summary>
    public string TurnId { get; set; } = "";

    /// <summary>建议时的回复代次，确认时必须匹配。</summary>
    public long Epoch { get; set; }

    /// <summary>单次决策状态。</summary>
    public ProposalStatus Status { get; set; } = ProposalStatus.PENDING;

    /// <summary>白名单内的模型建议原因。</summary>
    public ReasonCode ReasonCode { get; set; }

    /// <summary>有界可展示说明，不允许可执行 URL。</summary>
    public string UserFacingReason { get; set; } = "";

    /// <summary>模型置信度，范围 0 至 1。</summary>
    public double Confidence { get; set; }

    /// <summary>服务端 UTC 失效时间。</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>建议创建 UTC 时间。</summary>
    public DateTime CreatedAt { get; set; }
}
