namespace AI.Customer.Service.Domain.Models;

/// <summary>真实 AI 轮次及成功证据，失败回答不得进入调查资格。</summary>
public sealed class AiTurn : ConversationAggregate
{
    /// <summary>开始时回复代次，完成时必须匹配。</summary>
    public long Epoch { get; set; }

    /// <summary>本轮访客原始纯文本。</summary>
    public string UserContent { get; set; } = "";

    /// <summary>模型完整成功的展示文本。</summary>
    public string? AssistantContent { get; set; }

    /// <summary>RUNNING、SUCCEEDED、FAILED 或 FENCED。</summary>
    public string Status { get; set; } = "RUNNING";

    /// <summary>生成开始 UTC 时间。</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>完成或失败 UTC 时间。</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>传播至模型和故障任务的关联标识。</summary>
    public string TraceId { get; set; } = "";
}
