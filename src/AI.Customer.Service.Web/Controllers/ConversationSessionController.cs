using AI.Customer.Service.Api.Contracts;
using AI.Customer.Service.Common;
using AI.Customer.Service.Core;
using AI.Customer.Service.Domain.Ports;
using AI.Customer.Service.Service;
using AI.Customer.Service.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI.Customer.Service.Web.Controllers;

/// <summary>正式会话创建与外部身份绑定，任何客户端字段都不能指定 tenant/account。</summary>
[ApiController, Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ConversationSessionController(RequestAccess request, ConversationAccessService access,
    ConversationRuntime runtime, IAssistantRegistry assistants) : ControllerBase
{
    /// <summary>仅已登录用户创建会话；人工申请不依赖首条 AI 消息。</summary>
    [HttpPost("api/agent/session")]
    public async Task<object> Create(CreateSessionCommand command, CancellationToken ct)
    {
        if (request.Actor.GuestConversationId is not null) throw BusinessException.Forbidden();
        var value = await runtime.CreateAsync(request.Actor, assistants.Require(request.Actor.TenantId, command.AssistantConfigId), Guard.Id("session"), ct);
        return new { isSuccess = true, returnObj = new { sessionId = value.Id, value.ExternalIdentityRequired } };
    }

    /// <summary>匿名创建仅接受服务端明确公开的助手，不能对旧会话补发凭据。</summary>
    [AllowAnonymous, HttpPost("api/agent/anonymous/session")]
    public async Task<object> CreateAnonymous(CreateSessionCommand command, CancellationToken ct) =>
        new { isSuccess = true, returnObj = await access.CreateAnonymousAsync(command.AssistantConfigId, HttpContext.TraceIdentifier, ct) };

    /// <summary>登录用户只能绑定自己的已有会话，不接受匿名凭据冒充登录绑定。</summary>
    [HttpPost("api/agent/session/{sessionId}/external-identity")]
    public async Task<object> Bind(string sessionId, BindExternalIdentityCommand command, CancellationToken ct)
    {
        if (request.Actor.GuestConversationId is not null) throw BusinessException.Forbidden();
        return new { isSuccess = true, returnObj = await access.BindAsync(request.Actor, sessionId, command.CookieId, ct) };
    }

    /// <summary>匿名绑定仍需当前会话凭据，外部 cookie ID 本身不授予任何权限。</summary>
    [HttpPost("api/agent/anonymous/session/{sessionId}/external-identity")]
    public async Task<object> BindAnonymous(string sessionId, BindExternalIdentityCommand command, CancellationToken ct)
    {
        if (request.Actor.GuestConversationId is null) throw BusinessException.Forbidden();
        return new { isSuccess = true, returnObj = await access.BindAsync(request.Actor, sessionId, command.CookieId, ct) };
    }
}
