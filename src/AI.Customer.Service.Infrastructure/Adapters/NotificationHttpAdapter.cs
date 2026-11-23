using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AI.Customer.Service.Common;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;
using AI.Customer.Service.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace AI.Customer.Service.Infrastructure.Adapters;

/// <summary>
/// 官方企微、雅式及邮件网关的独立协议适配，通知开关只读取共享持久配置。
/// </summary>
public sealed class NotificationHttpAdapter(HttpClient http, IOptions<NotificationOptions> options,
    IConfigurationRepository configuration, IReceptionRepository reception, ISecretCipher cipher, TimeProvider clock) : INotificationPort
{
    /// <inheritdoc />
    public bool UseConfiguredChatGroups => true;

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedKinds
    {
        get
        {
            var kinds = new List<string>();
            if (!string.IsNullOrWhiteSpace(options.Value.Endpoint)) kinds.AddRange(["EMAIL", "EMAIL_REPLY"]);
            if (options.Value.EventsEnabled && !string.IsNullOrWhiteSpace(options.Value.EventsEndpoint)) kinds.Add("EVENT");
            return kinds;
        }
    }

    /// <inheritdoc />
    public async Task SendAsync(ReliableTask task, CancellationToken ct)
    {
        if (task.Kind == "EVENT")
        {
            if (!SupportedKinds.Contains("EVENT")) throw new NotificationDeliveryException("CONFIG_DISABLED", true);
            await SendGatewayAsync(task, options.Value.EventsEndpoint, ct);
            return;
        }
        var settings = await configuration.ChatNotificationAsync(task.TenantId, task.TopOrgId, ct);
        if (settings?.Enabled != true) throw new NotificationDeliveryException("CONFIG_DISABLED", true);
        if (task.Kind is "EMAIL" or "EMAIL_REPLY")
        {
            await SendGatewayAsync(task, options.Value.Endpoint, ct);
            return;
        }
        if (task.Kind is not ("HANDOFF_NOTIFY" or "INTERNAL")) throw new NotificationDeliveryException("CHANNEL_INVALID", false);
        HandoffNotificationSnapshot snapshot;
        if (task.Kind == "HANDOFF_NOTIFY")
        {
            try { snapshot = JsonSerializer.Deserialize<HandoffNotificationSnapshot>(task.Payload, JsonDefaults.Options)!; }
            catch (JsonException) { throw new NotificationDeliveryException("SNAPSHOT_INVALID", false); }
            if (snapshot is null || snapshot.Provider != settings.ActiveProvider)
                throw new NotificationDeliveryException("CONFIG_PROVIDER_MISMATCH", false);
        }
        else
        {
            var episode = await reception.EpisodeAsync(task.Scope, task.EpisodeId, ct)
                ?? throw new NotificationDeliveryException("EPISODE_MISSING", false);
            var category = ChatNotificationConfiguration.GroupKey(episode.Category);
            snapshot = new(settings.ActiveProvider, category, settings.ChatGroup.GetValueOrDefault(category), settings.BaseUrl,
                settings.PublicBaseUrl, episode.RequestedAt, null, null, null);
        }
        var text = HandoffNotificationText.Render(task, snapshot, clock.GetUtcNow().UtcDateTime);
        if (snapshot.Provider == "WECOM_ROBOT")
        {
            if (!settings.ChatGroup.TryGetValue(snapshot.NotificationCategory, out var hook) || !Regex.IsMatch(hook, "\\A[A-Za-z0-9_-]{10,200}\\z"))
                throw new NotificationDeliveryException("CONFIG_WECOM_HOOK_ID_INVALID", false);
            await PostAsync(task, new Uri("https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=" + Uri.EscapeDataString(hook)),
                new { msgtype = "text", text = new { content = text } }, "WECOM_ROBOT", null, ct);
        }
        else if (snapshot.Provider == "ADSALE")
        {
            if (snapshot.BaseUrl != "https://hub.adsaleinfo.com/open-api" || string.IsNullOrWhiteSpace(snapshot.ChatId))
                throw new NotificationDeliveryException("CONFIG_ADSALE_ROUTE_INVALID", false);
            if (settings.ApiTokenCiphertext is null) throw new NotificationDeliveryException("CONFIG_TOKEN_MISSING", false);
            string token;
            try { token = cipher.Unprotect(settings.ApiTokenCiphertext); }
            catch (BusinessException) { throw new NotificationDeliveryException("CONFIG_SECRET_INVALID", false); }
            await PostAsync(task, new Uri(snapshot.BaseUrl + "/messages/send"), new
            {
                recipient_type = "group",
                chat_id = snapshot.ChatId,
                msg_type = "text",
                content = new { content = text }
            }, "ADSALE", token, ct);
        }
        else throw new NotificationDeliveryException("CONFIG_PROVIDER_INVALID", false);
    }

    /// <summary>
    /// 邮件及审计网关仍由部署提供地址和认证，JSON 受理必须明确为 true。
    /// </summary>
    private Task SendGatewayAsync(ReliableTask task, string endpoint, CancellationToken ct)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length > 0)
            throw new NotificationDeliveryException("CONFIG_GATEWAY_MISSING", false);
        return PostAsync(task, uri, new
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
        }, "GATEWAY", options.Value.Authorization, ct);
    }

    /// <summary>
    /// 发送到已验证地址且不跟随跳转，响应正文和凭据不进入异常或日志。
    /// </summary>
    private async Task PostAsync(ReliableTask task, Uri endpoint, object payload, string provider, string? authorization, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", task.EventKey);
        request.Headers.TryAddWithoutValidation("X-Trace-Id", task.TraceId);
        if (!string.IsNullOrEmpty(authorization))
            request.Headers.TryAddWithoutValidation(provider == "ADSALE" ? "X-API-Key" : "Authorization", authorization);
        request.Content = JsonContent.Create(payload, options: JsonDefaults.Options);
        using var response = await http.SendAsync(request, ct);
        JsonElement receipt = default;
        if (provider == "ADSALE")
        {
            // 即使供应商拒绝，也只保留合法 request_id 供人工对账，不持久化响应正文。
            try { receipt = await response.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Options, ct); }
            catch (JsonException) when (!response.IsSuccessStatusCode) { }
            catch (JsonException) { throw new NotificationDeliveryException("ADSALE_RESPONSE_INVALID", false); }
            task.ProviderRequestId = receipt.ValueKind == JsonValueKind.Object ? RequestId(receipt) : null;
        }
        if (!response.IsSuccessStatusCode)
            throw new NotificationDeliveryException(provider + "_HTTP_REJECTED", provider != "ADSALE" || (int)response.StatusCode is 408 or 502 or 503);
        if (provider != "ADSALE")
        {
            try { receipt = await response.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Options, ct); }
            catch (JsonException) { throw new NotificationDeliveryException(provider + "_RESPONSE_INVALID", true); }
        }
        if (receipt.ValueKind != JsonValueKind.Object) throw new NotificationDeliveryException(provider + "_RESPONSE_INVALID", false);
        if (provider == "ADSALE")
        {
            if (task.ProviderRequestId is null || !receipt.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
                throw new NotificationDeliveryException("ADSALE_RECEIPT_REJECTED", false);
        }
        else if (provider == "WECOM_ROBOT")
        {
            if (!receipt.TryGetProperty("errcode", out var code) || code.ValueKind != JsonValueKind.Number || !code.TryGetInt32(out var number) || number != 0)
                throw new NotificationDeliveryException("WECOM_NOT_ACCEPTED", true);
        }
        else if (!receipt.TryGetProperty("accepted", out var accepted) || accepted.ValueKind != JsonValueKind.True)
            throw new NotificationDeliveryException("GATEWAY_NOT_ACCEPTED", true);
    }

    /// <summary>
    /// 仅保留安全格式的 request_id，忽略响应中的其它供应商信息。
    /// </summary>
    private static string? RequestId(JsonElement value)
    {
        if (!value.TryGetProperty("request_id", out var id) && value.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            data.TryGetProperty("request_id", out id);
        var text = id.ValueKind == JsonValueKind.String ? id.GetString() : null;
        return text is not null && Regex.IsMatch(text, "\\A[A-Za-z0-9._:-]{1,128}\\z") ? text : null;
    }
}
