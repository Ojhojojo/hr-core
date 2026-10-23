namespace HrCore.Features.Payroll;

/// <summary>
/// Holiday, rest-day, and night-differential premiums on top of <see cref="PayrollService.ComputePeriodGross"/>.
/// Day premium uses one percent (the higher of holiday and rest day — they do not stack).
/// Night differential is a separate percent of the hourly rate and is not multiplied by the day percent.
/// Only Hourly and Daily earn premiums. The day is the Asia/Manila date of the punch in-time.
/// </summary>
public static class PayrollPremiumCalculator
{
    public const string OverrideKey = "premium";

    public sealed record Settings(
        decimal RegularHolidayPercent,
        decimal SpecialDayPercent,
        decimal RestDayPercent,
        bool HolidayPremiumEnabled,
        bool RestDayPremiumEnabled,
        bool NightDiffEnabled,
        decimal NightDiffPercent,
        TimeOnly NightStart,
        TimeOnly NightEnd)
    {
        public static Settings Defaults { get; } = FromBranch(new Branch());

        public static Settings FromBranch(Branch branch) => new(
            branch.PayrollRegularHolidayPercent,
            branch.PayrollSpecialDayPercent,
            branch.PayrollRestDayPercent,
            branch.PayrollHolidayPremiumEnabled,
            branch.PayrollRestDayPremiumEnabled,
            branch.PayrollNightDiffEnabled,
            branch.PayrollNightDiffPercent,
            branch.PayrollNightStart,
            branch.PayrollNightEnd);
    }

    public readonly record struct Result(
        decimal HolidayHours,
        decimal RestDayHours,
        decimal NightHours,
        decimal PremiumPay)
    {
        public static Result Zero { get; } = new(0m, 0m, 0m, 0m);

        public Result Add(Result other) => new(
            HolidayHours + other.HolidayHours,
            RestDayHours + other.RestDayHours,
            NightHours + other.NightHours,
            PremiumPay + other.PremiumPay);
    }

    public static Result Compute(
        PayBasis? basis,
        decimal rate,
        IReadOnlyList<(DateTime InUtc, DateTime OutUtc)> closedPunches,
        IReadOnlyDictionary<DateOnly, HolidayKind> holidays,
        IReadOnlySet<DayOfWeek> restDays,
        Settings settings)
    {
        if (basis is not (PayBasis.Hourly or PayBasis.Daily))
        {
            return Result.Zero;
        }

        var hourly = basis == PayBasis.Daily ? rate / 8m : rate;
        decimal holidayHours = 0m;
        decimal restDayHours = 0m;
        decimal premium = 0m;

        foreach (var dayGroup in closedPunches
            .Where(punch => punch.OutUtc > punch.InUtc)
            .GroupBy(punch => ManilaTime.ToManilaDate(punch.InUtc)))
        {
            decimal dayHours = 0m;
            foreach (var punch in dayGroup)
            {
                var hours = (decimal)(punch.OutUtc - punch.InUtc).TotalHours;
                if (hours > 0)
                {
                    dayHours += Math.Round(hours, 2, MidpointRounding.AwayFromZero);
                }
            }

            if (dayHours <= 0)
            {
                continue;
            }

            var (percent, pricedAsRest) = DayPercent(dayGroup.Key, holidays, restDays, settings);
            if (percent is not decimal chosen)
            {
                continue;
            }

            if (holidays.ContainsKey(dayGroup.Key))
            {
                holidayHours += dayHours;
            }

            if (pricedAsRest)
            {
                restDayHours += dayHours;
            }

            var raw = dayHours * hourly * (chosen - 100m) / 100m;
            premium += Math.Round(raw, 2, MidpointRounding.AwayFromZero);
        }

        decimal nightHours = 0m;
        if (settings.NightDiffEnabled)
        {
            foreach (var punch in closedPunches)
            {
                if (punch.OutUtc > punch.InUtc)
                {
                    nightHours += NightOverlapHours(punch.InUtc, punch.OutUtc, settings.NightStart, settings.NightEnd);
                }
            }

            var nightPay = nightHours * hourly * settings.NightDiffPercent / 100m;
            premium += Math.Round(nightPay, 2, MidpointRounding.AwayFromZero);
        }

        return new Result(holidayHours, restDayHours, nightHours, premium);
    }

