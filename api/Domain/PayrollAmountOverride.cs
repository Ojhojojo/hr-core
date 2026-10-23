namespace HrCore.Domain;

/// <summary>
/// Owner or admin replacement for one suggested payroll amount on a draft period.
/// Key is generic so later slices reuse "allowances" and "loan:{loanId}". This slice uses "premium".
/// </summary>
public sealed class PayrollAmountOverride
{
    public Guid Id { get; set; }
    public Guid StaffId { get; set; }
    public Staff Staff { get; set; } = null!;
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public string Key { get; set; } = "";
    public decimal Amount { get; set; }
    public string Reason { get; set; } = "";
    public Guid? SetByStaffId { get; set; }
    public Staff? SetByStaff { get; set; }
    public DateTime SetAtUtc { get; set; }
}
