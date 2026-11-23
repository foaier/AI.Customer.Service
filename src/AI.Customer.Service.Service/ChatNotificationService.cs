using System.Text.RegularExpressions;
using AI.Customer.Service.Api.Contracts;
using AI.Customer.Service.Common;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;

namespace AI.Customer.Service.Service;

/// <summary>
/// 通知配置应用服务，组织 CAS 与令牌保留在一个事务中完成。
/// </summary>
public sealed class ChatNotificationService(IConfigurationRepository repository, IUnitOfWork transactions, ISecretCipher cipher)
{
    /// <summary>
    /// 读取不存在的配置仅返回禁用草稿，不初始化持久行。
    /// </summary>
    public async Task<ChatNotificationConfiguration> GetAsync(Actor actor, string org, CancellationToken ct) =>
        await repository.ChatNotificationAsync(actor.TenantId, org, ct) ?? new() { Id = "chat", TenantId = actor.TenantId, TopOrgId = org, Version = 0 };

    /// <summary>
    /// 保存完整群映射，旧页面未提交的新增设置沿用原值，密钥省略保留。
    /// </summary>
    public Task<ChatNotificationConfiguration> SaveAsync(Actor actor, string org, ChatNotificationCommand command, CancellationToken ct) =>
        transactions.ExecuteAsync(actor.TenantId, org, async () =>
        {
            var saved = await repository.ChatNotificationAsync(actor.TenantId, org, ct);
            if (command.Version < 0 || (saved?.Version ?? 0) != command.Version) throw BusinessException.Conflict("群通知配置版本已变化，请重新加载");
            if (command.ChatGroup is null || command.ChatGroup.Count > 7) throw BusinessException.Invalid("群配置无效");
            var value = new ChatNotificationConfiguration
            {
                Id = "chat",
                TenantId = actor.TenantId,
                TopOrgId = org,
                Version = command.Version + 1,
                Enabled = command.Enabled ?? saved?.Enabled ?? false,
                ActiveProvider = command.ActiveProvider ?? saved?.ActiveProvider ?? "WECOM_ROBOT",
                AccountCode = command.AccountCode ?? saved?.AccountCode ?? "vendor_31",
                BaseUrl = command.BaseUrl ?? saved?.BaseUrl ?? "https://hub.adsaleinfo.com/open-api",
                PublicBaseUrl = (command.PublicBaseUrl ?? saved?.PublicBaseUrl ?? "").Trim().TrimEnd('/'),
                ApiTokenCiphertext = saved?.ApiTokenCiphertext
            };
            if (value.ActiveProvider is not ("WECOM_ROBOT" or "ADSALE") || value.AccountCode != "vendor_31" || value.BaseUrl != "https://hub.adsaleinfo.com/open-api")
                throw BusinessException.Invalid("通知供应商或官方账号/API 地址无效");
            if (command.ClearApiToken && !string.IsNullOrEmpty(command.ApiToken)) throw BusinessException.Invalid("不能同时清除和设置通知令牌");
            if (command.ClearApiToken) value.ApiTokenCiphertext = null;
            if (command.ApiToken is not null)
            {
                if (string.IsNullOrWhiteSpace(command.ApiToken) || command.ApiToken == "***" || command.ApiToken.Length > 2048 || command.ApiToken.Any(char.IsControl))
                    throw BusinessException.Invalid("通知令牌无效；保留现有令牌时请省略 apiToken");
                value.ApiTokenCiphertext = cipher.Protect(command.ApiToken);
            }
            foreach (var (key, raw) in command.ChatGroup)
            {
                var category = key switch { "EXHIBITOR" => "SALES", "MEDIA" => "FINANCE", "OTHER" => "EMP", _ => key };
                var id = raw?.Trim() ?? "";
                if (category is not ("SALES" or "VISITOR" or "FINANCE" or "EMP")) throw BusinessException.Invalid("通知群类别无效");
                if (id.Length == 0) continue;
                var valid = value.ActiveProvider == "WECOM_ROBOT"
                    ? Regex.IsMatch(id, "\\A[A-Za-z0-9_-]{10,200}\\z")
                    : id.Length <= 128 && id != "***" && !id.Any(char.IsControl);
                if (!valid || (value.ChatGroup.TryGetValue(category, out var prior) && prior != id)) throw BusinessException.Invalid("群 ID 无效或同类别映射冲突");
                value.ChatGroup[category] = id;
            }
            if (value.PublicBaseUrl.Length > 0) ValidatePublicUrl(value.PublicBaseUrl);
            if (value.Enabled)
            {
                ValidatePublicUrl(value.PublicBaseUrl);
                if (value.ChatGroup.Count != 4) throw BusinessException.Invalid("启用通知前请填写四类群 ID");
                if (value.ActiveProvider == "ADSALE" && value.ApiTokenCiphertext is null) throw BusinessException.Invalid("启用雅式通知前请配置 API Token");
            }
            if (saved is null) { repository.AddChatNotification(value); return value; }
            saved.Enabled = value.Enabled;
            saved.ActiveProvider = value.ActiveProvider;
            saved.AccountCode = value.AccountCode;
            saved.BaseUrl = value.BaseUrl;
            saved.PublicBaseUrl = value.PublicBaseUrl;
            saved.ChatGroup = value.ChatGroup;
            saved.ApiTokenCiphertext = value.ApiTokenCiphertext;
            saved.Version = value.Version;
            return saved;
        }, ct);

    /// <summary>
    /// 链接只接受无账号、查询和片段的 HTTPS 站点根地址。
    /// </summary>
    public static void ValidatePublicUrl(string value)
    {
        if (value.Length > 256 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw BusinessException.Invalid("请填写有效 HTTPS 客服站点地址，不含账号、查询参数或片段");
    }

    /// <summary>
    /// 管理员可编辑的投影；主密钥、明文令牌及密文永不回显。
    /// </summary>
    public static object View(ChatNotificationConfiguration value) => new
    {
        value.Version,
        value.Enabled,
        value.ActiveProvider,
        value.AccountCode,
        value.BaseUrl,
        value.PublicBaseUrl,
        value.ChatGroup,
        apiTokenConfigured = value.ApiTokenCiphertext is not null
    };
}
