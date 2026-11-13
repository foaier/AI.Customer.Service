namespace AI.Customer.Service.Domain.Models;

/// <summary>原周期的留言表单及认领事实，回复另有生命周期。</summary>
public sealed class Followup : ConversationAggregate
{
    /// <summary>FORM 表示访客主动留言，EMAIL 表示仅选择邮箱跟进，不能伪造访客正文。</summary>
    public string Origin { get; set; } = "FORM";

    /// <summary>固定原人工周期。</summary>
    public string EpisodeId { get; set; } = "";

    /// <summary>等待 owner 的工单号。</summary>
    public string TicketId { get; set; } = "";

    /// <summary>原申请冻结的接待组。</summary>
    public string GroupId { get; set; } = "";

    /// <summary>首次留言请求键。</summary>
    public string RequestId { get; set; } = "";

    /// <summary>原留言及邮箱摘要，拒绝异内容重放。</summary>
    public string Fingerprint { get; set; } = "";

    /// <summary>可空称呼，最多 100 字符。</summary>
    public string? ContactName { get; set; }

    /// <summary>主动留言为 1 至 4000 字符；仅邮箱跟进时为空字符串。</summary>
    public string Content { get; set; } = "";

    /// <summary>OPEN 待认领或 CLAIMED 已认领。</summary>
    public string State { get; set; } = "OPEN";

    /// <summary>已认领客服，未认领为空。</summary>
    public string? AssignedTo { get; set; }

    /// <summary>首次提交 UTC 时间。</summary>
    public DateTime SubmittedAt { get; set; }

    /// <summary>首次认领 UTC 时间。</summary>
    public DateTime? ClaimedAt { get; set; }
}
