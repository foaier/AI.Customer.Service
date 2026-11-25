namespace AI.Customer.Service.Domain.Models;

/// <summary>
/// 新转人工申请的不可变通知快照；不包含用户资料、外部身份或供应商密钥。
/// </summary>
public sealed record HandoffNotificationSnapshot(string Provider, string NotificationCategory, string? ChatId,
    string BaseUrl, string PublicBaseUrl, DateTime RequestedAt, string? Language, string? Summary, string? AiReply,
    string? ContactEmail = null)
{
    /// <summary>
    /// 邮箱只用于通知渲染；对象摘要不得记录联系方式。
    /// </summary>
    public override string ToString() =>
        $"HandoffNotificationSnapshot {{ Provider = {Provider}, NotificationCategory = {NotificationCategory}, ContactEmail = REDACTED }}";
}

/// <summary>
/// 供应商投递的安全失败分类；可恢复故障退避，其余故障进入待人工处理状态。
/// </summary>
public sealed class NotificationDeliveryException(string code, bool retryable) : Exception(code)
{
    /// <summary>
    /// 稳定失败码，禁止放入 HTTP 正文、URL 或令牌。
    /// </summary>
    public string Code { get; } = code;
    /// <summary>
    /// 是否允许自动重试。
    /// </summary>
    public bool Retryable { get; } = retryable;
}
