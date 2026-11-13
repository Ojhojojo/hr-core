namespace HrCore.Features.Payroll;

/// <summary>
/// Hours that make a working day. Default 8, the same day used today for leave and
/// <see cref="OvertimeMath.HourlyEquivalent"/>.
/// </summary>
public static class HoursPerDaySetting
{
    public const decimal Default = Branch.DefaultHoursPerDay;
    public const decimal Min = 1m;
    public const decimal Max = 24m;
    public const decimal Step = 0.5m;

    public static decimal Resolve(decimal? hoursPerDay) =>
        hoursPerDay is decimal value && value > 0m ? value : PayrollPaidLeave.HoursPerLeaveDay;

    /// <summary>Hours paid for a leave day (or half-day) at the shop's hours-per-day.</summary>
    public static decimal LeaveHours(decimal dayFactor, decimal hoursPerDay) =>
        Resolve(hoursPerDay) * dayFactor;

    public static bool TryValidate(decimal value, out Dictionary<string, string[]> errors)
    {
        if (value < Min || value > Max || decimal.Remainder(value, Step) != 0m)
        {
            errors = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["hoursPerDay"] =
                [
                    "Hours per day must be from 1 through 24 in steps of 0.5."
                ]
            };
            return false;
        }

        errors = [];
        return true;
    }
}
