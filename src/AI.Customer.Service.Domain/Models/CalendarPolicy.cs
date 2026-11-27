using AI.Customer.Service.Common;

namespace AI.Customer.Service.Domain.Models;

/// <summary>
/// 组织级营业日历，只影响新等待周期，既有 UTC 期限保持冻结
/// </summary>
public sealed class CalendarPolicy : Aggregate
{
    /// <summary>
    /// 业务规则版本，改变时区、时间或日期覆盖时必须升级
    /// </summary>
    public string CalendarVersion { get; set; } = "cn-office-v1";

    /// <summary>
    /// IANA 时区，默认中国标准时间
    /// </summary>
    public string Zone { get; set; } = "Asia/Shanghai";

    /// <summary>
    /// 旧版单时段开门时间；多时段规则时保存首个时段的开始时间。
    /// </summary>
    public string Opens { get; set; } = "10:00";

    /// <summary>
    /// 旧版单时段闭门时间；多时段规则时保存末个时段的结束时间。
    /// </summary>
    public string Closes { get; set; } = "17:00";

    /// <summary>
    /// 每周营业日，ISO 星期数字 1 至 7；旧数据缺省为周一至周五。
    /// </summary>
    public List<int>? WorkingDays { get; set; } = [1, 2, 3, 4, 5];

    /// <summary>
    /// 每周共用的当地营业时段；旧数据缺省时使用 Opens 和 Closes。
    /// </summary>
    public List<CalendarTimeSlot>? Slots { get; set; } = [new("10:00", "17:00")];

    /// <summary>
    /// ISO 日期到开闭班覆盖，最多 1000 项，优先于工作日规则
    /// </summary>
    public Dictionary<string, bool> Overrides { get; set; } = [];

    /// <summary>
    /// 严格校验同日营业区间、版本、时区及日期覆盖
    /// </summary>
    public void Validate()
    {
        Guard.Text(CalendarVersion, "日历版本", 64);
        var workingDays = EffectiveWorkingDays();
        if (workingDays.Count is < 1 or > 7 || workingDays.Any(day => day is < 1 or > 7) ||
            workingDays.Distinct().Count() != workingDays.Count)
        {
            throw BusinessException.Invalid("每周营业日必须为不重复的 1 至 7 数字且至少选择一日");
        }
        _ = EffectiveSlots();
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(Zone);
        }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw BusinessException.Invalid("营业时区无效");
        }
        if (Overrides is null || Overrides.Count > 1000 || Overrides.Keys.Any(d => !DateOnly.TryParseExact(d, "yyyy-MM-dd", out _)))
        {
            throw BusinessException.Invalid("日历日期覆盖无效或超过 1000 项");
        }
    }

    /// <summary>
    /// 根据实际 UTC 时刻计算当前营业和下次营业，不由模型推算
    /// </summary>
    public CalendarWindow Evaluate(DateTime utc)
    {
        Validate();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(Zone);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        var day = DateOnly.FromDateTime(local);
        var now = TimeOnly.FromDateTime(local);
        var slots = EffectiveSlots();
        if (IsWorking(day))
        {
            for (var index = 0; index < slots.Count; index++)
            {
                var slot = slots[index];
                var opens = ParseTime(slot.Opens);
                var closes = ParseTime(slot.Closes);
                if (now >= opens && now < closes)
                {
                    while (index + 1 < slots.Count && ParseTime(slots[index + 1].Opens) == closes)
                    {
                        closes = ParseTime(slots[++index].Closes);
                    }
                    return new(true, ToUtc(day, opens, zone), ToUtc(day, closes, zone), CalendarVersion);
                }
                if (now < opens)
                {
                    return new(false, ToUtc(day, opens, zone), DateTime.SpecifyKind(utc, DateTimeKind.Utc), CalendarVersion);
                }
            }
        }
        for (var offset = 1; offset <= 1096; offset++)
        {
            var next = day.AddDays(offset);
            if (IsWorking(next))
            {
                return new(false, ToUtc(next, ParseTime(slots[0].Opens), zone), DateTime.SpecifyKind(utc, DateTimeKind.Utc), CalendarVersion);
            }
        }
        throw BusinessException.Invalid("日历未来三年没有营业日");
    }

    /// <summary>
    /// 日期覆盖优先，未覆盖时根据每周营业日判断。
    /// </summary>
    private bool IsWorking(DateOnly date) =>
        Overrides.TryGetValue(date.ToString("yyyy-MM-dd"), out var value)
            ? value
            : EffectiveWorkingDays().Contains((int)date.DayOfWeek is 0 ? 7 : (int)date.DayOfWeek);

    /// <summary>
    /// 获取已排序且验证不重叠的时段；旧记录使用原单时段字段。
    /// </summary>
    public IReadOnlyList<CalendarTimeSlot> EffectiveSlots()
    {
        var slots = Slots ?? [new CalendarTimeSlot(Opens, Closes)];
        if (slots.Count is < 1 or > 8)
        {
            throw BusinessException.Invalid("营业时段数量必须为 1 至 8 个");
        }
        var normalized = new List<(CalendarTimeSlot Slot, TimeOnly Opens, TimeOnly Closes)>(slots.Count);
        foreach (var slot in slots)
        {
            if (slot is null || !TimeOnly.TryParseExact(slot.Opens, ["HH:mm", "HH:mm:ss"], out var opens) ||
                !TimeOnly.TryParseExact(slot.Closes, ["HH:mm", "HH:mm:ss"], out var closes) || opens >= closes)
            {
                throw BusinessException.Invalid("每个营业时段必须为同日有效区间且开始早于结束");
            }
            normalized.Add((slot, opens, closes));
        }
        normalized.Sort((left, right) => left.Opens.CompareTo(right.Opens));
        for (var index = 1; index < normalized.Count; index++)
        {
            if (normalized[index].Opens < normalized[index - 1].Closes)
            {
                throw BusinessException.Invalid("营业时段不能重叠");
            }
        }
        return normalized.Select(item => item.Slot).ToArray();
    }

    /// <summary>
    /// 获取已排序的工作日；旧记录缺失时沿用周一至周五。
    /// </summary>
    public IReadOnlyList<int> EffectiveWorkingDays() => (WorkingDays ?? [1, 2, 3, 4, 5]).Order().ToArray();

    /// <summary>
    /// 把指定营业边界按日历时区转换为 UTC。
    /// </summary>
    private static DateTime ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(time), zone);

    /// <summary>
    /// 解析领域校验已接受的当地时分格式。
    /// </summary>
    private static TimeOnly ParseTime(string value) => TimeOnly.ParseExact(value, ["HH:mm", "HH:mm:ss"]);
}

/// <summary>
/// 一周重复使用的当地营业时段，开始时刻包含、结束时刻不包含。
/// </summary>
/// <param name="Opens">
/// 时段开始时间，格式为 HH:mm 或 HH:mm:ss。
/// </param>
/// <param name="Closes">
/// 时段结束时间，格式为 HH:mm 或 HH:mm:ss，必须晚于开始时间。
/// </param>
public sealed record CalendarTimeSlot(string Opens, string Closes);

/// <summary>
/// 一份不可变日历快照计算的营业窗口。
/// </summary>
/// <param name="Open"> 当前是否营业。 </param>
/// <param name="NextOpensAt"> 下次开门 UTC 时间。 </param>
/// <param name="ClosesAt"> 当天闭门 UTC 时间。 </param>
/// <param name="Version"> 业务日历版本。 </param>
public sealed record CalendarWindow(bool Open, DateTime NextOpensAt, DateTime ClosesAt, string Version);
