namespace HrCore.Domain;

/// <summary>
/// One cut-off in a branch's monthly payroll schedule. <see cref="Ordinal"/> is 1-based and is the
/// "cut-off N" later deduction and loan rules refer to. A branch with no rows keeps free from/to
/// dates. Host apps add an additive migration for table <c>BranchPayrollCutoffs</c>; this package
/// does not ship a migration.
/// </summary>
public sealed class BranchPayrollCutoff
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    /// <summary>1-based position in the branch schedule. Cut-off 1 is the first row.</summary>
    public int Ordinal { get; set; }

    /// <summary>Day of month the period starts (1–31). When greater than <see cref="EndDay"/>, the start is in the previous month.</summary>
    public int StartDay { get; set; }

    /// <summary>Day of month the period ends (1–31). Generation clamps this to the month's last day.</summary>
    public int EndDay { get; set; }
}
