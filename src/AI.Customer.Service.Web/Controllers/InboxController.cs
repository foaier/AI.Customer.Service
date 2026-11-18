using AI.Customer.Service.Service;
using AI.Customer.Service.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace AI.Customer.Service.Web.Controllers;

/// <summary>客服收件箱全范围提醒汇总 HTTP Adapter。</summary>
public sealed class InboxController(RequestAccess request, AccessService access, InboxService inbox) : CustomerServiceControllerBase(request)
{
    /// <summary>查询本人当前组权限内的消息、留言和待接待提醒。</summary>
    [HttpGet("agents/inbox/summary")]
    public async Task<object> Summary(string topOrgId, CancellationToken ct, string? assistantConfigId = null)
    {
        await access.RequireAccountAsync(Actor, topOrgId, ct);
        if (assistantConfigId is not null) access.RequireAssistant(Actor, topOrgId, assistantConfigId);
        return Success(await inbox.SummaryAsync(Actor, topOrgId, assistantConfigId, ct));
    }
}
