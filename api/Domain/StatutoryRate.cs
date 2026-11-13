namespace HrCore.Domain;

/// <summary>Kind keys stored on <see cref="StatutoryRate"/> and sent on the API.</summary>
public static class StatutoryRateKind
{
    public const string Sss = "sss";
    public const string PhilHealth = "philhealth";
    public const string PagIbig = "pagibig";
    public const string Wht = "wht";

    public static readonly IReadOnlyList<string> All = [Sss, PhilHealth, PagIbig, Wht];

    public static bool IsKnown(string? kind) =>
        kind is Sss or PhilHealth or PagIbig or Wht;
}

/// <summary>
/// One dated government table: SSS, PhilHealth, Pag-IBIG, or withholding.
/// Payroll picks the latest row of each kind whose <see cref="EffectiveFrom"/> is on or
/// before the month. Hosts add this table only when <see cref="HrCoreFeatures.StatutoryRates"/>
/// is on.
/// </summary>
public sealed class StatutoryRate
{
    public const int KindMaxLength = 20;

    public Guid Id { get; set; }

    /// <summary><see cref="StatutoryRateKind"/>: sss, philhealth, pagibig, or wht.</summary>
    public string Kind { get; set; } = "";

    /// <summary>First Manila calendar day this row covers. Inclusive.</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>SSS MSC floor. Null on other kinds.</summary>
    public decimal? MscFloor { get; set; }

    /// <summary>SSS MSC ceiling. Null on other kinds.</summary>
    public decimal? MscCeiling { get; set; }

    /// <summary>SSS MSC step (500 on the current table). Null on other kinds.</summary>
    public decimal? MscStep { get; set; }

    /// <summary>SSS or PhilHealth employee rate as a decimal (0.05 is 5%).</summary>
    public decimal? EmployeeRate { get; set; }

    /// <summary>SSS, PhilHealth, or Pag-IBIG employer rate as a decimal.</summary>
    public decimal? EmployerRate { get; set; }

    /// <summary>SSS EC when the MSC is at or below <see cref="EcMscCeilingForLow"/>.</summary>
    public decimal? EcLow { get; set; }

    /// <summary>SSS EC when the MSC is above <see cref="EcMscCeilingForLow"/>.</summary>
    public decimal? EcHigh { get; set; }

    /// <summary>SSS MSC at or below this amount uses <see cref="EcLow"/>.</summary>
    public decimal? EcMscCeilingForLow { get; set; }

    /// <summary>PhilHealth contribution floor. Null on other kinds.</summary>
    public decimal? Floor { get; set; }

    /// <summary>PhilHealth contribution ceiling. Null on other kinds.</summary>
    public decimal? Ceiling { get; set; }

    /// <summary>Pag-IBIG salary at or below which the small employee rate applies.</summary>
    public decimal? SmallSalary { get; set; }

    /// <summary>Pag-IBIG employee rate at or below <see cref="SmallSalary"/>.</summary>
    public decimal? RateSmall { get; set; }

    /// <summary>Pag-IBIG employee rate above <see cref="SmallSalary"/>.</summary>
    public decimal? Rate { get; set; }

    /// <summary>Pag-IBIG maximum fund salary.</summary>
    public decimal? Cap { get; set; }

    /// <summary>BIR monthly compensation exempt from withholding.</summary>
    public decimal? ExemptMonthly { get; set; }

    public ICollection<StatutoryTaxBracket> Brackets { get; set; } = [];
}

/// <summary>
/// One TRAIN monthly withholding band on a <see cref="StatutoryRateKind.Wht"/> row.
/// Hosts add this table only when <see cref="HrCoreFeatures.StatutoryRates"/> is on.
/// </summary>
public sealed class StatutoryTaxBracket
{
    public Guid Id { get; set; }

    public Guid StatutoryRateId { get; set; }

    public StatutoryRate StatutoryRate { get; set; } = null!;

    /// <summary>Ascending order within the parent row. The last band may have a null <see cref="UpTo"/>.</summary>
    public int SortOrder { get; set; }

    /// <summary>Inclusive upper bound of this band. Null is unbounded (the top band).</summary>
    public decimal? UpTo { get; set; }

    public decimal BaseTax { get; set; }

    /// <summary>Rate on the excess over <see cref="ExcessOver"/>, as a decimal (0.20 is 20%).</summary>
    public decimal Rate { get; set; }

    public decimal ExcessOver { get; set; }
}