    /// <summary>
    /// Holiday percent when that premium is on, else rest-day percent when that premium is on.
    /// Both on: the higher percent. A tie keeps the holiday percent, so the day is not rest-day-priced.
    /// </summary>
    private static (decimal? Percent, bool PricedAsRest) DayPercent(
        DateOnly day,
        IReadOnlyDictionary<DateOnly, HolidayKind> holidays,
        IReadOnlySet<DayOfWeek> restDays,
        Settings settings)
    {
        decimal? holidayPercent = null;
        if (settings.HolidayPremiumEnabled && holidays.TryGetValue(day, out var kind))
        {
            holidayPercent = kind == HolidayKind.Regular
                ? settings.RegularHolidayPercent
                : settings.SpecialDayPercent;
        }

        decimal? restPercent = null;
        if (settings.RestDayPremiumEnabled && restDays.Contains(day.DayOfWeek))
        {
            restPercent = settings.RestDayPercent;
        }

        if (holidayPercent is decimal holiday && restPercent is decimal rest)
        {
            return holiday >= rest ? (holiday, false) : (rest, true);
        }

        if (holidayPercent is decimal onlyHoliday)
        {
            return (onlyHoliday, false);
        }

        if (restPercent is decimal onlyRest)
        {
            return (onlyRest, true);
        }

        return (null, false);
    }

    /// <summary>
    /// Hours of [inUtc, outUtc) inside the Manila window [nightStart, nightEnd).
    /// The window may cross midnight. Rounded AwayFromZero to 2 decimals per punch.
    /// An empty window (start == end) is zero.
    /// </summary>
    internal static decimal NightOverlapHours(
        DateTime inUtc,
        DateTime outUtc,
        TimeOnly nightStart,
        TimeOnly nightEnd)
    {
        if (outUtc <= inUtc || nightStart == nightEnd)
        {
            return 0m;
        }

        var cursor = ManilaTime.ToManila(inUtc);
        var end = ManilaTime.ToManila(outUtc);
        decimal hours = 0m;
        for (var guard = 0; guard < 48 && cursor < end; guard++)
        {
            var next = NextBoundary(cursor, nightStart, nightEnd);
            if (next <= cursor)
            {
                break;
            }

            if (next > end)
            {
                next = end;
            }

            if (IsNight(TimeOnly.FromDateTime(cursor), nightStart, nightEnd))
            {
                hours += (decimal)(next - cursor).TotalHours;
            }

            cursor = next;
        }

        return hours <= 0 ? 0m : Math.Round(hours, 2, MidpointRounding.AwayFromZero);
    }

    private static bool IsNight(TimeOnly time, TimeOnly nightStart, TimeOnly nightEnd)
    {
        if (nightStart < nightEnd)
        {
            return time >= nightStart && time < nightEnd;
        }

        return time >= nightStart || time < nightEnd;
    }

    private static DateTime NextBoundary(DateTime cursor, TimeOnly nightStart, TimeOnly nightEnd)
    {
        var day = DateOnly.FromDateTime(cursor);
        DateTime? best = null;
        foreach (var candidate in new[]
        {
            day.ToDateTime(nightStart),
            day.ToDateTime(nightEnd),
            day.AddDays(1).ToDateTime(nightStart),
            day.AddDays(1).ToDateTime(nightEnd)
        })
        {
            if (candidate > cursor && (best is null || candidate < best))
            {
                best = candidate;
            }
        }

        return best ?? cursor.AddTicks(1);
    }
}
