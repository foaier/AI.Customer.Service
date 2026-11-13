namespace AI.Customer.Service.Domain.Models;

/// <summary>不可变命令回执，同键重试仍需复核权限。</summary>
public sealed class CommandReceipt : ConversationAggregate
{
    /// <summary>首次成功命令的 UTC 审计时间，重放不更新。</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>首次成功命令的服务端追踪标识。</summary>
    public string TraceId { get; set; } = "";

    /// <summary>首次命令主体。</summary>
    public string ActorId { get; set; } = "";

    /// <summary>明确的业务动作。</summary>
    public string Action { get; set; } = "";

    /// <summary>首次请求幂等键。</summary>
    public string RequestId { get; set; } = "";

    /// <summary>动作和原输入 SHA256 摘要。</summary>
    public string Fingerprint { get; set; } = "";

    /// <summary>首次成功的安全响应投影。</summary>
    public string ResultJson { get; set; } = "";
}
