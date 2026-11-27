using AI.Customer.Service.Common;
using AI.Customer.Service.Domain.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AI.Customer.Service.Tests;

/// <summary>
/// 营业日历的多时段、工作日和相邻时段规则回归。
/// </summary>
[TestClass]
public sealed class CalendarPolicyTests
{
    /// <summary>
    /// 午间闭店返回当日下一时段，自定义工作日控制后续开门日期。
    /// </summary>
    [TestMethod]
    public void EvaluateUsesConfiguredWeekdaysAndMultipleSlots()
    {
        var policy = new CalendarPolicy
        {
            Zone = "Asia/Shanghai",
            WorkingDays = [1],
            Slots = [new("09:00", "12:00"), new("13:00", "17:00")]
        };

        var lunchBreak = policy.Evaluate(new DateTime(2026, 11, 23, 4, 30, 0, DateTimeKind.Utc));
        Assert.IsFalse(lunchBreak.Open);
        Assert.AreEqual(new DateTime(2026, 11, 23, 5, 0, 0, DateTimeKind.Utc), lunchBreak.NextOpensAt);
        Assert.AreEqual(new DateTime(2026, 11, 23, 4, 30, 0, DateTimeKind.Utc), lunchBreak.ClosesAt);

        var nonWorkingDay = policy.Evaluate(new DateTime(2026, 11, 24, 3, 0, 0, DateTimeKind.Utc));
        Assert.IsFalse(nonWorkingDay.Open);
        Assert.AreEqual(new DateTime(2026, 11, 30, 1, 0, 0, DateTimeKind.Utc), nonWorkingDay.NextOpensAt);
    }

    /// <summary>
    /// 相邻时段之间没有实际停业间隔时，当前营业窗口延伸到末段闭店。
    /// </summary>
    [TestMethod]
    public void EvaluateMergesAdjacentOpenSlots()
    {
        var policy = new CalendarPolicy
        {
            Zone = "Asia/Shanghai",
            Slots = [new("09:00", "12:00"), new("12:00", "17:00")]
        };

        var window = policy.Evaluate(new DateTime(2026, 11, 23, 2, 0, 0, DateTimeKind.Utc));

        Assert.IsTrue(window.Open);
        Assert.AreEqual(new DateTime(2026, 11, 23, 9, 0, 0, DateTimeKind.Utc), window.ClosesAt);
    }

    /// <summary>
    /// 重叠时段及空工作周都作为无效组织营业策略拒绝。
    /// </summary>
    [TestMethod]
    public void ValidateRejectsOverlappingSlotsAndEmptyWeekdays()
    {
        var overlapping = new CalendarPolicy { Slots = [new("09:00", "13:00"), new("12:00", "17:00")] };
        var noWeekdays = new CalendarPolicy { WorkingDays = [] };

        Assert.ThrowsExactly<BusinessException>(overlapping.Validate);
        Assert.ThrowsExactly<BusinessException>(noWeekdays.Validate);
    }
}
