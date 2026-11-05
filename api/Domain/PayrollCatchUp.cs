namespace HrCore.Domain;

/// <summary>
/// A catch-up deduction for one person and one payroll period.
/// The period is the actual from and to dates. No cut-off ordinal is stored,
/// so editing the branch schedule later does not retarget this row.
/// </summary>
public sealed class PayrollCatchUp
{
    public Guid Id { get; set; }
    public Guid StaffId { get; set; }
    public Staff Staff { get; set; } = null!;
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }

    /// <summary>Same keys as <see cref="BranchDeductionSchedule.DeductionKey"/>.</summary>
    public string DeductionKey { get; set; } = "";

    public decimal Amount { get; set; }
    public string Note { get; set; } = "";
    public Guid? RecordedByStaffId { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}
