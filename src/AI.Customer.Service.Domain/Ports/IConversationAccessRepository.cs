using AI.Customer.Service.Domain.Models;

namespace AI.Customer.Service.Domain.Ports;

/// <summary>
/// Conversation owner 的凭据及外部身份关系仓储，所有范围来自可信会话。
/// </summary>
public interface IConversationAccessRepository
{
    /// <summary>
    /// 按高熵凭据摘要定位唯一授权；随后必须核验生命周期及会话范围。
    /// </summary>
    Task<AnonymousConversationAccess?> CredentialAsync(string hash, CancellationToken ct);
    /// <summary>
    /// 实时重查已认证匿名主体的同范围授权。
    /// </summary>
    Task<AnonymousConversationAccess?> GuestAsync(Scope scope, CancellationToken ct);
    /// <summary>
    /// 只登记新会话凭据，不为旧会话补发。
    /// </summary>
    void AddGuest(AnonymousConversationAccess value);
    /// <summary>
    /// 按完整范围读取不可变身份绑定。
    /// </summary>
    Task<ConversationExternalIdentity?> IdentityAsync(Scope scope, CancellationToken ct);
    /// <summary>
    /// 首次绑定，由组织事务和唯一约束保证并发一致。
    /// </summary>
    void AddIdentity(ConversationExternalIdentity value);
}
