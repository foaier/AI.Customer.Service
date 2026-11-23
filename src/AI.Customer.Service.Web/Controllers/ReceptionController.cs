using AI.Customer.Service.Api.Contracts;
using AI.Customer.Service.Service;
using AI.Customer.Service.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace AI.Customer.Service.Web.Controllers;

/// <summary>
/// 接待队列、原子认领、结束和取消的 HTTP Adapter。
/// </summary>
/// <param name="request">
/// 正式身份解析。
/// </param>
/// <param name="reception">
/// 接待应用端口。
/// </param>
/// <param name="access">
/// 本地冻结组核验。
/// </param>
/// <param name="inbox">
/// 收件箱提醒投影。
/// </param>
public sealed class ReceptionController(RequestAccess request, ReceptionService reception, AccessService access, InboxService inbox) : CustomerServiceControllerBase(request)
{
    /// <summary>
    /// 查询有资格组的待接单和本人接待。
    /// </summary>
    [HttpGet("agents/queue")]
    public async Task<object> Queue(string assistantConfigId, CancellationToken ct)
    {
        await RequestAccess.AgentListAsync(assistantConfigId, ct);
        return Success(await inbox.QueueAsync(Actor, assistantConfigId, ct));
    }

    /// <summary>
    /// 访客读取安全接待状态。
    /// </summary>
    [HttpGet("conversations/{id}/reception")]
    public async Task<object> VisitorReception(string id, CancellationToken ct) =>
        Success(VisitorEpisode(await reception.RequireAsync(await RequestAccess.VisitorAsync(id, ct), null, ct)));

    /// <summary>
    /// 客服读取冻结组内接待摘要。
    /// </summary>
    [HttpGet("agents/conversations/{id}/reception")]
    public async Task<object> AgentReception(string id, CancellationToken ct)
    {
        var episode = await reception.RequireAsync(await RequestAccess.AgentAsync(id, ct), null, ct);
        await access.RequireGroupAsync(Actor, episode, ct);
        return Success(ReceptionView(episode));
    }

    /// <summary>
    /// 客服在容量内直接手动接单。
    /// </summary>
    [HttpPost("agents/conversations/{id}/claim")]
    public async Task<object> Claim(string id, StateCommand command, CancellationToken ct) =>
        Success(await reception.ClaimAsync(Actor, await RequestAccess.AgentAsync(id, ct), command, ct));

    /// <summary>
    /// 原认领客服明确结束接待。
    /// </summary>
    [HttpPost("agents/conversations/{id}/resolve")]
    public async Task<object> Resolve(string id, StateCommand command, CancellationToken ct) =>
        Success(await reception.FinishAsync(Actor, await RequestAccess.AgentAsync(id, ct), command, false, ct));

    /// <summary>
    /// 原访客取消尚未被客服认领的申请。
    /// </summary>
    [HttpPost("conversations/{id}/reception/cancel")]
    public async Task<object> Cancel(string id, StateCommand command, CancellationToken ct) =>
        Success(await reception.FinishAsync(Actor, await RequestAccess.VisitorAsync(id, ct), command, true, ct));
}
