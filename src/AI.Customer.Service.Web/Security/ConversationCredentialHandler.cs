using System.Security.Claims;
using System.Text.Encodings.Web;
using AI.Customer.Service.Common;
using AI.Customer.Service.Service;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AI.Customer.Service.Web.Security;

/// <summary>
/// 匿名凭据独立认证方案，只映射到固定会话访客；不能冒充登录账号。
/// </summary>
public sealed class ConversationCredentialHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, ConversationAccessService access, IOptions<AuthenticationOptions> claims)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>
    /// 方案及凭据头常量，不接受 URL 中的普通 HTTP 凭据。
    /// </summary>
    public const string SchemeName = "ConversationCredential";
    /// <summary>
    /// 只用于访客 HTTP；WebSocket 另行验证 credential query。
    /// </summary>
    public const string Header = "X-Conversation-Credential";

    /// <summary>
    /// 登录凭据和匿名凭据不能混用，失效时只返回认证失败。
    /// </summary>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var values)) return AuthenticateResult.NoResult();
        if (values.Count != 1 || Request.Headers.ContainsKey("Authorization")) return AuthenticateResult.Fail("凭据不能混用");
        try
        {
            var grant = await access.AuthorizeAsync(values[0]!, null, Context.RequestAborted);
            var identity = new ClaimsIdentity([
                new Claim(claims.Value.AccountClaim, grant.PrincipalId),
                new Claim("conversation_id", grant.ConversationId)
            ], SchemeName);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
        }
        catch (BusinessException) { return AuthenticateResult.Fail("匿名会话凭据无效或已失效"); }
    }
}
