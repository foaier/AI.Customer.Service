namespace AI.Customer.Service.Domain.Models;

/// <summary>Conversation owner 的匿名访问授权；原始凭据只在创建响应中返回一次。</summary>
public sealed class AnonymousConversationAccess : ConversationAggregate
{
    /// <summary>256 位随机凭据的 SHA-256 摘要，不保存明文。</summary>
    public string CredentialHash { get; set; } = "";
    /// <summary>固定七天有效期，UTC。</summary>
    public DateTime ExpiresAt { get; set; }
    /// <summary>非空即撤销，不删除原会话和消息。</summary>
    public DateTime? RevokedAt { get; set; }
}

/// <summary>Conversation owner 的不可覆盖第三方身份绑定；它不授予会话或客服权限。</summary>
public sealed class ConversationExternalIdentity : ConversationAggregate
{
    /// <summary>当前仅接受 ADSALE，服务端固定。</summary>
    public string ProviderCode { get; set; } = "ADSALE";
    /// <summary>不透明第三方 cookie ID，最多 512 字符，不回显或进入通知载荷。</summary>
    public string ExternalAuthId { get; set; } = "";
    /// <summary>首次成功绑定的 UTC 时间。</summary>
    public DateTime BoundAt { get; set; }
}
