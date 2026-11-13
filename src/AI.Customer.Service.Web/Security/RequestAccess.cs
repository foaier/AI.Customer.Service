using System.Security.Claims;
using AI.Customer.Service.Common;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;
using AI.Customer.Service.Service;
using Microsoft.Extensions.Options;

namespace AI.Customer.Service.Web.Security;

/// <summary>Web 身份解析，归属只能来自已验证声明和持久会话，不读取身份自报头。</summary>
/// <param name="accessor">当前请求。</param>
/// <param name="options">认证声明映射。</param>
/// <param name="access">业务准入端口。</param>
/// <param name="conversations">会话范围只读端口。</param>
public sealed class RequestAccess(IHttpContextAccessor accessor, IOptions<AuthenticationOptions> options,
    AccessService access, IConversationRepository conversations)
{
    /// <summary>取得已验证主体及服务端追踪号。</summary>
    public Actor Actor => Resolve(accessor.HttpContext?.User ?? new ClaimsPrincipal(),
        accessor.HttpContext?.TraceIdentifier ?? Guard.Id("trace"), options.Value);

    /// <summary>从指定已验证主体构造业务身份，供 WebSocket 首帧认证复用。</summary>
    public static Actor Resolve(ClaimsPrincipal principal, string trace, AuthenticationOptions options)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            throw new BusinessException("UNAUTHENTICATED", "需要正式登录", 401);
        }
        var tenant = principal.FindFirstValue(options.TenantClaim);
        var account = principal.FindFirstValue(options.AccountClaim);
        if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(account))
        {
            throw new BusinessException("UNAUTHENTICATED", "登录声明缺少租户或账号", 401);
        }
        return new(Guard.Text(tenant, "认证租户", 64), Guard.Text(account, "认证账号"), trace);
    }

    /// <summary>按原访客持久关系授权。</summary>
    public Task<Scope> VisitorAsync(string id, CancellationToken ct) => access.VisitorAsync(Actor, id, ct);

    /// <summary>每次客服 HTTP 即时核验外部账号组织，再解析完整会话范围。</summary>
    public async Task<Scope> AgentAsync(string org, string id, CancellationToken ct)
    {
        await access.RequireAccountAsync(Actor, org, ct);
        return await access.AgentScopeAsync(Actor, org, id, conversations, ct);
    }

    /// <summary>队列及在线操作核验外部账号、助手归属及本地成员。</summary>
    public Task AgentListAsync(string org, string assistant, CancellationToken ct) => access.RequireAgentAsync(Actor, org, assistant, ct);

    /// <summary>配置管理员按目标组织外部关系授权，不以客户端页面角色授权。</summary>
    public Task<Member> ManagerAsync(string org, CancellationToken ct) => access.RequireAccountAsync(Actor, org, ct);
}
