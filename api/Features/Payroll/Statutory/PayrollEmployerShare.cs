namespace HrCore.Features.Payroll;

/// <summary>
/// Employer SSS, PhilHealth, and Pag-IBIG for one period, from the statutory pack.
/// <see cref="Compute"/> is the only calculation. Lock does not call it. The owner review
/// step will call it before anything is stored; that step is not built yet.
/// The monthly amounts use the compensation of the period-end month. Monthly and hourly
/// staff are then placed the same way the employee statutory suggestion is placed: prorate
/// by period base / month base, or the shop's cut-off / split rule when one is saved.
/// Daily-paid staff take half of that monthly share on each half-month run.
/// A month before the pack's effective date is zero. Nothing here is stored.
/// </summary>
public static class PayrollEmployerShare
{
    public readonly record struct Shares(decimal Sss, decimal PhilHealth, decimal PagIbig);

    /// <summary>One deduction rule. <paramref name="Mode"/> null means prorate.</summary>
    public readonly record struct Rule(string Key, string? Mode, int? CutoffOrdinal);

    /// <summary>Which half-month run a daily-paid share lands on.</summary>
    public enum HalfMonth
    {
        Full,
        First,
        Second
    }

    /// <summary>
    /// OPEN QUESTION for Bienh. Which half-month run keeps a leftover centavo when a monthly
    /// employer share does not divide evenly. "unassigned" stores the truncated half on both
    /// runs and leaves the centavo off both. "first" adds it to the earlier run. "second"
    /// adds it to the later run.
    /// </summary>
    public const string OddCentavoHalf = "unassigned";

    /// <summary>Monthly employer shares for <paramref name="compensationBase"/>. Zero before January 2024.</summary>
    public static Shares Monthly(DateOnly month, decimal compensationBase)
    {
        if (month < StatutoryPack.EffectiveFrom)
        {
            return new Shares(0m, 0m, 0m);
        }

        return new Shares(
            StatutoryPack.SssEmployerShare(month, compensationBase),
            StatutoryPack.PhilHealthEmployerShare(month, compensationBase),
            StatutoryPack.PagIbigEmployerShare(month, compensationBase));
    }

    /// <summary>
    /// Employer shares for each staff row. The only calculation. A full calendar month keeps
    /// the monthly figure. A shorter period is the first half when it ends on or before the
    /// 15th, otherwise the second half.
    /// </summary>
    public static IReadOnlyDictionary<Guid, Shares> Compute(
        DateOnly from,
        DateOnly to,
        IReadOnlyList<PayrollStaffRowDto> periodRows,
        IReadOnlyList<PayrollStaffRowDto> monthRows,
        IReadOnlyList<Rule> rules,
        int? matchedOrdinal,
        int periodCount)
    {
        var monthStart = new DateOnly(to.Year, to.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var half = from <= monthStart && to >= monthEnd
            ? HalfMonth.Full
            : to.Day <= 15 ? HalfMonth.First : HalfMonth.Second;
        var shares = new Dictionary<Guid, Shares>(periodRows.Count);
        foreach (var row in periodRows)
        {
            var monthRow = monthRows.FirstOrDefault(candidate => candidate.StaffId == row.StaffId);
            var monthBase = monthRow is null ? 0m : PayrollService.StatutoryBase(monthRow);
            var periodBase = PayrollService.StatutoryBase(row);
            shares[row.StaffId] = ForPeriod(
                monthStart,
                monthBase,
                periodBase,
                rules,
                matchedOrdinal,
                periodCount,
                row.Basis,
                half);
        }

        return shares;
    }

    /// <summary>
    /// Two exact halves when the amount divides to the centavo. Otherwise follows
    /// <see cref="OddCentavoHalf"/>.
    /// </summary>
    public static (decimal Earlier, decimal Later) SplitHalfMonth(decimal monthly) =>
        SplitHalfMonth(monthly, OddCentavoHalf);

    /// <summary>Same split with an explicit answer for the leftover centavo, so the placeholder can be tested.</summary>
    public static (decimal Earlier, decimal Later) SplitHalfMonth(decimal monthly, string oddCentavoHalf)
    {
        var cents = decimal.ToInt64(decimal.Round(monthly * 100m, 0, MidpointRounding.AwayFromZero));
        var each = cents / 2;
        var leftover = cents - (each * 2);
        var earlier = each;
        var later = each;
        if (leftover != 0 && oddCentavoHalf == "first")
        {
            earlier += leftover;
        }
        else if (leftover != 0 && oddCentavoHalf == "second")
        {
            later += leftover;
        }

        return (earlier / 100m, later / 100m);
    }

    /// <summary>
    /// Employer shares for one register period. <paramref name="monthBase"/> and
    /// <paramref name="periodBase"/> are the statutory bases (register gross with non-taxable
    /// allowances removed). Empty <paramref name="rules"/> keeps proration.
    /// </summary>
    public static Shares ForPeriod(
        DateOnly month,
        decimal monthBase,
        decimal periodBase,
        IReadOnlyList<Rule> rules,
        int? matchedOrdinal,
        int periodCount,
        PayBasis? basis = null,
        HalfMonth half = HalfMonth.Full)
    {
        var monthly = Monthly(month, monthBase);
        if (basis == PayBasis.Daily && half != HalfMonth.Full)
        {
            var (earlierSss, laterSss) = SplitHalfMonth(monthly.Sss);
            var (earlierPhilHealth, laterPhilHealth) = SplitHalfMonth(monthly.PhilHealth);
            var (earlierPagIbig, laterPagIbig) = SplitHalfMonth(monthly.PagIbig);
            return half == HalfMonth.First
                ? new Shares(earlierSss, earlierPhilHealth, earlierPagIbig)
                : new Shares(laterSss, laterPhilHealth, laterPagIbig);
        }

        var fraction = PayrollStatutoryProration.FractionOf(periodBase, monthBase);
        return new Shares(
            Place("sss", monthly.Sss, fraction, rules, matchedOrdinal, periodCount),
            Place("philhealth", monthly.PhilHealth, fraction, rules, matchedOrdinal, periodCount),
            Place("pagibig", monthly.PagIbig, fraction, rules, matchedOrdinal, periodCount));
    }

    private static decimal Place(
        string key,
        decimal monthly,
        decimal fraction,
        IReadOnlyList<Rule> rules,
        int? matchedOrdinal,
        int periodCount)
    {
        var prorated = PayrollStatutoryAllocation.Round(monthly * fraction);
        string? mode = null;
        int? cutoffOrdinal = null;
        foreach (var candidate in rules)
        {
            if (candidate.Key == key)
            {
                mode = candidate.Mode;
                cutoffOrdinal = candidate.CutoffOrdinal;
                break;
            }
        }

        return PayrollStatutoryAllocation.Amount(
            mode,
            monthly,
            prorated,
            cutoffOrdinal,
            matchedOrdinal,
            periodCount);
    }
}
