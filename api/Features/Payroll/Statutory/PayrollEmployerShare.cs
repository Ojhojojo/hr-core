namespace HrCore.Features.Payroll;

/// <summary>
/// Employer SSS, PhilHealth, and Pag-IBIG for one period, from the statutory pack.
/// The monthly amounts use the compensation of the period-end month. They are then placed
/// on that period the same way the employee statutory suggestion is placed: prorate by
/// period base / month base, or the shop's cut-off / split rule when one is saved.
/// A month before the pack's effective date is zero. Nothing here is stored.
/// </summary>
public static class PayrollEmployerShare
{
    public readonly record struct Shares(decimal Sss, decimal PhilHealth, decimal PagIbig);

    /// <summary>One deduction rule. <paramref name="Mode"/> null means prorate.</summary>
    public readonly record struct Rule(string Key, string? Mode, int? CutoffOrdinal);

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
            StatutoryPack.PagIbigEmployerShare(compensationBase));
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
        int periodCount)
    {
        var monthly = Monthly(month, monthBase);
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
