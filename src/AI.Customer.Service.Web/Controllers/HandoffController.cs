using AI.Customer.Service.Api.Contracts;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Service;
using AI.Customer.Service.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace AI.Customer.Service.Web.Controllers;

/// <summary>访客明确申请、最新状态及模型建议决策 HTTP Adapter。</summary>
/// <param name="request">访客准入。</param>
/// <param name="handoff">人工申请应用端口。</param>
public sealed class HandoffController(RequestAccess request, HandoffService handoff) : CustomerServiceControllerBase(request)
{
    /// <summary>主动申请，无需第二次确认。</summary>
    [HttpPost("conversations/{id}/handoffs")]
    public async Task<object> RequestHandoff(string id, HandoffCommand command, CancellationToken ct)
    {
        var scope = await RequestAccess.VisitorAsync(id, ct);
        return new
        {
            code = "REQUESTED",
            data = await handoff.RequestAsync(Actor, scope, command.RequestId,
            TriggerSource.VISITOR_DIRECT_REQUEST, ReasonCode.USER_REQUEST, ct),
            requestId = command.RequestId,
            retryable = false,
            serverTime = DateTime.UtcNow
        };
    }

    /// <summary>查询不创建申请，无记录时返回 episode=null。</summary>
    [HttpGet("conversations/{id}/handoffs/latest")]
    public async Task<object> Latest(string id, CancellationToken ct)
    {
        var episode = await handoff.LatestAsync(await RequestAccess.VisitorAsync(id, ct), ct);
        return Success(new { episode = episode is null ? null : VisitorEpisode(episode) });
    }

    /// <summary>恢复有效建议卡，失效建议不再展示。</summary>
    [HttpGet("conversations/{id}/handoff-proposals/pending")]
    public async Task<object> Pending(string id, CancellationToken ct) =>
        Success(new { proposals = (await handoff.PendingAsync(await RequestAccess.VisitorAsync(id, ct), ct)).Select(ChatService.ProposalView) });

    /// <summary>访客确认或拒绝建议，模型不能代为确认。</summary>
    [HttpPost("conversations/{id}/handoff-proposals/{proposal}/decision")]
    public async Task<object> Decide(string id, string proposal, ProposalDecision command, CancellationToken ct) =>
        Success(await handoff.DecideAsync(Actor, await RequestAccess.VisitorAsync(id, ct), proposal, command, ct));
}
