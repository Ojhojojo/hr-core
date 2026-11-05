namespace HrCore.Domain;

/// <summary>
/// How one deduction is placed on the branch's current cut-off list.
/// <see cref="CutoffOrdinal"/> is positional: it means "cut-off N" in the list as saved now.
/// It is not copied onto deductions, catch-ups, locked runs, or remittances.
/// Those keep the period's from and to dates.
/// </summary>
public sealed class BranchDeductionSchedule
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    /// <summary><c>sss</c>, <c>philhealth</c>, <c>pagibig</c>, <c>wht</c>, or <c>loan:</c> plus the loan kind name.</summary>
    public string DeductionKey { get; set; } = "";

    /// <summary><c>prorate</c> (today's behaviour), <c>cutoff</c>, or <c>split</c>.</summary>
    public string Mode { get; set; } = "prorate";

    /// <summary>Set only when <see cref="Mode"/> is <c>cutoff</c>. 1-based position in the current cut-off list.</summary>
    public int? CutoffOrdinal { get; set; }
}
