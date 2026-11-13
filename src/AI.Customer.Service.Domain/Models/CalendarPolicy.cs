using AI.Customer.Service.Common;

namespace AI.Customer.Service.Domain.Models;

/// <summary>组织级营业日历，只影响新等待周期，既有 UTC 期限保持冻结。</summary>
public sealed class CalendarPolicy : Aggregate
{
    /// <summary>业务规则版本，改变时区、时间或日期覆盖时必须升级。</summary>
    public string CalendarVersion { get; set; } = "cn-office-v1";

    /// <summary>IANA 时区，默认中国标准时间。</summary>
    public string Zone { get; set; } = "Asia/Shanghai";

    /// <summary>同日营业开始，含边界，默认 10:00。</summary>
    public string Opens { get; set; } = "10:00";

    /// <summary>同日营业结束，不含边界，默认 17:00。</summary>
    public string Closes { get; set; } = "17:00";

    /// <summary>ISO 日期到开闭班覆盖，最多 1000 项，优先于工作日规则。</summary>
    public Dictionary<string, bool> Overrides { get; set; } = [];

    /// <summary>严格校验同日营业区间、版本、时区及日期覆盖。</summary>
    public void Validate()
    {
        Guard.Text(CalendarVersion, "日历版本", 64);
        if (!TimeOnly.TryParseExact(Opens, ["HH:mm", "HH:mm:ss"], out var opens) ||
            !TimeOnly.TryParseExact(Closes, ["HH:mm", "HH:mm:ss"], out var closes) || opens >= closes)
        {
            throw BusinessException.Invalid("营业时间必须为同日有效区间，开始早于结束");
        }
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

    /// <summary>根据实际 UTC 时刻计算当前营业和下次营业，不由模型推算。</summary>
    public CalendarWindow Evaluate(DateTime utc)
    {
        Validate();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(Zone);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        var day = DateOnly.FromDateTime(local);
        var opens = TimeOnly.Parse(Opens);
        var closes = TimeOnly.Parse(Closes);
        var open = IsWorking(day) && TimeOnly.FromDateTime(local) >= opens && TimeOnly.FromDateTime(local) < closes;
        var closeAt = TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(closes), zone);
        for (var offset = 0; offset <= 1096; offset++)
        {
            var next = day.AddDays(offset);
            if (IsWorking(next) && (offset > 0 || TimeOnly.FromDateTime(local) < opens))
            {
                return new(open, TimeZoneInfo.ConvertTimeToUtc(next.ToDateTime(opens), zone), closeAt, CalendarVersion);
            }
        }
        throw BusinessException.Invalid("日历未来三年没有营业日");
    }

    /// <summary>日期覆盖优先，未覆盖时仅周一至周五营业。</summary>
    private bool IsWorking(DateOnly date) =>
        Overrides.TryGetValue(date.ToString("yyyy-MM-dd"), out var value)
            ? value
            : date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
}

/// <summary>一份不可变日历快照计算的营业窗口。</summary>
/// <param name="Open">当前是否营业。</param>
/// <param name="NextOpensAt">下次开门 UTC 时间。</param>
/// <param name="ClosesAt">当天闭门 UTC 时间。</param>
/// <param name="Version">业务日历版本。</param>
public sealed record CalendarWindow(bool Open, DateTime NextOpensAt, DateTime ClosesAt, string Version);
