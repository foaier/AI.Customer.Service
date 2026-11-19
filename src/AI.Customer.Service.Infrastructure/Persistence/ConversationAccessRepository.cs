using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;
using Microsoft.EntityFrameworkCore;

namespace AI.Customer.Service.Infrastructure.Persistence;

/// <summary>仅使用 ORM 保存会话授权，不跨 owner 访问用户中心主数据。</summary>
public sealed class ConversationAccessRepository(CustomerServiceDbContext db) : IConversationAccessRepository
{
    /// <inheritdoc />
    public Task<AnonymousConversationAccess?> CredentialAsync(string hash, CancellationToken ct) =>
        db.Set<AnonymousConversationAccess>().AsNoTracking().SingleOrDefaultAsync(x => x.CredentialHash == hash, ct);
    /// <inheritdoc />
    public Task<AnonymousConversationAccess?> GuestAsync(Scope s, CancellationToken ct) =>
        db.Set<AnonymousConversationAccess>().AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == s.TenantId && x.TopOrgId == s.TopOrgId &&
            x.ConversationId == s.ConversationId && x.AssistantConfigId == s.AssistantConfigId && x.BventId == s.BventId, ct);
    /// <inheritdoc />
    public void AddGuest(AnonymousConversationAccess value) => db.Add(value);
    /// <inheritdoc />
    public Task<ConversationExternalIdentity?> IdentityAsync(Scope s, CancellationToken ct) =>
        db.Set<ConversationExternalIdentity>().SingleOrDefaultAsync(x => x.TenantId == s.TenantId && x.TopOrgId == s.TopOrgId &&
            x.ConversationId == s.ConversationId && x.AssistantConfigId == s.AssistantConfigId && x.BventId == s.BventId && x.ProviderCode == "ADSALE", ct);
    /// <inheritdoc />
    public void AddIdentity(ConversationExternalIdentity value) => db.Add(value);
}
