namespace AI.Customer.Service.Domain.Models;

/// <summary>独立的等待和联系方式聚合，期限在初始化时冻结且只按需推进。</summary>
public sealed class Waiting : ConversationAggregate
{
    /// <summary>唯一对应的原人工周期。</summary>
    public string EpisodeId { get; set; } = "";

    /// <summary>独立等待工作流状态。</summary>
    public WaitingState State { get; set; }

    /// <summary>T+5 内部提醒 UTC 期限。</summary>
    public DateTime RemindAt { get; set; }

    /// <summary>T+10 用户提示 UTC 期限。</summary>
    public DateTime BusyAt { get; set; }

    /// <summary>冻结的营业结束 UTC 时间。</summary>
    public DateTime ClosesAt { get; set; }

    /// <summary>冻结的下次营业 UTC 时间。</summary>
    public DateTime NextOpensAt { get; set; }

    /// <summary>期限的业务日历版本。</summary>
    public string CalendarVersion { get; set; } = "";

    /// <summary>是否登记提醒，不表示实际送达。</summary>
    public bool Reminded { get; set; } = false;

    /// <summary>访客明确提交的邮箱，仅客服 owner 保存。</summary>
    public string? Email { get; set; }

    /// <summary>唯一跟进工单号，不代表邮件已发送。</summary>
    public string? TicketId { get; set; }
}
