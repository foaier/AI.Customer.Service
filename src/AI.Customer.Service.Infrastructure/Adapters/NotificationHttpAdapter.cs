using System.Net.Http.Json;
using System.Text.Json;
using AI.Customer.Service.Common;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;
using AI.Customer.Service.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace AI.Customer.Service.Infrastructure.Adapters;

/// <summary>可靠通知协议适配，HTTP 成功还需 accepted=true；企微需 errcode=0。</summary>
/// <param name="http">短超时且不跟随重定向的客户端。</param>
/// <param name="options">可选通知及事件网关。</param>
public sealed class NotificationHttpAdapter(HttpClient http, IOptions<NotificationOptions> options) : INotificationPort
{
    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedKinds
    {
        get
        {
            var kinds = new List<string>();
            var value = options.Value;
            if (value.Enabled && !string.IsNullOrWhiteSpace(value.Endpoint))
            {
                kinds.AddRange(["EMAIL", "EMAIL_REPLY", "INTERNAL"]);
            }
            if (value.Enabled && !string.IsNullOrWhiteSpace(value.WeComWebhook) && !kinds.Contains("INTERNAL"))
            {
                kinds.Add("INTERNAL");
            }
            if (value.EventsEnabled && !string.IsNullOrWhiteSpace(value.EventsEndpoint))
            {
                kinds.Add("EVENT");
            }
            return kinds;
        }
    }

    /// <inheritdoc />
    public async Task SendAsync(ReliableTask task, CancellationToken ct)
    {
        var value = options.Value;
        var weCom = task.Kind == "INTERNAL" && !string.IsNullOrWhiteSpace(value.WeComWebhook);
        var endpoint = weCom ? value.WeComWebhook : task.Kind == "EVENT" ? value.EventsEndpoint : value.Endpoint;
        if (!SupportedKinds.Contains(task.Kind) || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not "http" and not "https")
        {
            throw BusinessException.Unavailable("任务通知通道尚未配置");
        }
        if (weCom && (uri.Scheme != "https" || uri.Host != "qyapi.weixin.qq.com" || uri.AbsolutePath != "/cgi-bin/webhook/send" || !string.IsNullOrEmpty(uri.UserInfo)))
        {
            throw BusinessException.Invalid("企微地址必须为官方 HTTPS 群机器人接口");
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", task.EventKey);
        request.Headers.TryAddWithoutValidation("X-Trace-Id", task.TraceId);
        if (!weCom && !string.IsNullOrWhiteSpace(value.Authorization))
        {
            request.Headers.TryAddWithoutValidation("Authorization", value.Authorization);
        }
        request.Content = weCom
            ? JsonContent.Create(new { msgtype = "text", text = new { content = "客服待接待提醒：会话 " + task.ConversationId + "，请在工作台处理。" } })
            : JsonContent.Create(new
            {
                task.EventKey,
                task.Kind,
                sessionId = task.ConversationId,
                task.EpisodeId,
                task.TenantId,
                task.TopOrgId,
                task.BventId,
                task.TraceId,
                payload = JsonSerializer.Deserialize<JsonElement>(task.Payload)
            }, options: JsonDefaults.Options);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Options, ct);
        var accepted = weCom
            ? result.TryGetProperty("errcode", out var code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var number) && number == 0
            : result.TryGetProperty("accepted", out var receipt) && receipt.ValueKind == JsonValueKind.True;
        if (!accepted)
        {
            throw BusinessException.Unavailable("通知网关未明确受理");
        }
    }
}
