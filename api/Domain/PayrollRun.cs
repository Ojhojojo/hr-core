namespace HrCore.Domain;

/// <summary>
/// A payroll period locked for one shop. Locking freezes the register snapshot into lines.
/// Locked runs are never edited; corrections flow into the next period.
/// </summary>
public sealed class PayrollRun
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public PayrollRunStatus Status { get; set; }
    public string? Note { get; set; }
    public decimal TotalGross { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal TotalNet { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public Guid? CreatedByStaffId { get; set; }
    public Staff? CreatedByStaff { get; set; }
    public DateTime? LockedAtUtc { get; set; }
    public Guid? LockedByStaffId { get; set; }
    public Staff? LockedByStaff { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    public Guid? VoidedByStaffId { get; set; }
    public Staff? VoidedByStaff { get; set; }
    public ICollection<PayrollRunLine> Lines { get; set; } = [];
}
