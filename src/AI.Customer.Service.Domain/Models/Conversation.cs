namespace AI.Customer.Service.Domain.Models;

/// <summary>Conversation owner 的回复代次和当前轮次，人工与 AI 共用同一会话。</summary>
public sealed class Conversation : ConversationAggregate
{
    /// <summary>最近完整成功轮次，调查恢复 Worker 仅处理尚未评估的新成功轮次。</summary>
    public string? LastCompletedTurnId { get; set; }

    /// <summary>最后已完成资格评估的成功轮次，不根据用户沉默产生新调查。</summary>
    public string? SurveyCheckedTurnId { get; set; }

    /// <summary>回复代次，每次人工接管或恢复均递增。</summary>
    public long Epoch { get; set; } = 1;

    /// <summary>当前独占回复权的人工周期，空表示允许 AI。</summary>
    public string? HumanEpisodeId { get; set; }

    /// <summary>正在生成的 AI 轮次，拒绝并发轮次及迟到响应。</summary>
    public string? CurrentTurnId { get; set; }

    /// <summary>AI 生成租约 UTC 截止，崩溃后允许新轮次恢复。</summary>
    public DateTime? TurnLeaseUntil { get; set; }

    /// <summary>整个会话共享的人工消息最大序列。</summary>
    public long NextSeq { get; set; } = 0;

    /// <summary>首次创建 UTC 时间。</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>最后活动 UTC 时间，不作为调查结束证据。</summary>
    public DateTime UpdatedAt { get; set; }
}
