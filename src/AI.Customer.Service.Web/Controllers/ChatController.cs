using System.Text.Json;
using AI.Customer.Service.Api.Contracts;
using AI.Customer.Service.Common;
using AI.Customer.Service.Core;
using AI.Customer.Service.Domain.Ports;
using AI.Customer.Service.Service;
using AI.Customer.Service.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;

namespace AI.Customer.Service.Web.Controllers;

/// <summary>
/// AI 会话 Adapter，提供页面接口和 SSE 响应信封。
/// </summary>
/// <param name="request">
/// 正式登录身份。
/// </param>
/// <param name="assistants">
/// 可信助手注册表。
/// </param>
/// <param name="runtime">
/// Conversation owner。
/// </param>
/// <param name="conversations">
/// 最小真实历史。
/// </param>
/// <param name="chat">
/// 模型与工具编排。
/// </param>
[ApiController]
[Authorize]
[Route("api")]
public sealed class ChatController(RequestAccess request, IAssistantRegistry assistants, ConversationRuntime runtime,
    IConversationRepository conversations, ChatService chat) : ControllerBase
{
    /// <summary>
    /// 查询当前租户注册的助手，兼容设置页最小列表。
    /// </summary>
    [HttpGet("assistant/config/list")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public object List() => new { isSuccess = true, returnObj = assistants.List().Select(x => new { id = x.Id, configId = x.Id, assistantConfigId = x.Id, name = x.Name }) };

    /// <summary>
    /// 原设置页的助手选择列表，不按客户端身份头授权。
    /// </summary>
    [HttpGet("assistant/config/listAssistants")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public object ListAssistants() => new
    {
        isSuccess = true,
        returnObj = assistants.List()
        .Select(x => new { assistantConfigId = x.Id, buttonName = x.Name })
    };

    /// <summary>
    /// 可信运行配置只来自服务端注册表。
    /// </summary>
    [HttpGet("assistant/config/getRuntimeConfig")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public object RuntimeConfig(string assistantConfigId)
    {
        var value = assistants.Require(assistantConfigId);
        return new
        {
            isSuccess = true,
            returnObj = new
            {
                assistantConfigId = value.Id,
                value.BventId,
                scopeType = value.BventId is null ? "ORGANIZATION" : "EVENT",
                resolvedLanguage = value.Language
            }
        };
    }

    /// <summary>
    /// 生成服务端会话引用；显式给助手时立即持久绑定，否则首个聊天请求绑定。
    /// </summary>
    [HttpGet("agent/sessionId")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<object> Session(CancellationToken ct, string? assistantConfigId = null)
    {
        var id = Guard.Id("session");
        if (assistantConfigId is not null)
        {
            await runtime.CreateAsync(request.Actor, assistants.Require(assistantConfigId), id, ct);
        }
        return new { isSuccess = true, returnObj = new { sessionId = id } };
    }

    /// <summary>
    /// 文本 SSE 不包含人工实时状态的第二套通道。
    /// </summary>
    [HttpPost("agent/chatStream")]
    public async Task Stream(ChatCommand command, CancellationToken ct)
    {
        if (command.TempUserId is not null && command.TempUserId != request.Actor.AccountId)
            throw BusinessException.Forbidden("临时主体与会话凭据不一致");
        if (command.IsReconnect || command.SpeechEnabled)
        {
            throw BusinessException.Invalid("当前只支持新文本轮次，重连请读取持久历史");
        }
        var extension = command.Extensions?.GetValueOrDefault("event-assistant");
        var existing = await conversations.FindAsync(command.SessionId, ct);
        var assistant = assistants.Require(extension?.AssistantConfigId ?? existing?.AssistantConfigId ?? "");
        if (!string.IsNullOrEmpty(extension?.BventId) && extension.BventId != assistant.BventId)
        {
            throw BusinessException.Forbidden("大会引用与可信助手不一致");
        }
        var conversation = await runtime.CreateAsync(request.Actor, assistant, Guard.Text(command.SessionId, "会话"), ct);
        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Accel-Buffering"] = "no";
        await foreach (var item in chat.StreamAsync(request.Actor, conversation.Scope, command.UserInput, ct))
        {
            await Response.WriteAsync("event: " + item.Event + "\ndata: " + JsonSerializer.Serialize(item.Data, JsonDefaults.Options) + "\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }

    /// <summary>
    /// 沿用原匿名 SSE 路径，要求服务端签发的会话凭据。
    /// </summary>
    [HttpPost("agent/anonymous/chatStream")]
    public Task StreamAnonymous(ChatCommand command, CancellationToken ct)
    {
        if (request.Actor.GuestConversationId is null) throw BusinessException.Forbidden();
        return Stream(command, ct);
    }

    /// <summary>
    /// 沿用原匿名历史 POST，临时主体不替代会话凭据。
    /// </summary>
    [HttpPost("agent/anonymous/history_detail")]
    public Task<object> HistoryAnonymous(AnonymousHistoryCommand command, CancellationToken ct)
    {
        if (request.Actor.GuestConversationId is null ||
            (command.TempUserId is not null && command.TempUserId != request.Actor.AccountId))
            throw BusinessException.Forbidden();
        return History(command.SessionId, ct, command.AssistantConfigId);
    }

    /// <summary>
    /// 原访客恢复成功 AI 文本，不含系统提示词及失败回答。
    /// </summary>
    [HttpGet("agent/history_detail")]
    public async Task<object> History(string sessionId, CancellationToken ct, string? assistantConfigId = null)
    {
        var value = await runtime.VisitorAsync(request.Actor, sessionId, ct);
        if (assistantConfigId is not null && value.AssistantConfigId != assistantConfigId)
        {
            throw BusinessException.Forbidden("助手与会话不一致");
        }
        var turns = await conversations.TurnsAsync(value.Scope, 1000, ct);
        return new
        {
            isSuccess = true,
            returnObj = new
            {
                messages = turns.Where(x => x.Status == "SUCCEEDED").SelectMany(x => new[]
        {
            new { id = x.Id + ":user", role = "user", content = x.UserContent },
            new { id = x.Id + ":assistant", role = "assistant", content = x.AssistantContent! }
        })
            }
        };
    }
}
