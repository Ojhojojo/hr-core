namespace HrCore.Domain;

/// <summary>
/// Staff.RestDays is a comma list of <see cref="DayOfWeek"/> ints. 0 is Sunday.
/// </summary>
public static class RestDayList
{
    public readonly record struct Update(bool Changed, string? Stored, string? Error);

    public static Update ParseUpdate(int[]? days, bool clear)
    {
        if (days is null && !clear)
        {
            return new Update(false, null, null);
        }

        if (clear || days is null || days.Length == 0)
        {
            return new Update(true, null, null);
        }

        var distinct = new HashSet<int>();
        foreach (var day in days)
        {
            if (day is < 0 or > 6)
            {
                return new Update(false, null, "Rest days must be between 0 and 6.");
            }

            if (!distinct.Add(day))
            {
                return new Update(false, null, "Rest days must be distinct.");
            }
        }

        return new Update(true, string.Join(",", distinct.OrderBy(day => day)), null);
    }

    public static IReadOnlySet<DayOfWeek> Parse(string? stored)
    {
        var set = new HashSet<DayOfWeek>();
        if (string.IsNullOrWhiteSpace(stored))
        {
            return set;
        }

        foreach (var part in stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var day) && day is >= 0 and <= 6)
            {
                set.Add((DayOfWeek)day);
            }
        }

        return set;
    }

    public static int[]? ToArray(string? stored)
    {
        var set = Parse(stored);
        return set.Count == 0 ? null : set.Select(day => (int)day).OrderBy(day => day).ToArray();
    }

    public static bool HasAny(string? stored) => Parse(stored).Count > 0;
}
