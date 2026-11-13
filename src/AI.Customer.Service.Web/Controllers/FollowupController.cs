using System.Text.Json;
using AI.Customer.Service.Api.Contracts;
using AI.Customer.Service.Common;
using AI.Customer.Service.Service;
using AI.Customer.Service.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace AI.Customer.Service.Web.Controllers;

/// <summary>当前会话等待及固定原周期留言回复 HTTP Adapter。</summary>
/// <param name="request">正式访客或客服准入。</param>
/// <param name="waiting">等待应用端口。</param>
/// <param name="followups">跟进工单应用端口。</param>
public sealed class FollowupController(RequestAccess request, WaitingService waiting, FollowupService followups) : CustomerServiceControllerBase(request)
{
    /// <summary>读取时按需恢复期限，不创建全库扫描任务。</summary>
    [HttpGet("conversations/{id}/waiting")]
    public async Task<object> Waiting(string id, CancellationToken ct) => Success(await waiting.GetAsync(Actor, await RequestAccess.VisitorAsync(id, ct), ct));

    /// <summary>访客继续等待、保存邮箱或选择邮件。</summary>
    [HttpPost("conversations/{id}/waiting")]
    public async Task<object> Choose(string id, WaitingChoice command, CancellationToken ct) => Success(await waiting.ChooseAsync(Actor, await RequestAccess.VisitorAsync(id, ct), command, ct));

    /// <summary>提交明确留言，固定原周期。</summary>
    [HttpPost("conversations/{id}/followup-form")]
    public async Task<object> Submit(string id, FollowupFormCommand command, CancellationToken ct) => Success(await followups.SubmitAsync(Actor, await RequestAccess.VisitorAsync(id, ct), command, ct));

    /// <summary>原访客恢复已提交留言。</summary>
    [HttpGet("conversations/{id}/followup-form")]
    public async Task<object> Form(string id, CancellationToken ct) => Success(await followups.GetFormAsync(Actor, await RequestAccess.VisitorAsync(id, ct), null, false, ct));

    /// <summary>有资格组的跟进摘要列表，不包含邮箱和正文。</summary>
    [HttpGet("agents/followups")]
    public async Task<object> Queue(string topOrgId, string assistantConfigId, CancellationToken ct)
    {
        await RequestAccess.AgentListAsync(topOrgId, assistantConfigId, ct);
        var result = await followups.QueueAsync(Actor, topOrgId, assistantConfigId, ct);
        return Success(JsonSerializer.SerializeToElement(result, JsonDefaults.Options).GetProperty("followups"));
    }

    /// <summary>原组客服读取原周期等待和通知状态，不返回邮箱。</summary>
    [HttpGet("agents/conversations/{id}/waiting")]
    public async Task<object> AgentWaiting(string id, string topOrgId, CancellationToken ct, string? episodeId = null) =>
        Success(await waiting.GetAgentAsync(Actor, await RequestAccess.AgentAsync(topOrgId, id, ct), episodeId, ct));

    /// <summary>原组客服显式恢复失败通知，原事件键保持不变。</summary>
    [HttpPost("agents/conversations/{id}/notifications/retry")]
    public async Task<object> RetryNotifications(string id, string topOrgId, CancellationToken ct, string? episodeId = null) =>
        Success(await waiting.RetryNotificationsAsync(Actor, await RequestAccess.AgentAsync(topOrgId, id, ct), episodeId, ct));

    /// <summary>固定原周期读取表单，认领前只有摘要。</summary>
    [HttpGet("agents/conversations/{id}/followups/{episode}/form")]
    public async Task<object> AgentForm(string id, string episode, string topOrgId, CancellationToken ct) =>
        Success(await followups.GetFormAsync(Actor, await RequestAccess.AgentAsync(topOrgId, id, ct), episode, true, ct));

    /// <summary>原业务组客服原子认领留言。</summary>
    [HttpPost("agents/conversations/{id}/followups/{episode}/claim")]
    public async Task<object> Claim(string id, string episode, string topOrgId, StateCommand command, CancellationToken ct) =>
        Success(await followups.ClaimAsync(Actor, await RequestAccess.AgentAsync(topOrgId, id, ct), episode, command, ct));

    /// <summary>当前认领者读取最终回复生命周期。</summary>
    [HttpGet("agents/conversations/{id}/followups/{episode}")]
    public async Task<object> GetResponse(string id, string episode, string topOrgId, CancellationToken ct) =>
        Success(await followups.GetResponseAsync(Actor, await RequestAccess.AgentAsync(topOrgId, id, ct), episode, ct));

    /// <summary>保存最终正文及可靠任务，不能从客户端指定收件人。</summary>
    [HttpPost("agents/conversations/{id}/followups/{episode}/reply")]
    public async Task<object> Reply(string id, string episode, string topOrgId, ReplyCommand command, CancellationToken ct) =>
        Success(await followups.ReplyAsync(Actor, await RequestAccess.AgentAsync(topOrgId, id, ct), episode, command, ct));

    /// <summary>最终失败后人工恢复原投递任务。</summary>
    [HttpPost("agents/conversations/{id}/followups/{episode}/retry")]
    public async Task<object> Retry(string id, string episode, string topOrgId, StateCommand command, CancellationToken ct) =>
        Success(await followups.ChangeResponseAsync(Actor, await RequestAccess.AgentAsync(topOrgId, id, ct), episode, command, false, ct));

    /// <summary>网关已受理后显式结案，不把待发送当作处理完成。</summary>
    [HttpPost("agents/conversations/{id}/followups/{episode}/close")]
    public async Task<object> Close(string id, string episode, string topOrgId, StateCommand command, CancellationToken ct) =>
        Success(await followups.ChangeResponseAsync(Actor, await RequestAccess.AgentAsync(topOrgId, id, ct), episode, command, true, ct));
}
