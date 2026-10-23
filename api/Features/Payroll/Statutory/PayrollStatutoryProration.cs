using System.Globalization;

namespace HrCore.Features.Payroll;

/// <summary>
/// Period share of a calendar month's statutory suggestion.
/// f = periodBase / monthBase, clamped to [0, 1]. A zero month base yields f = 0.
/// SSS, PhilHealth, and Pag-IBIG are Round(monthly × f). WHT is Round(monthly WHT × f),
/// then capped at the period taxable (period base minus those three shares, floored at 0).
/// </summary>
public static class PayrollStatutoryProration
{
    public readonly record struct MonthShares(decimal Sss, decimal PhilHealth, decimal PagIbig, decimal Wht);

    public readonly record struct Result(
        decimal Fraction,
        decimal Sss,
        decimal PhilHealth,
        decimal PagIbig,
        decimal Wht,
        decimal PeriodTaxable);

    public static decimal FractionOf(decimal periodBase, decimal monthBase)
    {
        if (monthBase == 0m)
        {
            return 0m;
        }

        var fraction = periodBase / monthBase;
        if (fraction < 0m)
        {
            return 0m;
        }

        return fraction > 1m ? 1m : fraction;
    }

    public static Result Apportion(MonthShares monthly, decimal monthBase, decimal periodBase)
    {
        var fraction = FractionOf(periodBase, monthBase);
        var sss = Round(monthly.Sss * fraction);
        var philHealth = Round(monthly.PhilHealth * fraction);
        var pagIbig = Round(monthly.PagIbig * fraction);
        var wht = Round(monthly.Wht * fraction);
        var taxable = periodBase - sss - philHealth - pagIbig;
        if (taxable < 0m)
        {
            taxable = 0m;
        }

        if (wht > taxable)
        {
            wht = Round(taxable);
        }

        return new Result(fraction, sss, philHealth, pagIbig, wht, taxable);
    }

    public static string Note(DateOnly from, DateOnly to, decimal fraction)
    {
        var percent = Round(fraction * 100m);
        return "Prorated to the period: " +
            from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
            "–" +
            to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
            " (" + percent.ToString("0.00", CultureInfo.InvariantCulture) + "% of the month)";
    }

    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
