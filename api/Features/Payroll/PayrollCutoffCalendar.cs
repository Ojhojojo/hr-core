using System.Diagnostics.CodeAnalysis;

namespace HrCore.Features.Payroll;

/// <summary>Stored mode for a branch cut-off schedule. <see cref="Free"/> is the default and today's behaviour.</summary>
public static class PayrollCutoffMode
{
    public const string Free = "free";
    public const string Schedule = "schedule";
}

/// <summary>One cut-off rule. <see cref="Ordinal"/> is 1-based. Days are nominal days of the month, 1 through 31.</summary>
public sealed record PayrollCutoffRule(int Ordinal, int StartDay, int EndDay);

/// <summary>A concrete period generated from a rule for one calendar month (the month of the period end).</summary>
public sealed record PayrollCutoffPeriod(int Ordinal, DateOnly From, DateOnly To);

/// <summary>
/// Pure cut-off rules. Validation covers the nominal days 1 through 31 exactly once, so a start day
/// after the end day wraps into the previous month. Generation clamps 29, 30, and 31 to the real
/// last day of the month. Nothing here is company-specific.
/// </summary>
public static class PayrollCutoffCalendar
{
    public const int MinDay = 1;
    public const int MaxDay = 31;
    public const int MaxPerBranch = 8;

    /// <summary>When every ordinal is 0, number the rows 1..n in list order. Otherwise leave them for validation.</summary>
    public static IReadOnlyList<PayrollCutoffRule> WithOrdinals(IReadOnlyList<PayrollCutoffRule> cutoffs)
    {
        if (cutoffs.Count == 0 || cutoffs.Any(row => row.Ordinal != 0))
        {
            return cutoffs;
        }

        return cutoffs.Select((row, index) => row with { Ordinal = index + 1 }).ToList();
    }

    public static bool TryValidate(
        IReadOnlyList<PayrollCutoffRule> cutoffs,
        [NotNullWhen(false)] out Dictionary<string, string[]>? errors)
    {
        var bag = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        if (cutoffs.Count == 0)
        {
            Add(bag, "cutoffs", "Add at least one cut-off.");
            errors = Freeze(bag);
            return false;
        }

        if (cutoffs.Count > MaxPerBranch)
        {
            Add(bag, "cutoffs", $"A branch can have at most {MaxPerBranch} cut-offs.");
        }

        var daysOk = cutoffs.Count <= MaxPerBranch;
        for (var i = 0; i < cutoffs.Count; i++)
        {
            var cutoff = cutoffs[i];
            if (cutoff.Ordinal != i + 1)
            {
                Add(bag, $"cutoffs[{i}].ordinal", "Ordinals must run 1, 2, 3, … in the order listed.");
                daysOk = false;
            }

            if (cutoff.StartDay is < MinDay or > MaxDay)
            {
                Add(bag, $"cutoffs[{i}].startDay", "Start day must be from 1 through 31.");
                daysOk = false;
            }

            if (cutoff.EndDay is < MinDay or > MaxDay)
            {
                Add(bag, $"cutoffs[{i}].endDay", "End day must be from 1 through 31.");
                daysOk = false;
            }
        }

        if (daysOk)
        {
            var owner = new int[MaxDay + 1];
            for (var i = 0; i < cutoffs.Count; i++)
            {
                foreach (var day in NominalDays(cutoffs[i].StartDay, cutoffs[i].EndDay))
                {
                    if (owner[day] == 0)
                    {
                        owner[day] = cutoffs[i].Ordinal;
                        continue;
                    }

                    Add(
                        bag,
                        $"cutoffs[{i}].startDay",
                        $"Day {day} is already in cut-off {owner[day]}.");
                }
            }

            var missing = new List<int>();
            for (var day = MinDay; day <= MaxDay; day++)
            {
                if (owner[day] == 0)
                {
                    missing.Add(day);
                }
            }

            if (missing.Count > 0)
            {
                Add(
                    bag,
                    "cutoffs",
                    "Days not covered: " + JoinDays(missing) +
                    ". Every day from 1 through 31 must belong to one cut-off.");
            }
        }

        if (bag.Count == 0)
        {
            errors = null;
            return true;
        }

        errors = Freeze(bag);
        return false;
    }

    /// <summary>
    /// Periods whose end falls in <paramref name="month"/> of <paramref name="year"/>.
    /// A rule with start day after end day begins in the previous month. Days 29–31 clamp
    /// to that month's last day.
    /// </summary>
    public static IReadOnlyList<PayrollCutoffPeriod> PeriodsForMonth(
        int year,
        int month,
        IReadOnlyList<PayrollCutoffRule> cutoffs)
    {
        var list = new List<PayrollCutoffPeriod>(cutoffs.Count);
        foreach (var cutoff in cutoffs.OrderBy(row => row.Ordinal))
        {
            var end = Clamp(year, month, cutoff.EndDay);
            DateOnly start;
            if (cutoff.StartDay <= cutoff.EndDay)
            {
                start = Clamp(year, month, cutoff.StartDay);
            }
            else
            {
                var previous = new DateOnly(year, month, 1).AddMonths(-1);
                start = Clamp(previous.Year, previous.Month, cutoff.StartDay);
            }

            list.Add(new PayrollCutoffPeriod(cutoff.Ordinal, start, end));
        }

        return list;
    }

    public static DateOnly Clamp(int year, int month, int day)
    {
        var last = DateTime.DaysInMonth(year, month);
        var clamped = Math.Clamp(day, MinDay, last);
        return new DateOnly(year, month, clamped);
    }

    /// <summary><c>free</c> when there are no rules, otherwise <c>1:26-10;2:11-25</c>.</summary>
    public static string Describe(IReadOnlyList<PayrollCutoffRule> cutoffs)
    {
        if (cutoffs.Count == 0)
        {
            return PayrollCutoffMode.Free;
        }

        return string.Join(
            ';',
            cutoffs.OrderBy(row => row.Ordinal).Select(row => $"{row.Ordinal}:{row.StartDay}-{row.EndDay}"));
    }

    private static IEnumerable<int> NominalDays(int startDay, int endDay)
    {
        if (startDay <= endDay)
        {
            for (var day = startDay; day <= endDay; day++)
            {
                yield return day;
            }

            yield break;
        }

        for (var day = startDay; day <= MaxDay; day++)
        {
            yield return day;
        }

        for (var day = MinDay; day <= endDay; day++)
        {
            yield return day;
        }
    }

    private static string JoinDays(IReadOnlyList<int> days)
    {
        var parts = new List<string>();
        var index = 0;
        while (index < days.Count)
        {
            var start = days[index];
            var end = start;
            while (index + 1 < days.Count && days[index + 1] == end + 1)
            {
                index++;
                end = days[index];
            }

            parts.Add(start == end ? start.ToString() : $"{start}-{end}");
            index++;
        }

        return string.Join(", ", parts);
    }

    private static void Add(Dictionary<string, List<string>> bag, string key, string message)
    {
        if (!bag.TryGetValue(key, out var list))
        {
            list = [];
            bag[key] = list;
        }

        if (!list.Contains(message))
        {
            list.Add(message);
        }
    }

    private static Dictionary<string, string[]> Freeze(Dictionary<string, List<string>> bag) =>
        bag.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
}
