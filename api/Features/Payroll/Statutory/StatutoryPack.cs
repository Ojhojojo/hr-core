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

    // SSS monthly salary credit (MSC) and the employee share of the premium.
    // 2024 (the January 2023 schedule): Circular No. 2022-033. MSC 4,000 to 30,000, employee 4.5%.
    // 2025 on: Circular No. 2024-006. MSC 5,000 to 35,000, employee 5%.
    // Each MSC above the floor covers compensation from MSC − 250 through MSC + 249.99, so
    // 19,750–20,249.99 is MSC 20,000. Below the first band uses the floor; at or above the last
    // band start uses the ceiling.
    public const decimal SssMscFloor2024 = 4_000m;
    public const decimal SssMscCeiling2024 = 30_000m;
    public const decimal SssMscFloor = 5_000m;
    public const decimal SssMscCeiling = 35_000m;
    public const decimal SssMscStep = 500m;
    public const decimal SssEmployeeRate2024 = 0.045m;
    public const decimal SssEmployeeRate2025 = 0.05m;

    // PhilHealth Circular No. 2020-0005: 5% in 2024 and 2025 (Advisory No. 2025-0002), floor 10,000,
    // ceiling 100,000. Employed members split the premium in half, so the employee pays 2.5%.
    public const decimal PhilHealthFloor = 10_000m;
    public const decimal PhilHealthCeiling = 100_000m;
    public const decimal PhilHealthEmployeeRate2024 = 0.025m;
    public const decimal PhilHealthEmployeeRate2025 = 0.025m;

    // Pag-IBIG Circular No. 460: employee 1% on a fund salary of 1,500 and below, else 2%.
    // The fund salary cap is 5,000 through January 2024 and 10,000 from February 2024.
    public static readonly DateOnly PagIbigCapRaisedOn = new(2024, 2, 1);
    public const decimal PagIbigSmallSalary = 1_500m;
    public const decimal PagIbigRateSmall = 0.01m;
    public const decimal PagIbigRate = 0.02m;
    public const decimal PagIbigCapBeforeRaise = 5_000m;
    public const decimal PagIbigCap = 10_000m;

    // BIR monthly withholding on compensation (TRAIN table). Employee statutory shares are exempt.
    public const decimal WhtExemptMonthly = 20_833m;

    /// <summary>The MSC on the current (2025) table. Prefer the overload that takes the month.</summary>
    public static decimal SssMsc(decimal baseAmount) => SssMsc(new DateOnly(2025, 1, 1), baseAmount);

    /// <summary>
    /// The MSC for the month's table. The band is centred on the MSC: 20,250 is MSC 20,500 and
    /// 20,249.99 is MSC 20,000. Below the first band uses the floor; above the last uses the ceiling.
    /// </summary>
    public static decimal SssMsc(DateOnly month, decimal baseAmount)
    {
        var (floor, ceiling) = month >= new DateOnly(2025, 1, 1)
            ? (SssMscFloor, SssMscCeiling)
            : (SssMscFloor2024, SssMscCeiling2024);
        if (baseAmount < floor + (SssMscStep / 2m))
        {
            return floor;
        }

        var centred = Math.Round(baseAmount / SssMscStep, 0, MidpointRounding.AwayFromZero) * SssMscStep;
        return Math.Min(centred, ceiling);
    }

    public static decimal SssEmployeeShare(DateOnly month, decimal baseAmount) =>
        Money(SssMsc(month, baseAmount) * EmployeeRate(month, SssEmployeeRate2024, SssEmployeeRate2025));

    public static decimal PhilHealthBase(decimal baseAmount) =>
        Math.Clamp(baseAmount, PhilHealthFloor, PhilHealthCeiling);

    public static decimal PhilHealthEmployeeShare(DateOnly month, decimal baseAmount) =>
        Money(PhilHealthBase(baseAmount)
            * EmployeeRate(month, PhilHealthEmployeeRate2024, PhilHealthEmployeeRate2025));

    /// <summary>The fund salary on the current cap. Prefer the overload that takes the month.</summary>
    public static decimal PagIbigBase(decimal baseAmount) => Math.Min(baseAmount, PagIbigCap);

    /// <summary>The fund salary, capped at 5,000 through January 2024 and 10,000 after.</summary>
    public static decimal PagIbigBase(DateOnly month, decimal baseAmount) =>
        Math.Min(baseAmount, month >= PagIbigCapRaisedOn ? PagIbigCap : PagIbigCapBeforeRaise);

    /// <summary>The employee share on the current cap. Prefer the overload that takes the month.</summary>
    public static decimal PagIbigEmployeeShare(decimal baseAmount) =>
        PagIbigEmployeeShare(PagIbigCapRaisedOn, baseAmount);

    public static decimal PagIbigEmployeeShare(DateOnly month, decimal baseAmount)
    {
        var rate = baseAmount <= PagIbigSmallSalary ? PagIbigRateSmall : PagIbigRate;
        return Money(PagIbigBase(month, baseAmount) * rate);
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
