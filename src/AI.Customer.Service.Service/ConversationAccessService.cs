using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AI.Customer.Service.Common;
using AI.Customer.Service.Core;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;

namespace AI.Customer.Service.Service;

/// <summary>会话匿名凭据签发及第三方身份绑定，外部身份永远不用于授予会话访问权。</summary>
public sealed class ConversationAccessService(IAssistantRegistry assistants, ConversationRuntime runtime,
    IConversationAccessRepository repository, IUnitOfWork transactions, TimeProvider clock)
{
    /// <summary>只签发新会话，随机凭据原文返回一次，摘要与会话原子提交。</summary>
    public async Task<object> CreateAnonymousAsync(string assistantId, string trace, CancellationToken ct)
    {
        var assistant = assistants.RequirePublic(Guard.Text(assistantId, "助手"));
        var session = Guard.Id("session");
        var principal = Guard.Id("guest");
        var credential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expires = clock.GetUtcNow().UtcDateTime.AddDays(7);
        var conversation = await runtime.CreateAsync(new(assistant.TenantId, principal, trace, session), assistant, session, ct, Digest(credential), expires);
        return new { sessionId = session, tempUserId = principal, credential, expiresAt = expires, conversation.ExternalIdentityRequired };
    }

    /// <summary>凭据只授予原会话访客身份，任何组织、租户和主体均从持久授权解析。</summary>
    public async Task<AnonymousConversationAccess> AuthorizeAsync(string credential, string? session, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(credential) || !Regex.IsMatch(credential, "\\A[A-Za-z0-9_-]{43}\\z"))
            throw BusinessException.Forbidden("匿名会话凭据无效");
        var grant = await repository.CredentialAsync(Digest(credential), ct);
        if (grant is null || (session is not null && grant.ConversationId != session))
            throw BusinessException.Forbidden("匿名会话凭据无效");
        await runtime.VisitorAsync(new(grant.TenantId, grant.PrincipalId, "credential", grant.ConversationId), grant.ConversationId, ct);
        return grant;
    }

    /// <summary>原访客首次绑定；并发相同值幂等，不同值稳定冲突，不回显 cookie ID。</summary>
    public async Task<object> BindAsync(Actor actor, string session, string cookieId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cookieId) || cookieId.Length > 512)
            throw new BusinessException("EXTERNAL_IDENTITY_INVALID", "第三方身份标识不能为空且不得超过 512 字符", 400);
        var conversation = await runtime.VisitorAsync(actor, session, ct);
        return await transactions.ExecuteAsync(conversation.TenantId, conversation.TopOrgId, async () =>
        {
            var current = await runtime.VisitorAsync(actor, session, ct);
            var saved = await repository.IdentityAsync(current.Scope, ct);
            if (saved is not null && saved.ExternalAuthId != cookieId)
                throw new BusinessException("EXTERNAL_IDENTITY_ALREADY_BOUND", "此会话已绑定其他第三方身份", 409);
            if (saved is null)
            {
                saved = ConversationRuntime.Bind<ConversationExternalIdentity>(current.Scope, current.PrincipalId, Guard.Id("identity"));
                saved.ExternalAuthId = cookieId;
                saved.BoundAt = clock.GetUtcNow().UtcDateTime;
                repository.AddIdentity(saved);
            }
            return (object)new { sessionId = session, providerCode = "ADSALE", bound = true };
        }, ct);
    }

    /// <summary>只保存单向摘要，未将原始凭据作为日志或数据库键。</summary>
    private static string Digest(string credential) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));
}
