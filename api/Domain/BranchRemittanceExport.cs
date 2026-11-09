namespace HrCore.Domain;

/// <summary>
/// One recorded remittance export for a shop and calendar month.
/// Hosts add this table only when <see cref="HrCoreFeatures.EmployerRemittance"/> is on.
/// </summary>
public sealed class BranchRemittanceExport
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    /// <summary>Calendar year of the remittance month.</summary>
    public int Year { get; set; }

    /// <summary>Calendar month, 1 through 12.</summary>
    public int Month { get; set; }

    public DateTime ExportedAtUtc { get; set; }

    /// <summary>Staff who exported this month. A later export of the same month updates this.</summary>
    public Guid? ExportedBy { get; set; }

    public Staff? ExportedByStaff { get; set; }
}
