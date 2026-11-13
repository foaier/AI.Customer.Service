using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI.Customer.Service.Web.Controllers;

/// <summary>正式客服接口共同信封及可信身份，不承担业务状态推进。</summary>
/// <param name="request">当前 Web 准入。</param>
[ApiController]
[Authorize]
[Route("api/customer-service")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public abstract class CustomerServiceControllerBase(RequestAccess request) : ControllerBase
{
    /// <summary>当前请求可信身份解析器。</summary>
    protected RequestAccess RequestAccess { get; } = request;

    /// <summary>当前已验证业务主体。</summary>
    protected Actor Actor => RequestAccess.Actor;

    /// <summary>保持前端 code/data 信封格式。</summary>
    protected static object Success(object? data) => new { code = "OK", data };

    /// <summary>访客可见最新申请不暴露内部路由、客服账号和追踪号。</summary>
    protected static object VisitorEpisode(Episode episode) => new
    {
        episodeId = episode.Id,
        episode.TriggerSource,
        episode.ReasonCode,
        episode.Status,
        episode.Version,
        episode.RequestedAt
    };

    /// <summary>正式客服组内摘要，正文由认领接口另行授权。</summary>
    protected static object ReceptionView(Episode episode) => new
    {
        episode.Scope,
        episodeId = episode.Id,
        episode.Status,
        episode.Version,
        episode.PrincipalId,
        episode.AgentId,
        episode.GroupId,
        episode.RequestedAt,
        episode.TriggerSource,
        episode.ReasonCode
    };
}
