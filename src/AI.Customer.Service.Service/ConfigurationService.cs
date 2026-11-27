using System.Text.Json;
using AI.Customer.Service.Api.Contracts;
using AI.Customer.Service.Common;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;
using AI.Customer.Service.Service.Configuration;
using Microsoft.Extensions.Options;

namespace AI.Customer.Service.Service;

/// <summary>
/// 本地业务分组及日历的配置编排，不维护用户中心账号主数据。
/// </summary>
/// <param name="repository"> 配置 owner。 </param>
/// <param name="transactions"> 共享本地事务。 </param>
/// <param name="accounts"> 事务外核验账号。 </param>
/// <param name="access"> 统一准入。 </param>
/// <param name="options"> 部署默认规则。 </param>
public sealed class ConfigurationService(IConfigurationRepository repository, IUnitOfWork transactions,
    IAccountDirectory accounts, AccessService access, IOptions<CustomerServiceOptions> options)
{
    /// <summary>
    /// 未保存只读返回四个稳定组，不创建成员或授予权限。
    /// </summary>
    public async Task<RoutingConfiguration> GetAsync(Actor actor, CancellationToken ct) =>
        await repository.RoutingAsync(ct) ?? Defaults();

    /// <summary>
    /// 核验全部外部账号后，在短事务内整体 CAS 保存组及路由。
    /// </summary>
    public async Task<RoutingConfiguration> SaveAsync(Actor actor, RoutingCommand command, CancellationToken ct)
    {
        ValidateRouting(actor, command);
        var verified = new List<ReceptionGroup>();
        foreach (var group in command.Groups)
        {
            var members = new List<Member>();
            foreach (var member in group.Members)
            {
                members.Add(await accounts.RequireAsync(Guard.Text(member.AccountId, "账号"), ct));
            }
            verified.Add(group with { Members = members });
        }
        return await transactions.ExecuteAsync(async () =>
        {
            var value = await repository.RoutingAsync(ct);
            if ((value?.Version ?? 0) != command.Version)
            {
                throw BusinessException.Conflict("分组路由配置版本已变化");
            }
            if (value is null)
            {
                value = new RoutingConfiguration { Id = "routing", Version = 0 };
                repository.AddRouting(value);
            }
            value.Groups = verified;
            value.Routes = command.Routes;
            value.Version++;
            return value;
        }, ct);
    }

    /// <summary>
    /// 读取正式日历，不存在时取得部署默认的独立副本。
    /// </summary>
    public async Task<CalendarPolicy> CalendarAsync(CancellationToken ct)
    {
        var value = await repository.CalendarAsync(ct);
        if (value is not null)
        {
            return value;
        }
        var fallback = JsonSerializer.Deserialize<CalendarPolicy>(JsonSerializer.Serialize(options.Value.Calendar, JsonDefaults.Options), JsonDefaults.Options)!;
        fallback.Id = "calendar";
        fallback.Version = 0;
        return fallback;
    }

    /// <summary>
    /// 整体保存组织日历，改变业务规则时强制更换业务版本。
    /// </summary>
    public Task<CalendarPolicy> SaveCalendarAsync(Actor actor, CalendarCommand command, CancellationToken ct)
    {
        var draft = ToCalendar(command);
        draft.Validate();
        return transactions.ExecuteAsync(async () =>
        {
            var current = await repository.CalendarAsync(ct);
            if ((current?.Version ?? 0) != command.Version)
            {
                throw BusinessException.Conflict("营业日历版本已变化");
            }
            if (current is not null && current.CalendarVersion == draft.CalendarVersion &&
                (current.Zone != draft.Zone || current.Opens != draft.Opens || current.Closes != draft.Closes ||
                 !current.EffectiveWorkingDays().SequenceEqual(draft.EffectiveWorkingDays()) ||
                 !current.EffectiveSlots().SequenceEqual(draft.EffectiveSlots()) ||
                 !current.Overrides.OrderBy(x => x.Key).SequenceEqual(draft.Overrides.OrderBy(x => x.Key))))
            {
                throw BusinessException.Invalid("更改营业规则必须升级 calendarVersion");
            }
            if (current is null)
            {
                current = new CalendarPolicy { Id = "calendar", Version = 0 };
                repository.AddCalendar(current);
            }
            current.CalendarVersion = draft.CalendarVersion;
            current.Zone = draft.Zone;
            current.WorkingDays = draft.EffectiveWorkingDays().ToList();
            current.Slots = draft.EffectiveSlots().ToList();
            current.Opens = current.Slots[0].Opens;
            current.Closes = current.Slots[^1].Closes;
            current.Overrides = draft.Overrides;
            current.Version++;
            return current;
        }, ct);
    }

    /// <summary>
    /// 把严格 HTTP 草稿映射到独立日历规则，不读取身份字段。
    /// </summary>
    public static CalendarPolicy ToCalendar(CalendarCommand command)
    {
        var suppliedSlots = command.Slots;
        var opens = suppliedSlots is { Count: > 0 } ? suppliedSlots[0].Opens : command.Opens ?? "";
        var closes = suppliedSlots is { Count: > 0 } ? suppliedSlots[^1].Closes : command.Closes ?? "";
        var calendar = new CalendarPolicy
        {
            CalendarVersion = command.CalendarVersion,
            Zone = command.Zone,
            Opens = opens,
            Closes = closes,
            Overrides = command.Overrides ?? [],
            WorkingDays = command.WorkingDays,
            Slots = suppliedSlots?.Select(slot => new CalendarTimeSlot(slot.Opens, slot.Closes)).ToList()
        };
        calendar.Validate();
        calendar.WorkingDays = calendar.EffectiveWorkingDays().ToList();
        calendar.Slots = calendar.EffectiveSlots().ToList();
        calendar.Opens = calendar.Slots[0].Opens;
        calendar.Closes = calendar.Slots[^1].Closes;
        return calendar;
    }

    /// <summary>
    /// 创建同组织稳定默认组，读操作无数据库副作用。
    /// </summary>
    public static RoutingConfiguration Defaults() =>
        new()
        {
            Id = "routing",
            Version = 0,
            Groups = Enum.GetValues<Category>().Select(c => new ReceptionGroup(
                "group_" + Guard.Fingerprint(c == Category.FINANCE ? "MEDIA" : c.ToString())[..24], c,
                c switch { Category.EXHIBITOR => "展商组", Category.VISITOR => "客服组", Category.FINANCE => "财务客服", _ => "其他组" }, [])).ToList()
        };

    /// <summary>
    /// 完整检查四类、成员唯一性、助手归属及映射，不允许外部组越权成为本地组。
    /// </summary>
    private void ValidateRouting(Actor actor, RoutingCommand command)
    {
        if (command.Version < 0 || command.Groups is null || command.Routes is null || command.Groups.Count != 4 ||
            command.Groups.Any(g => g is null || g.Members is null) ||
            command.Groups.Select(g => g.Category).Distinct().Count() != 4 ||
            command.Groups.Select(g => g.Id).Distinct().Count() != 4 || command.Routes.Count > 1000)
        {
            throw BusinessException.Invalid("必须提供四类完整接待组及有效路由");
        }
        foreach (var group in command.Groups)
        {
            Guard.Text(group.Id, "业务组", 64);
            Guard.Text(group.Name, "组名称", 128);
            if (!Enum.IsDefined(group.Category) || group.Members.Count > 1000 ||
                group.Members.Select(m => m.AccountId).Distinct().Count() != group.Members.Count)
            {
                throw BusinessException.Invalid("组成员重复或超出范围");
            }
        }
        if (command.Routes.Select(r => r.AssistantConfigId).Distinct().Count() != command.Routes.Count)
        {
            throw BusinessException.Invalid("助手路由重复");
        }
        foreach (var route in command.Routes)
        {
            access.RequireAssistant(actor, route.AssistantConfigId);
            if (route.Groups is null || route.Groups.Count != 4 || Enum.GetValues<Category>().Any(c =>
                !route.Groups.TryGetValue(c, out var group) || !command.Groups.Any(g => g.Id == group && g.Category == c)))
            {
                throw BusinessException.Invalid("助手必须映射四类同类别业务组");
            }
        }
    }
}
