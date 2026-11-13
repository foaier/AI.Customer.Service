using AI.Customer.Service.Api.Contracts;
using AI.Customer.Service.Common;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;
using AI.Customer.Service.Service;
using AI.Customer.Service.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace AI.Customer.Service.Web.Controllers;

/// <summary>组织分组、正式日历及外部候选目录 HTTP Adapter。</summary>
/// <param name="request">组织设置权限。</param>
/// <param name="configuration">本地配置应用端口。</param>
/// <param name="accounts">外部账号只读目录。</param>
/// <param name="model">语义分类协议端口。</param>
/// <param name="access">可信助手范围。</param>
public sealed class ConfigurationController(RequestAccess request, ConfigurationService configuration,
    IAccountDirectory accounts, IChatModel model, AccessService access) : CustomerServiceControllerBase(request)
{
    /// <summary>首次只读返回四类稳定默认组，不自动关联成员。</summary>
    [HttpGet("configuration")]
    public async Task<object> Get(string topOrgId, CancellationToken ct)
    {
        await RequestAccess.ManagerAsync(topOrgId, ct);
        return Success(RoutingView(await configuration.GetAsync(Actor, topOrgId, ct)));
    }

    /// <summary>核验外部成员后整体 CAS 保存。</summary>
    [HttpPut("configuration")]
    public async Task<object> Save(string topOrgId, RoutingCommand command, CancellationToken ct)
    {
        await RequestAccess.ManagerAsync(topOrgId, ct);
        return Success(RoutingView(await configuration.SaveAsync(Actor, topOrgId, command, ct)));
    }

    /// <summary>正式只读外部人员候选组，不返回手机号或邮箱。</summary>
    [HttpGet("configuration/directory/groups")]
    public async Task<object> Groups(string topOrgId, CancellationToken ct, string? keyword = null, string? afterCursor = null, int limit = 100)
    {
        await RequestAccess.ManagerAsync(topOrgId, ct);
        var page = await accounts.GroupsAsync(Actor.TenantId, topOrgId, keyword, afterCursor, limit, ct);
        return Success(new { page.Status, groups = page.Items.Select(x => new { id = x.GroupId, groupId = x.GroupId, name = x.Name }), page.NextCursor, page.HasMore });
    }

    /// <summary>外部组归属核验后返回最小账号候选。</summary>
    [HttpGet("configuration/directory/accounts")]
    public async Task<object> Accounts(string topOrgId, string groupId, CancellationToken ct, string? keyword = null, string? afterCursor = null, int limit = 50)
    {
        await RequestAccess.ManagerAsync(topOrgId, ct);
        var page = await accounts.AccountsAsync(Actor.TenantId, topOrgId, groupId, keyword, afterCursor, limit, ct);
        return Success(new { page.Status, candidates = page.Items, page.NextCursor, page.HasMore });
    }

    /// <summary>预览主题与已保存映射，不创建人工申请。</summary>
    [HttpPost("configuration/preview")]
    public async Task<object> Preview(string topOrgId, RoutingPreview command, CancellationToken ct)
    {
        await RequestAccess.ManagerAsync(topOrgId, ct);
        var assistant = access.RequireAssistant(Actor, topOrgId, command.AssistantConfigId);
        var scope = new Scope(Actor.TenantId, topOrgId, "preview", assistant.Id, assistant.BventId);
        var classification = await model.ClassifyAsync(scope, Guard.Text(command.Context, "上下文", 16000), ct);
        var config = await configuration.GetAsync(Actor, topOrgId, ct);
        var route = config.Routes.SingleOrDefault(x => x.AssistantConfigId == assistant.Id);
        return Success(new { classification, groupId = route?.Groups.GetValueOrDefault(classification.Category), configVersion = config.Version });
    }

    /// <summary>读取持久组织日历，无配置则使用部署缺省。</summary>
    [HttpGet("configuration/calendar")]
    public async Task<object> Calendar(string topOrgId, CancellationToken ct)
    {
        await RequestAccess.ManagerAsync(topOrgId, ct);
        return Success(CalendarView(await configuration.CalendarAsync(Actor.TenantId, topOrgId, ct)));
    }

    /// <summary>保存正式日历，不影响已冻结等待周期。</summary>
    [HttpPut("configuration/calendar")]
    public async Task<object> SaveCalendar(string topOrgId, CalendarCommand command, CancellationToken ct)
    {
        await RequestAccess.ManagerAsync(topOrgId, ct);
        return Success(CalendarView(await configuration.SaveCalendarAsync(Actor, topOrgId, command, ct)));
    }

    /// <summary>仅预览指定时刻，不改变时钟或持久配置。</summary>
    [HttpPost("configuration/calendar/preview")]
    public async Task<object> PreviewCalendar(string topOrgId, CalendarPreview command, CancellationToken ct)
    {
        await RequestAccess.ManagerAsync(topOrgId, ct);
        return Success(ConfigurationService.ToCalendar(command.Calendar).Evaluate(command.At.UtcDateTime));
    }

    /// <summary>配置仅返回业务字段，不包含基础设施键。</summary>
    private static object RoutingView(RoutingConfiguration value) => new { value.Version, value.Groups, value.Routes };

    /// <summary>日历不返回数据库主键和内部归属副本。</summary>
    private static object CalendarView(CalendarPolicy value) => new { value.Version, value.CalendarVersion, value.Zone, value.Opens, value.Closes, value.Overrides };
}
