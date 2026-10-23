namespace HrCore.Features.Payroll;

/// <summary>
/// Statutory pack v1: the government numbers the suggestion endpoint works from. They produce
/// SUGGESTED amounts only — the owner verifies them against the current SSS, PhilHealth,
/// Pag-IBIG, and BIR tables before saving. Effective-dated: months before January 2024 return no
/// suggestion. When a table changes, this pack is the one place to update.
/// </summary>
public static class StatutoryPack
{
    public static readonly DateOnly EffectiveFrom = new(2024, 1, 1);

    // SSS monthly salary credit: 5,000 to 35,000 in 500 steps; the employee share of the premium.
    public const decimal SssMscFloor = 5_000m;
    public const decimal SssMscCeiling = 35_000m;
    public const decimal SssMscStep = 500m;
    public const decimal SssEmployeeRate2024 = 0.045m;
    public const decimal SssEmployeeRate2025 = 0.05m;

    // PhilHealth premium: the employee half (2.25% of 4.5% in 2024, 2.5% of 5% in 2025).
    public const decimal PhilHealthFloor = 10_000m;
    public const decimal PhilHealthCeiling = 100_000m;
    public const decimal PhilHealthEmployeeRate2024 = 0.0225m;
    public const decimal PhilHealthEmployeeRate2025 = 0.025m;

    // Pag-IBIG: 1% of a small salary, else 2% capped at 10,000 (max 200 a month).
    public const decimal PagIbigSmallSalary = 5_000m;
    public const decimal PagIbigRateSmall = 0.01m;
    public const decimal PagIbigRate = 0.02m;
    public const decimal PagIbigCap = 10_000m;

    // BIR monthly withholding on compensation (TRAIN table). Employee statutory shares are exempt.
    public const decimal WhtExemptMonthly = 20_833m;

    /// <summary>The salary bracket the contribution is computed on. Below the floor uses the floor.</summary>
    public static decimal SssMsc(decimal baseAmount)
    {
        if (baseAmount <= SssMscFloor)
        {
            return SssMscFloor;
        }

        var stepped = Math.Floor(baseAmount / SssMscStep) * SssMscStep;
        return stepped > SssMscCeiling ? SssMscCeiling : stepped;
    }

    public static decimal SssEmployeeShare(DateOnly month, decimal baseAmount) =>
        Money(SssMsc(baseAmount) * EmployeeRate(month, SssEmployeeRate2024, SssEmployeeRate2025));

    public static decimal PhilHealthBase(decimal baseAmount) =>
        Math.Clamp(baseAmount, PhilHealthFloor, PhilHealthCeiling);

    public static decimal PhilHealthEmployeeShare(DateOnly month, decimal baseAmount) =>
        Money(PhilHealthBase(baseAmount)
            * EmployeeRate(month, PhilHealthEmployeeRate2024, PhilHealthEmployeeRate2025));

    public static decimal PagIbigBase(decimal baseAmount) => Math.Min(baseAmount, PagIbigCap);

    public static decimal PagIbigEmployeeShare(decimal baseAmount)
    {
        var rate = baseAmount <= PagIbigSmallSalary ? PagIbigRateSmall : PagIbigRate;
        return Money(PagIbigBase(baseAmount) * rate);
    }

    public static decimal MonthlyWithholdingTax(decimal taxable) => taxable switch
    {
        <= WhtExemptMonthly => 0m,
        <= 33_333m => Money(0.20m * (taxable - WhtExemptMonthly)),
        <= 66_667m => Money(2_500m + 0.25m * (taxable - 33_333m)),
        <= 166_667m => Money(10_833.33m + 0.30m * (taxable - 66_667m)),
        <= 666_667m => Money(40_833.33m + 0.32m * (taxable - 166_667m)),
        _ => Money(200_833.33m + 0.35m * (taxable - 666_667m))
    };

    public static IReadOnlyList<string> VerificationNotes =>
    [
        "Suggested from the statutory pack. Verify against the current SSS, PhilHealth, Pag-IBIG, and BIR tables before saving.",
        "13th-month pay and other exempt amounts are not part of this computation.",
        "The amounts are monthly. Divide them for shorter pay periods."
    ];

    private static decimal EmployeeRate(DateOnly month, decimal rate2024, decimal rate2025) =>
        month >= new DateOnly(2025, 1, 1) ? rate2025 : rate2024;

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
