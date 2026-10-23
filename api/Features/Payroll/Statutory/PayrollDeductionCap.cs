using System.Globalization;

namespace HrCore.Features.Payroll;

/// <summary>
/// SSS, PhilHealth, Pag-IBIG, WHT, and Other cannot take the register net below zero.
/// When they exceed gross, the excess comes off Other, then WHT, then Pag-IBIG,
/// then PhilHealth, then SSS. Loans are applied after this cap.
/// </summary>
public static class PayrollDeductionCap
{
    public readonly record struct Shares(
        decimal Sss,
        decimal PhilHealth,
        decimal PagIbig,
        decimal Wht,
        decimal Other)
    {
        public decimal Total => Sss + PhilHealth + PagIbig + Wht + Other;
    }

    public sealed record Result(Shares Shares, string? Warning);

    public static Result Cap(Shares entered, decimal gross)
    {
        var sss = NonNegative(entered.Sss);
        var philHealth = NonNegative(entered.PhilHealth);
        var pagIbig = NonNegative(entered.PagIbig);
        var wht = NonNegative(entered.Wht);
        var other = NonNegative(entered.Other);
        var sum = sss + philHealth + pagIbig + wht + other;
        var ceiling = gross < 0m ? 0m : gross;
        if (sum <= ceiling)
        {
            return new Result(new Shares(sss, philHealth, pagIbig, wht, other), null);
        }

        var excess = sum - ceiling;
        (other, excess) = Reduce(other, excess);
        (wht, excess) = Reduce(wht, excess);
        (pagIbig, excess) = Reduce(pagIbig, excess);
        (philHealth, excess) = Reduce(philHealth, excess);
        (sss, excess) = Reduce(sss, excess);
        var shares = new Shares(sss, philHealth, pagIbig, wht, other);
        return new Result(shares, Warning(shares.Total));
    }

    public static string Warning(decimal cappedAt) =>
        "Deductions above gross pay: capped at ₱" +
        cappedAt.ToString("0.00", CultureInfo.InvariantCulture) +
        "; review the deductions for this period";

    private static decimal NonNegative(decimal value) => value < 0m ? 0m : value;

    private static (decimal Left, decimal Excess) Reduce(decimal amount, decimal excess)
    {
        if (excess <= 0m || amount <= 0m)
        {
            return (amount, excess);
        }

        var cut = amount < excess ? amount : excess;
        return (amount - cut, excess - cut);
    }
}
