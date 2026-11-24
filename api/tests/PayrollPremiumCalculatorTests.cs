using HrCore.Features.Payroll;
using Xunit;

namespace HrCore.Tests;

public class PayrollPremiumCalculatorTests
{
    private static readonly DateOnly Holiday = new(2026, 1, 1);

    private static readonly PayrollPremiumCalculator.Settings HolidaySettings =
        PayrollPremiumCalculator.Settings.FromBranch(new Branch
        {
            PayrollHolidayPremiumEnabled = true,
            PayrollRegularHolidayPercent = 200m,
            PayrollRestDayPremiumEnabled = false,
            PayrollNightDiffEnabled = false,
            ShiftEnd = new TimeOnly(17, 0),
            OtGraceMinutes = 0
        });

    private static (DateTime InUtc, DateTime OutUtc) Punch(DateOnly day, int inHour, int outHour)
    {
        var inUtc = ManilaTime.StartOfDayUtc(day).AddHours(inHour);
        var outUtc = ManilaTime.StartOfDayUtc(day).AddHours(outHour);
        return (inUtc, outUtc);
    }

    [Fact]
    public void Holiday_premium_uses_regular_hours_minus_one_hour_break()
    {
        // 08:00–17:00 is 9 punched hours; day premium is 8 after the unpaid meal break.
        var punches = new[] { Punch(Holiday, 8, 17) };
        var holidays = new Dictionary<DateOnly, HolidayKind> { [Holiday] = HolidayKind.Regular };

        var result = PayrollPremiumCalculator.Compute(
            PayBasis.Hourly,
            100m,
            punches,
            holidays,
            new HashSet<DayOfWeek>(),
            HolidaySettings);

        Assert.Equal(8m, result.HolidayHours);
        // 8h × 100 × (200-100)/100 = 800
        Assert.Equal(800m, result.PremiumPay);
    }

    [Fact]
    public void Holiday_overtime_hours_are_not_also_paid_the_day_premium()
    {
        // 08:00–19:00 with shift end 17:00: 2h claimed OT. Day premium stays on 8 regular hours
        // (9 regular before break − 1h meal), not on the OT hours.
        var punches = new[] { Punch(Holiday, 8, 19) };
        var holidays = new Dictionary<DateOnly, HolidayKind> { [Holiday] = HolidayKind.Regular };

        var result = PayrollPremiumCalculator.Compute(
            PayBasis.Hourly,
            100m,
            punches,
            holidays,
            new HashSet<DayOfWeek>(),
            HolidaySettings);

        Assert.Equal(8m, result.HolidayHours);
        Assert.Equal(800m, result.PremiumPay);
    }

    [Fact]
    public void Rest_day_premium_also_excludes_break_and_overtime()
    {
        var day = new DateOnly(2026, 1, 4); // Sunday
        var settings = PayrollPremiumCalculator.Settings.FromBranch(new Branch
        {
            PayrollHolidayPremiumEnabled = false,
            PayrollRestDayPremiumEnabled = true,
            PayrollRestDayPercent = 130m,
            PayrollNightDiffEnabled = false,
            ShiftEnd = new TimeOnly(17, 0),
            OtGraceMinutes = 0
        });
        var punches = new[] { Punch(day, 8, 19) };

        var result = PayrollPremiumCalculator.Compute(
            PayBasis.Daily,
            800m,
            punches,
            new Dictionary<DateOnly, HolidayKind>(),
            new HashSet<DayOfWeek> { DayOfWeek.Sunday },
            settings);

        Assert.Equal(8m, result.RestDayHours);
        // Daily rate 800 → hourly 100; 8h × 100 × 30% = 240
        Assert.Equal(240m, result.PremiumPay);
    }
}
