using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AI.Customer.Service.Common;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;
using AI.Customer.Service.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace AI.Customer.Service.Infrastructure.Adapters;

/// <summary>
/// 通过用户中心四个只读接口查询权威账号和组织关系。
/// </summary>
/// <param name="http">
/// 有超时且禁重定向的客户端。
/// </param>
/// <param name="options">
/// 受控根地址和服务间认证。
/// </param>
public sealed class UserCenterAdapter(HttpClient http, IOptions<UserCenterOptions> options) : IAccountDirectory
{
    /// <summary>
    /// 实时核验用户中心账号、租户和目标组织启用关系，并返回权威展示名称。
    /// </summary>
    public async Task<Member> RequireAsync(string account, CancellationToken ct)
    {
        var info = await SendAsync(HttpMethod.Post, "/api/AccountUser/v1/Info", new { accountId = account }, ct);
        var item = info.GetProperty("returnObj");
        if (item.GetProperty("accountId").GetString() != account)
        {
            throw BusinessException.Forbidden("用户中心账号核验失败");
        }
        var name = item.TryGetProperty("fullName", out var display) ? display.GetString() : null;
        return new(account, string.IsNullOrWhiteSpace(name) ? account : name[..Math.Min(name.Length, 128)]);
    }

    /// <summary>
    /// 按绑定租户、组织、关键词和页大小的游标查询人员组，拒绝跨组织记录。
    /// </summary>
    public async Task<DirectoryPage<DirectoryGroup>> GroupsAsync(string? keyword, string? cursor, int limit, CancellationToken ct)
    {
        var scope = CursorScope("", keyword, limit);
        var page = Decode(cursor, scope);
        var json = await SendAsync(HttpMethod.Post, "/api/accountGroup/v1/list",
            new { Search = keyword ?? "", PageIndex = page, PageSize = limit, Skip = (page - 1) * limit }, ct);
        var items = json.GetProperty("returnObj").EnumerateArray().Select(x =>
        {
            return new DirectoryGroup(x.GetProperty("id").GetString()!, x.GetProperty("name").GetString()!);
        }).ToArray();
        return Page(json, page, limit, scope, items);
    }

    /// <summary>
    /// 先确认人员组属于目标组织，再按绑定范围的游标分页读取最小账号候选。
    /// </summary>
    public async Task<DirectoryPage<Member>> AccountsAsync(string group, string? keyword, string? cursor, int limit, CancellationToken ct)
    {
        var verified = false;
        string? groupCursor = null;
        for (var scan = 0; scan < 100; scan++)
        {
            var groups = await GroupsAsync(null, groupCursor, 1000, ct);
            if (groups.Items.Any(x => x.GroupId == group))
            {
                verified = true;
                break;
            }
            if (!groups.HasMore)
            {
                break;
            }
            groupCursor = groups.NextCursor;
        }
        if (!verified)
        {
            throw BusinessException.Forbidden("人员候选组不属于当前组织");
        }
        var scope = CursorScope(group, keyword, limit);
        var page = Decode(cursor, scope);
        var json = await SendAsync(HttpMethod.Post, "/api/accountGroup/v1/accountDetails",
            new { AccountGroupId = group, Search = keyword ?? "", PageIndex = page, PageSize = limit, Skip = (page - 1) * limit }, ct);
        var items = json.GetProperty("returnObj").EnumerateArray().Select(x => new Member(
            x.GetProperty("id").GetString()!, x.TryGetProperty("fullName", out var name) && !string.IsNullOrWhiteSpace(name.GetString())
                ? name.GetString()! : x.GetProperty("id").GetString()!)).ToArray();
        return Page(json, page, limit, scope, items);
    }

    /// <summary>
    /// 严格验证上游成功信封，协议失败不伪装为空账号或空目录。
    /// </summary>
    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (!Uri.TryCreate(options.Value.BaseUrl, UriKind.Absolute, out var root) || root.Scheme is not "http" and not "https")
        {
            throw BusinessException.Unavailable("尚未配置用户中心地址");
        }
        using var request = new HttpRequestMessage(method, new Uri(root, path));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: new JsonSerializerOptions { PropertyNamingPolicy = null });
        }
        if (!string.IsNullOrWhiteSpace(options.Value.Authorization))
        {
            request.Headers.TryAddWithoutValidation("Authorization", options.Value.Authorization);
        }
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw BusinessException.Unavailable("用户中心请求失败");
            }
            var result = await response.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Options, ct);
            if (!result.TryGetProperty("isSuccess", out var success) || success.ValueKind != JsonValueKind.True ||
                !result.TryGetProperty("returnObj", out var item) || item.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                throw BusinessException.Unavailable("用户中心业务响应无效");
            }
            return result;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or KeyNotFoundException || error is OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw BusinessException.Unavailable("用户中心暂不可用");
        }
    }

    /// <summary>
    /// 检查分页元数据并绑定下一页游标，拒绝不一致的页大小。
    /// </summary>
    private static DirectoryPage<T> Page<T>(JsonElement json, int page, int limit, string scope, T[] items)
    {
        var pager = json.GetProperty("pager");
        var total = pager.GetProperty("totalPage").GetInt32();
        if (pager.GetProperty("pageIndex").GetInt32() != page || pager.GetProperty("pageSize").GetInt32() != limit ||
            items.Length > limit || total < 0 || page > Math.Max(1, total))
        {
            throw BusinessException.Unavailable("用户中心目录分页无效");
        }
        var more = page < total;
        return new("READY", items, more ? Convert.ToBase64String(Encoding.UTF8.GetBytes((page + 1) + "|" + scope)) : null, more);
    }

    /// <summary>
    /// 游标绑定租户、组织、外部组、关键词和页大小。
    /// </summary>
    private static string CursorScope(string group, string? keyword, int limit)
    {
        if (limit is < 1 or > 1000)
        {
            throw BusinessException.Invalid("目录页大小无效");
        }
        return Guard.Fingerprint(string.Join("\n", group, keyword ?? "", limit.ToString()));
    }

    /// <summary>
    /// 损坏或跨范围游标必须报错，不能静默重置第一页。
    /// </summary>
    private static int Decode(string? cursor, string scope)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return 1;
        }
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            if (parts.Length == 2 && parts[1] == scope && int.TryParse(parts[0], out var page) && page is >= 2 and <= 10000)
            {
                return page;
            }
        }
        catch (FormatException)
        {
            // 外部输入格式错误只返回业务参数错误，不输出游标正文。
        }
        throw BusinessException.Invalid("目录分页游标无效");
    }
}
