using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AI.Customer.Service.Common;
using AI.Customer.Service.Core;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;
using AI.Customer.Service.Service;
using AI.Customer.Service.Web.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using AuthenticationOptions = AI.Customer.Service.Web.Security.AuthenticationOptions;

namespace AI.Customer.Service.Web.Realtime;

/// <summary>人工实时单一 WebSocket Adapter，首帧复用 JWT，持久序列恢复且按连接复核权限。</summary>
public static class RealtimeEndpoint
{
    /// <summary>固定订阅范围，令牌只出现在首帧，不能在 URL 或后续帧切换。</summary>
    public static async Task HandleAsync(HttpContext context, RealtimeConnectionLimiter limiter, IServiceScopeFactory scopes,
        IOptionsMonitor<JwtBearerOptions> jwt, IOptions<AuthenticationOptions> authentication)
    {
        if (!context.WebSockets.IsWebSocketRequest || context.Request.Query.Count != 0 ||
            context.Request.Headers.Origin.Count > 0 && context.Request.Headers.Origin.ToString() != context.Request.Scheme + "://" + context.Request.Host)
        {
            context.Response.StatusCode = 400;
            return;
        }
        if (!await limiter.Slots.WaitAsync(0, context.RequestAborted))
        {
            context.Response.StatusCode = 429;
            return;
        }
        try
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            try
            {
                using var firstTimeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                firstTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                var first = await ReadFirstAsync(socket, firstTimeout.Token);
                var (principal, expires) = await AuthenticateAsync(first.Authorization, jwt.Get(JwtBearerDefaults.AuthenticationScheme), firstTimeout.Token);
                var actor = RequestAccess.Resolve(principal, context.TraceIdentifier, authentication.Value);
                var subscription = new Subscription(first.Agent, first.ConversationId, first.TopOrgId, first.AssistantConfigId);
                await CheckExternalAsync(scopes, actor, subscription, context.RequestAborted);
                var checkedAt = DateTime.UtcNow;
                var sentAt = DateTime.UtcNow;
                var cursor = first.AfterSeq;
                if (cursor < 0)
                {
                    throw BusinessException.Invalid("实时消息游标无效");
                }
                string? previous = null;
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                var receive = ReadUnexpectedAsync(socket, stop.Token);
                try
                {
                    while (socket.State == WebSocketState.Open && !stop.IsCancellationRequested)
                    {
                        if (receive.IsCompleted)
                        {
                            await receive;
                            break;
                        }
                        var now = DateTime.UtcNow;
                        if (now >= expires)
                        {
                            throw BusinessException.Forbidden("登录已过期");
                        }
                        if ((now - checkedAt).TotalSeconds >= 60)
                        {
                            await CheckExternalAsync(scopes, actor, subscription, stop.Token);
                            checkedAt = now;
                        }
                        await using var scope = scopes.CreateAsyncScope();
                        var snapshot = await SnapshotAsync(scope.ServiceProvider, actor, subscription, cursor, stop.Token);
                        var data = JsonSerializer.Serialize(snapshot, JsonDefaults.Options);
                        if (data != previous)
                        {
                            await SendAsync(socket, new { type = "snapshot", data = snapshot }, stop.Token);
                            previous = data;
                            sentAt = now;
                            var element = JsonSerializer.SerializeToElement(snapshot, JsonDefaults.Options);
                            if (element.TryGetProperty("timeline", out var timeline) && timeline.TryGetProperty("messages", out var messages))
                            {
                                foreach (var message in messages.EnumerateArray())
                                {
                                    cursor = Math.Max(cursor, message.GetProperty("seq").GetInt64());
                                }
                            }
                        }
                        else if ((now - sentAt).TotalSeconds >= 15)
                        {
                            await SendAsync(socket, new { type = "heartbeat", at = now }, stop.Token);
                            sentAt = now;
                        }
                        await Task.Delay(TimeSpan.FromSeconds(1), stop.Token);
                    }
                }
                finally
                {
                    stop.Cancel();
                    try
                    {
                        await receive;
                    }
                    catch (Exception error) when (error is OperationCanceledException or WebSocketException or BusinessException)
                    {
                        // 连接结束时主动取消唯一接收任务，避免遗留后台读取。
                    }
                }
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "连接已结束", CancellationToken.None);
                }
            }
            catch (Exception error) when (error is BusinessException or JsonException or SecurityTokenException or OperationCanceledException or WebSocketException)
            {
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "认证、权限或订阅已失效", CancellationToken.None);
                }
            }
        }
        finally
        {
            limiter.Slots.Release();
        }
    }

    /// <summary>读取唯一认证首帧，严格 8KB 上限，不把分块消息当完整 JSON。</summary>
    private static async Task<FirstFrame> ReadFirstAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var length = 0;
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(length), ct);
            if (result.MessageType != WebSocketMessageType.Text)
            {
                throw BusinessException.Invalid("首帧必须是文本认证对象");
            }
            length += result.Count;
            if (result.EndOfMessage)
            {
                return JsonSerializer.Deserialize<FirstFrame>(buffer.AsSpan(0, length), JsonDefaults.Options) ?? throw BusinessException.Invalid("首帧为空");
            }
            if (length == buffer.Length)
            {
                throw BusinessException.Invalid("认证首帧超长");
            }
        }
    }

    /// <summary>使用与 HTTP 相同的签名、issuer、audience 和 lifetime 规则验证首帧 JWT。</summary>
    private static async Task<(ClaimsPrincipal Principal, DateTime Expires)> AuthenticateAsync(string authorization, JwtBearerOptions options, CancellationToken ct)
    {
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw BusinessException.Forbidden("缺少登录令牌");
        }
        var parameters = options.TokenValidationParameters.Clone();
        if (options.ConfigurationManager is not null)
        {
            var configuration = await options.ConfigurationManager.GetConfigurationAsync(ct);
            parameters.ValidIssuer ??= configuration.Issuer;
            parameters.IssuerSigningKeys = configuration.SigningKeys;
        }
        var handler = new JsonWebTokenHandler { MapInboundClaims = false };
        var result = await handler.ValidateTokenAsync(authorization[7..], parameters);
        if (!result.IsValid)
        {
            throw BusinessException.Forbidden("登录令牌无效");
        }
        return (new ClaimsPrincipal(result.ClaimsIdentity), result.SecurityToken.ValidTo);
    }

    /// <summary>首帧及每六十秒即时复核外部账号组织，期间本地成员在每次快照检查。</summary>
    private static async Task CheckExternalAsync(IServiceScopeFactory scopes, Actor actor, Subscription subscription, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var access = scope.ServiceProvider.GetRequiredService<AccessService>();
        if (subscription.Agent)
        {
            if (string.IsNullOrWhiteSpace(subscription.TopOrgId) || string.IsNullOrWhiteSpace(subscription.AssistantConfigId))
            {
                throw BusinessException.Invalid("客服订阅缺少组织或助手");
            }
            await access.RequireAgentAsync(actor, subscription.TopOrgId, subscription.AssistantConfigId, ct);
        }
        else
        {
            var conversation = await access.VisitorAsync(actor, Guard.Text(subscription.ConversationId, "会话"), ct);
            await access.RequireAccountAsync(actor, conversation.TopOrgId, ct);
        }
    }

    /// <summary>每次快照独立作用域查询持久事实，避免 ORM 一级缓存隐藏改派或撤权。</summary>
    private static async Task<object> SnapshotAsync(IServiceProvider provider, Actor actor, Subscription sub, long afterSeq, CancellationToken ct)
    {
        var access = provider.GetRequiredService<AccessService>();
        var handoff = provider.GetRequiredService<HandoffService>();
        var messaging = provider.GetRequiredService<MessagingService>();
        if (sub.Agent)
        {
            await access.GroupsAsync(actor, sub.TopOrgId!, ct);
            var reception = provider.GetRequiredService<ReceptionService>();
            var followups = provider.GetRequiredService<FollowupService>();
            if (string.IsNullOrWhiteSpace(sub.ConversationId))
            {
                return new
                {
                    queue = (await reception.QueueAsync(actor, sub.TopOrgId!, sub.AssistantConfigId!, ct)).Select(AgentEpisodeView),
                    followups = JsonSerializer.SerializeToElement(await followups.QueueAsync(actor, sub.TopOrgId!, sub.AssistantConfigId!, ct), JsonDefaults.Options).GetProperty("followups")
                };
            }
            var scope = await access.AgentScopeAsync(actor, sub.TopOrgId!, sub.ConversationId,
                provider.GetRequiredService<IConversationRepository>(), ct);
            if (scope.AssistantConfigId != sub.AssistantConfigId)
            {
                throw BusinessException.Forbidden("订阅助手与会话不一致");
            }
            var current = await reception.RequireAsync(scope, null, ct);
            var timeline = await messaging.TimelineAsync(actor, scope, true, afterSeq, 100, ct);
            return new { reception = AgentEpisodeView(current), timeline };
        }
        var visitorScope = await access.VisitorAsync(actor, sub.ConversationId!, ct);
        var episode = await handoff.LatestAsync(visitorScope, ct);
        var waiting = await provider.GetRequiredService<WaitingService>().GetAsync(actor, visitorScope, ct);
        var latest = await handoff.LatestAsync(visitorScope, ct);
        return new
        {
            episode = latest is null ? null : new { episodeId = latest.Id, latest.Status, latest.Version, latest.TriggerSource, latest.ReasonCode, latest.RequestedAt },
            waiting,
            survey = await provider.GetRequiredService<SurveyService>().GetAsync(visitorScope, ct),
            proposals = (await handoff.PendingAsync(visitorScope, ct)).Select(ChatService.ProposalView),
            timeline = episode is null ? null : await messaging.TimelineAsync(actor, visitorScope, false, afterSeq, 100, ct)
        };
    }

    /// <summary>客服快照保留原页面字段，不携带正文。</summary>
    private static object AgentEpisodeView(Episode value) => new
    {
        value.Scope,
        episodeId = value.Id,
        value.Status,
        value.Version,
        value.PrincipalId,
        value.AgentId,
        value.GroupId,
        value.RequestedAt,
        value.TriggerSource,
        value.ReasonCode
    };

    /// <summary>订阅建立后只允许 WebSocket 关闭，不接受二次认证或客户端切换范围。</summary>
    private static async Task ReadUnexpectedAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[1];
        var result = await socket.ReceiveAsync(buffer.AsMemory(), ct);
        if (result.MessageType != WebSocketMessageType.Close)
        {
            throw BusinessException.Invalid("建立订阅后不能发送其它帧");
        }
    }

    /// <summary>仅由当前循环串行发送帧，避免并发写入 socket。</summary>
    private static Task SendAsync(WebSocket socket, object value, CancellationToken ct) =>
        socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(value, JsonDefaults.Options), WebSocketMessageType.Text, true, ct);

    /// <summary>严格认证首帧，不接受自报 tenant、principal、组或容量。</summary>
    /// <param name="Authorization">已登录 Bearer JWT。</param>
    /// <param name="Agent">是否客服订阅。</param>
    /// <param name="ConversationId">可空，客服为空时订阅队列。</param>
    /// <param name="TopOrgId">客服待核验组织。</param>
    /// <param name="AssistantConfigId">客服待核验助手。</param>
    /// <param name="AfterSeq">最后展示的人工持久序列。</param>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record FirstFrame(string Authorization, bool Agent, string? ConversationId, string? TopOrgId, string? AssistantConfigId, long AfterSeq);

    /// <summary>去除令牌后保留的固定订阅参数，不保存认证凭据。</summary>
    private sealed record Subscription(bool Agent, string? ConversationId, string? TopOrgId, string? AssistantConfigId);
}
