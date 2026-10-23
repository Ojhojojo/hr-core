namespace HrCore.Domain;

/// <summary>
/// Per-day pay override for a draft payroll period (#390). Does not change the punch;
/// only the gross for that calendar day. Cleared when removed; locked runs refuse writes.
/// </summary>
public sealed class PayrollDayPayOverride
{
    public Guid Id { get; set; }
    public Guid StaffId { get; set; }
    public Staff Staff { get; set; } = null!;
    public DateOnly WorkDate { get; set; }
    public PayrollDayPayMode Mode { get; set; }
    /// <summary>Required when <see cref="Mode"/> is Custom; otherwise null.</summary>
    public decimal? CustomAmount { get; set; }
    public string Reason { get; set; } = "";
    public Guid? SetByStaffId { get; set; }
    public Staff? SetByStaff { get; set; }
    public DateTime SetAtUtc { get; set; }
}
