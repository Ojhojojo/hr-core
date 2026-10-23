namespace HrCore.Domain;

/// <summary>
/// Working-day rules for leave. A working day is a date that is not one of the person's rest days
/// and not a shop holiday. Leave balances and approvals both count with this helper.
/// </summary>
public static class LeaveDays
{
    public const decimal HalfDayAmount = 0.5m;

    /// <summary>
    /// Days charged for a leave request. A half day is only valid when start equals end, and it
    /// counts 0.5 when that date is a working day (0 when it is a rest day or holiday).
    /// </summary>
    public static decimal Count(
        DateOnly start,
        DateOnly end,
        LeaveHalfDay? halfDay,
        IReadOnlySet<DayOfWeek> restDays,
        IReadOnlySet<DateOnly> holidays)
    {
        if (halfDay is null)
        {
            return PaidDates(start, end, restDays, holidays).Count();
        }

        if (start != end)
        {
            throw new ArgumentException("A half day must be a single date.", nameof(halfDay));
        }

        return IsWorkingDay(start, restDays, holidays) ? HalfDayAmount : 0m;
    }

    /// <summary>The working dates from start to end, inclusive. Empty when end is before start.</summary>
    public static IEnumerable<DateOnly> PaidDates(
        DateOnly start,
        DateOnly end,
        IReadOnlySet<DayOfWeek> restDays,
        IReadOnlySet<DateOnly> holidays)
    {
        if (end < start)
        {
            yield break;
        }

        var last = end.DayNumber;
        for (var number = start.DayNumber; number <= last; number++)
        {
            var date = DateOnly.FromDayNumber(number);
            if (IsWorkingDay(date, restDays, holidays))
            {
                yield return date;
            }
        }
    }

    public static bool IsWorkingDay(
        DateOnly date,
        IReadOnlySet<DayOfWeek> restDays,
        IReadOnlySet<DateOnly> holidays) =>
        !restDays.Contains(date.DayOfWeek) && !holidays.Contains(date);
}
