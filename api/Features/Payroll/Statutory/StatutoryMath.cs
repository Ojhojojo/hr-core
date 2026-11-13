namespace HrCore.Features.Payroll;

/// <summary>SSS table used by <see cref="StatutoryMath"/>.</summary>
public sealed record SssRateTable(
    decimal MscFloor,
    decimal MscCeiling,
    decimal MscStep,
    decimal EmployeeRate,
    decimal EmployerRate,
    decimal EcLow,
    decimal EcHigh,
    decimal EcMscCeilingForLow);

/// <summary>PhilHealth table used by <see cref="StatutoryMath"/>.</summary>
public sealed record PhilHealthRateTable(
    decimal Floor,
    decimal Ceiling,
    decimal EmployeeRate,
    decimal EmployerRate);

/// <summary>Pag-IBIG table used by <see cref="StatutoryMath"/>.</summary>
public sealed record PagIbigRateTable(
    decimal SmallSalary,
    decimal RateSmall,
    decimal Rate,
    decimal Cap,
    decimal EmployerRate);

/// <summary>One TRAIN monthly withholding band.</summary>
public sealed record WhtRateBracket(
    decimal? UpTo,
    decimal BaseTax,
    decimal Rate,
    decimal ExcessOver);

/// <summary>Withholding table used by <see cref="StatutoryMath"/>.</summary>
public sealed record WhtRateTable(
    decimal ExemptMonthly,
    IReadOnlyList<WhtRateBracket> Brackets);

/// <summary>
/// The latest covering row of each kind for one month. A null kind means no row covers that
/// month, so that share is zero.
/// </summary>
public sealed record StatutoryTables(
    SssRateTable? Sss,
    PhilHealthRateTable? PhilHealth,
    PagIbigRateTable? PagIbig,
    WhtRateTable? Wht)
{
    public bool CoversSuggestions =>
        Sss is not null && PhilHealth is not null && PagIbig is not null && Wht is not null;
}

/// <summary>
/// The same SSS, PhilHealth, Pag-IBIG, and TRAIN math as <see cref="StatutoryPack"/>, driven by
/// dated rows instead of compiled constants. Used only when <see cref="HrCoreFeatures.StatutoryRates"/>
/// is on. The pack stays the source of suggestions when the flag is off.
/// </summary>
public static class StatutoryMath
{
    public static decimal SssMsc(SssRateTable table, decimal baseAmount)
    {
        var firstBandStart = table.MscFloor + (table.MscStep / 2m);
        if (baseAmount < firstBandStart)
        {
            return table.MscFloor;
        }

        var rounded = Math.Round(baseAmount / table.MscStep, 0, MidpointRounding.AwayFromZero) * table.MscStep;
        if (rounded < table.MscFloor)
        {
            return table.MscFloor;
        }

        return rounded > table.MscCeiling ? table.MscCeiling : rounded;
    }

    public static decimal SssEmployeeShare(SssRateTable table, decimal baseAmount) =>
        Money(SssMsc(table, baseAmount) * table.EmployeeRate);

    public static decimal SssEmployerShare(SssRateTable table, decimal baseAmount)
    {
        var msc = SssMsc(table, baseAmount);
        return Money(msc * table.EmployerRate) + SssEc(table, msc);
    }

    public static decimal SssEc(SssRateTable table, decimal msc) =>
        msc <= table.EcMscCeilingForLow ? table.EcLow : table.EcHigh;

    public static decimal PhilHealthBase(PhilHealthRateTable table, decimal baseAmount) =>
        Math.Clamp(baseAmount, table.Floor, table.Ceiling);

    public static decimal PhilHealthEmployeeShare(PhilHealthRateTable table, decimal baseAmount) =>
        Money(PhilHealthBase(table, baseAmount) * table.EmployeeRate);

    public static decimal PhilHealthEmployerShare(PhilHealthRateTable table, decimal baseAmount) =>
        Money(PhilHealthBase(table, baseAmount) * table.EmployerRate);

    public static decimal PagIbigBase(PagIbigRateTable table, decimal baseAmount) =>
        Math.Min(baseAmount, table.Cap);

    public static decimal PagIbigEmployeeShare(PagIbigRateTable table, decimal baseAmount)
    {
        var rate = baseAmount <= table.SmallSalary ? table.RateSmall : table.Rate;
        return Money(PagIbigBase(table, baseAmount) * rate);
    }

    public static decimal PagIbigEmployerShare(PagIbigRateTable table, decimal baseAmount) =>
        Money(PagIbigBase(table, baseAmount) * table.EmployerRate);

    public static decimal MonthlyWithholdingTax(WhtRateTable table, decimal taxable)
    {
        if (taxable <= table.ExemptMonthly)
        {
            return 0m;
        }

        foreach (var bracket in table.Brackets)
        {
            if (bracket.UpTo is null || taxable <= bracket.UpTo)
            {
                return Money(bracket.BaseTax + (bracket.Rate * (taxable - bracket.ExcessOver)));
            }
        }

        return 0m;
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
