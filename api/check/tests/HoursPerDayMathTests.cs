using HrCore.Domain;
using HrCore.Features.Payroll;

namespace HrCore.Tests;

public sealed class HoursPerDayMathTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(8.5)]
    [InlineData(9)]
    [InlineData(24)]
    public void Valid_hours_are_accepted(decimal hours)
    {
        Assert.True(HoursPerDaySetting.TryValidate(hours, out var errors));
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(8.25)]
    [InlineData(8.1)]
    [InlineData(24.5)]
    [InlineData(25)]
    [InlineData(-1)]
    public void Invalid_hours_are_rejected(decimal hours)
    {
        Assert.False(HoursPerDaySetting.TryValidate(hours, out var errors));
        Assert.Contains("1 through 24", errors["hoursPerDay"][0]);
    }

    [Fact]
    public void Hourly_equivalent_at_8_matches_today()
    {
        Assert.Equal(100m, OvertimeMath.HourlyEquivalent(PayBasis.Hourly, 100m, 313m));
        Assert.Equal(100m, OvertimeMath.HourlyEquivalent(PayBasis.Daily, 800m, 313m));
        Assert.Equal(150m, OvertimeMath.HourlyEquivalent(PayBasis.Monthly, 31300m, 313m));
        Assert.Equal(100m, OvertimeMath.HourlyEquivalent(PayBasis.Daily, 800m, 313m, 8m));
    }

    [Fact]
    public void Hourly_equivalent_at_9_divides_by_nine()
    {
        Assert.Equal(100m, OvertimeMath.HourlyEquivalent(PayBasis.Hourly, 100m, 313m, 9m));
        Assert.Equal(800m / 9m, OvertimeMath.HourlyEquivalent(PayBasis.Daily, 800m, 313m, 9m));
        Assert.Equal(31300m * 12m / 313m / 9m, OvertimeMath.HourlyEquivalent(PayBasis.Monthly, 31300m, 313m, 9m));
    }

    [Fact]
    public void Leave_hours_at_8_and_9()
    {
        Assert.Equal(8m, HoursPerDaySetting.LeaveHours(1m, 8m));
        Assert.Equal(4m, HoursPerDaySetting.LeaveHours(0.5m, 8m));
        Assert.Equal(9m, HoursPerDaySetting.LeaveHours(1m, 9m));
        Assert.Equal(4.5m, HoursPerDaySetting.LeaveHours(0.5m, 9m));
        Assert.Equal(800m, HoursPerDaySetting.LeaveHours(1m, 8m) * 100m);
        Assert.Equal(900m, HoursPerDaySetting.LeaveHours(1m, 9m) * 100m);
    }
}
