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

    // SSS MSC bands and rates.
    // 2024 (still the January 2023 schedule): Circular No. 2022-033, RA 11199.
    // https://www.sss.gov.ph/wp-content/uploads/2023/02/2023-Schedule-of-Contributions.pdf
    // Minimum MSC 4,000, maximum 30,000. Total 14% (employee 4.5%, employer 9.5%).
    // 2025: Circular No. 2024-006.
    // https://www.sss.gov.ph/wp-content/uploads/2024/12/CI-2024-006-Publication.pdf
    // Minimum MSC 5,000, maximum 35,000. Total 15% (employee 5%, employer 10%) from January 2025.
    // Each MSC above the floor covers compensation from MSC − 250 through MSC + 249.99.
    // Below the first band (4,250 in 2024, 5,250 in 2025) uses the minimum MSC.
    // At or above the last band start (29,750 in 2024, 34,750 in 2025) uses the maximum MSC.
    // EC, employer only: 10 when the MSC is 14,500 or below, 30 from MSC 15,000.
    // https://www.sss.gov.ph/pay-contribution/
    public const decimal SssMscFloor2024 = 4_000m;
    public const decimal SssMscCeiling2024 = 30_000m;
    public const decimal SssMscFloor2025 = 5_000m;
    public const decimal SssMscCeiling2025 = 35_000m;
    public const decimal SssMscFloor = SssMscFloor2025;
    public const decimal SssMscCeiling = SssMscCeiling2025;
    public const decimal SssMscStep = 500m;
    public const decimal SssEmployeeRate2024 = 0.045m;
    public const decimal SssEmployeeRate2025 = 0.05m;
    public const decimal SssEmployerRate2024 = 0.095m;
    public const decimal SssEmployerRate2025 = 0.10m;
    public const decimal SssEcLow = 10m;
    public const decimal SssEcHigh = 30m;
    public const decimal SssEcMscCeilingForLow = 14_500m;

    // PhilHealth Circular No. 2020-0005: 5% in 2024 and in 2025, floor 10,000, ceiling 100,000.
    // https://www.philhealth.gov.ph/circulars/2020/circ2020-0005.pdf
    // Advisory No. 2025-0002 keeps 5% for 2025.
    // https://www.philhealth.gov.ph/advisories/2025/PA2025-0002.pdf
    // Employed members split the premium in half.
    public const decimal PhilHealthFloor = 10_000m;
    public const decimal PhilHealthCeiling = 100_000m;
    public const decimal PhilHealthEmployeeRate2024 = 0.025m;
    public const decimal PhilHealthEmployeeRate2025 = 0.025m;
    public const decimal PhilHealthEmployerRate2024 = 0.025m;
    public const decimal PhilHealthEmployerRate2025 = 0.025m;

    // Pag-IBIG Circular No. 460, effective February 2024.
    // pagibigfund.gov.ph did not return the PDF. The circular text was read from:
    // https://mpm.ph/wp-content/uploads/2024/01/HDMF-Circular-No.-460-Pag-ibig-HDMF-Table-2024.pdf
    // Fund salary of 1,500 and below: employee 1%, employer 2%. Over 1,500: both 2%.
    // Maximum fund salary 5,000 through January 2024, then 10,000 from February 2024.
    public static readonly DateOnly PagIbigCapRaisedOn = new(2024, 2, 1);
    public const decimal PagIbigSmallSalary = 1_500m;
    public const decimal PagIbigRateSmall = 0.01m;
    public const decimal PagIbigRate = 0.02m;
    public const decimal PagIbigCapBeforeRaise = 5_000m;
    public const decimal PagIbigCap = 10_000m;
    public const decimal PagIbigEmployerRate = 0.02m;

    // BIR monthly withholding on compensation (TRAIN table). Employee statutory shares are exempt.
    public const decimal WhtExemptMonthly = 20_833m;

    /// <summary>
    /// MSC for the current (2025) table. Suggestions pass the month into
    /// <see cref="SssMsc(DateOnly, decimal)"/>.
    /// </summary>
    public static decimal SssMsc(decimal baseAmount) => SssMsc(new DateOnly(2025, 1, 1), baseAmount);

    /// <summary>
    /// Compensation band for one MSC. The published range is centred on the MSC:
    /// 19,750–20,249.99 is MSC 20,000. Below the first band uses the minimum MSC.
    /// </summary>
    public static decimal SssMsc(DateOnly month, decimal baseAmount)
    {
        var (floor, ceiling) = SssMscRange(month);
        var firstBandStart = floor + (SssMscStep / 2m);
        if (baseAmount < firstBandStart)
        {
            return floor;
        }

        var rounded = Math.Round(baseAmount / SssMscStep, 0, MidpointRounding.AwayFromZero) * SssMscStep;
        if (rounded < floor)
        {
            return floor;
        }

        return rounded > ceiling ? ceiling : rounded;
    }

    public static (decimal Floor, decimal Ceiling) SssMscRange(DateOnly month) =>
        month >= new DateOnly(2025, 1, 1)
            ? (SssMscFloor2025, SssMscCeiling2025)
            : (SssMscFloor2024, SssMscCeiling2024);

    public static decimal SssEmployeeShare(DateOnly month, decimal baseAmount) =>
        Money(SssMsc(month, baseAmount) * RateForYear(month, SssEmployeeRate2024, SssEmployeeRate2025));

    public static decimal PhilHealthBase(decimal baseAmount) =>
        Math.Clamp(baseAmount, PhilHealthFloor, PhilHealthCeiling);

    public static decimal PhilHealthEmployeeShare(DateOnly month, decimal baseAmount) =>
        Money(PhilHealthBase(baseAmount)
            * RateForYear(month, PhilHealthEmployeeRate2024, PhilHealthEmployeeRate2025));

    public static decimal PagIbigCapFor(DateOnly month) =>
        month >= PagIbigCapRaisedOn ? PagIbigCap : PagIbigCapBeforeRaise;

    public static decimal PagIbigBase(decimal baseAmount) => PagIbigBase(PagIbigCapRaisedOn, baseAmount);

    public static decimal PagIbigBase(DateOnly month, decimal baseAmount) =>
        Math.Min(baseAmount, PagIbigCapFor(month));

    /// <summary>Current table: February 2024 onward (cap 10,000, 1% only through 1,500).</summary>
    public static decimal PagIbigEmployeeShare(decimal baseAmount) =>
        PagIbigEmployeeShare(PagIbigCapRaisedOn, baseAmount);

    public static decimal PagIbigEmployeeShare(DateOnly month, decimal baseAmount)
    {
        var rate = baseAmount <= PagIbigSmallSalary ? PagIbigRateSmall : PagIbigRate;
        return Money(PagIbigBase(month, baseAmount) * rate);
    }

    /// <summary>Employer SSS premium on the MSC, plus the EC. Months before 2025 use the 2024 rate.</summary>
    public static decimal SssEmployerShare(DateOnly month, decimal baseAmount)
    {
        var msc = SssMsc(month, baseAmount);
        return Money(msc * RateForYear(month, SssEmployerRate2024, SssEmployerRate2025)) + SssEc(msc);
    }

    /// <summary>EC remitted to SSS. MSC 14,500 and below is 10; the next step, 15,000, is 30.</summary>
    public static decimal SssEc(decimal msc) => msc <= SssEcMscCeilingForLow ? SssEcLow : SssEcHigh;

    /// <summary>Employer half of the PhilHealth premium, on the same floor and ceiling as the employee half.</summary>
    public static decimal PhilHealthEmployerShare(DateOnly month, decimal baseAmount) =>
        Money(PhilHealthBase(baseAmount)
            * RateForYear(month, PhilHealthEmployerRate2024, PhilHealthEmployerRate2025));

    /// <summary>Current employer table: February 2024 onward.</summary>
    public static decimal PagIbigEmployerShare(decimal baseAmount) =>
        PagIbigEmployerShare(PagIbigCapRaisedOn, baseAmount);

    /// <summary>Employer Pag-IBIG: 2% of the capped fund salary.</summary>
    public static decimal PagIbigEmployerShare(DateOnly month, decimal baseAmount) =>
        Money(PagIbigBase(month, baseAmount) * PagIbigEmployerRate);

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

    private static decimal RateForYear(DateOnly month, decimal rate2024, decimal rate2025) =>
        month >= new DateOnly(2025, 1, 1) ? rate2025 : rate2024;

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
