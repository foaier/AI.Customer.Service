using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;
using Microsoft.EntityFrameworkCore;

namespace AI.Customer.Service.Infrastructure.Persistence;

/// <summary>
/// 仅使用 ORM 保存会话授权，不跨 owner 访问用户中心主数据。
/// </summary>
public sealed class ConversationAccessRepository(CustomerServiceDbContext db) : IConversationAccessRepository
{
    /// <summary>
    /// 按凭据摘要只读查询唯一匿名授权，授权有效期及会话范围由调用方继续核验。
    /// </summary>
    public Task<AnonymousConversationAccess?> CredentialAsync(string hash, CancellationToken ct) =>
        db.Set<AnonymousConversationAccess>().AsNoTracking().SingleOrDefaultAsync(x => x.CredentialHash == hash, ct);
    /// <summary>
    /// 按完整会话范围只读查询匿名授权，供调用方实时复核有效期和撤销状态。
    /// </summary>
    public Task<AnonymousConversationAccess?> GuestAsync(Scope s, CancellationToken ct) =>
        db.Set<AnonymousConversationAccess>().AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == s.TenantId && x.TopOrgId == s.TopOrgId &&
            x.ConversationId == s.ConversationId && x.AssistantConfigId == s.AssistantConfigId && x.BventId == s.BventId, ct);
    /// <summary>
    /// 将新会话的匿名授权加入当前上下文，等待共享事务提交。
    /// </summary>
    public void AddGuest(AnonymousConversationAccess value) => db.Add(value);
    /// <summary>
    /// 按完整会话范围读取 ADSALE 外部身份绑定。
    /// </summary>
    public Task<ConversationExternalIdentity?> IdentityAsync(Scope s, CancellationToken ct) =>
        db.Set<ConversationExternalIdentity>().SingleOrDefaultAsync(x => x.TenantId == s.TenantId && x.TopOrgId == s.TopOrgId &&
            x.ConversationId == s.ConversationId && x.AssistantConfigId == s.AssistantConfigId && x.BventId == s.BventId && x.ProviderCode == "ADSALE", ct);
    /// <summary>
    /// 将首次外部身份绑定加入当前上下文，由共享事务和唯一约束保证一致性。
    /// </summary>
    public void AddIdentity(ConversationExternalIdentity value) => db.Add(value);
}
