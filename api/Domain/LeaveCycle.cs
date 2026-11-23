namespace HrCore.Domain;

/// <summary>
/// One annual leave cycle window for a staff member, either calendar-year (Jan 1 – Dec 31)
/// or hire-anniversary (anniversary date through the day before the next anniversary).
/// </summary>
public readonly record struct LeaveCycleWindow(
    int CycleYear,
    DateOnly StartsOn,
    DateOnly ExpiresOn,
    bool UsesHireAnniversary);

/// <summary>
/// Pure date helpers for resolving leave cycles and effective expiry dates.
/// </summary>
public static class LeaveCycle
{
    /// <summary>Returns the active leave cycle containing <paramref name="date"/>.</summary>
    public static LeaveCycleWindow Resolve(LeaveExpirationMode mode, DateOnly? hireDate, DateOnly date)
    {
        if (mode != LeaveExpirationMode.HireAnniversary || hireDate is not DateOnly hired)
        {
            return CalendarYear(date.Year);
        }

        var thisYearAnniversary = AnniversaryOn(hired, date.Year);
        var cycleYear = date >= thisYearAnniversary || date.Year <= 1
            ? date.Year
            : date.Year - 1;
        return ForHireAnniversary(hired, cycleYear);
    }

    /// <summary>Returns the leave cycle window for a specific <paramref name="cycleYear"/>.</summary>
    public static LeaveCycleWindow ForYear(LeaveExpirationMode mode, DateOnly? hireDate, int cycleYear)
    {
        if (mode != LeaveExpirationMode.HireAnniversary || hireDate is not DateOnly hired)
        {
            return CalendarYear(cycleYear);
        }

        return ForHireAnniversary(hired, cycleYear);
    }

    /// <summary>Returns the custom expiry date if set, otherwise the cycle's automatic expiry date.</summary>
    public static DateOnly EffectiveExpiresOn(LeaveCycleWindow cycle, DateOnly? customExpiresOn) =>
        customExpiresOn ?? cycle.ExpiresOn;

    /// <summary>
    /// Returns the anniversary date of <paramref name="hireDate"/> in <paramref name="year"/>.
    /// Uses <see cref="DateOnly.AddYears"/> so Feb 29 hires map cleanly to Feb 28 in non-leap years.
    /// </summary>
    private static DateOnly AnniversaryOn(DateOnly hireDate, int year) =>
        hireDate.AddYears(year - hireDate.Year);

    private static LeaveCycleWindow CalendarYear(int year) =>
        new(year, new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), false);

    private static LeaveCycleWindow ForHireAnniversary(DateOnly hireDate, int cycleYear)
    {
        var startsOn = AnniversaryOn(hireDate, cycleYear);
        var expiresOn = cycleYear >= 9999
            ? DateOnly.MaxValue
            : AnniversaryOn(hireDate, cycleYear + 1).AddDays(-1);
        return new LeaveCycleWindow(cycleYear, startsOn, expiresOn, true);
    }
}
