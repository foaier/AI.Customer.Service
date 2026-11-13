using AI.Customer.Service.Api.Contracts;
using AI.Customer.Service.Service;
using AI.Customer.Service.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace AI.Customer.Service.Web.Controllers;

/// <summary>人工纯文本、持久序列恢复、回执及 AI 最小历史 HTTP Adapter。</summary>
/// <param name="request">访客与客服准入。</param>
/// <param name="messages">消息应用端口。</param>
public sealed class MessagingController(RequestAccess request, MessagingService messages) : CustomerServiceControllerBase(request)
{
    /// <summary>访客以稳定 clientMsgId 发送纯文本。</summary>
    [HttpPost("conversations/{id}/messages")]
    public async Task<object> VisitorSend(string id, MessageCommand command, CancellationToken ct) =>
        Success(await messages.SendAsync(Actor, await RequestAccess.VisitorAsync(id, ct), false, command, ct));

    /// <summary>当前认领客服发送纯文本，首次有效消息推进 ACTIVE。</summary>
    [HttpPost("agents/conversations/{id}/messages")]
    public async Task<object> AgentSend(string id, string topOrgId, MessageCommand command, CancellationToken ct) =>
        Success(await messages.SendAsync(Actor, await RequestAccess.AgentAsync(topOrgId, id, ct), true, command, ct));

    /// <summary>访客按序列恢复时间线。</summary>
    [HttpGet("conversations/{id}/messages")]
    public async Task<object> VisitorMessages(string id, CancellationToken ct, long afterSeq = 0, int limit = 100) =>
        Success(await messages.TimelineAsync(Actor, await RequestAccess.VisitorAsync(id, ct), false, afterSeq, limit, ct));

    /// <summary>当前认领客服按序列恢复正文。</summary>
    [HttpGet("agents/conversations/{id}/messages")]
    public async Task<object> AgentMessages(string id, string topOrgId, CancellationToken ct, long afterSeq = 0, int limit = 100) =>
        Success(await messages.TimelineAsync(Actor, await RequestAccess.AgentAsync(topOrgId, id, ct), true, afterSeq, limit, ct));

    /// <summary>访客明确确认自身水位。</summary>
    [HttpPost("conversations/{id}/receipts")]
    public async Task<object> VisitorReceipt(string id, ReceiptCommand command, CancellationToken ct) =>
        Success(await messages.ReceiptAsync(Actor, await RequestAccess.VisitorAsync(id, ct), false, command, ct));

    /// <summary>客服明确确认自身水位，不能代访客已读。</summary>
    [HttpPost("agents/conversations/{id}/receipts")]
    public async Task<object> AgentReceipt(string id, string topOrgId, ReceiptCommand command, CancellationToken ct) =>
        Success(await messages.ReceiptAsync(Actor, await RequestAccess.AgentAsync(topOrgId, id, ct), true, command, ct));

    /// <summary>仅当前认领者可读接待前的 AI 文本上下文。</summary>
    [HttpGet("agents/conversations/{id}/history")]
    public async Task<object> History(string id, string topOrgId, CancellationToken ct) =>
        Success(await messages.HistoryAsync(Actor, await RequestAccess.AgentAsync(topOrgId, id, ct), ct));
}
