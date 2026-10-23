namespace HrCore.Domain;

/// <summary>
/// A loan or cash advance repaid by payroll instalments. Kind is free text.
/// Balance is principal minus unreversed <see cref="StaffLoanPayment"/> amounts. It is not stored.
/// Cancelled rows stop future instalments and keep the payments already taken.
/// </summary>
public sealed class StaffLoan
{
    public Guid Id { get; set; }
    public Guid StaffId { get; set; }
    public Staff Staff { get; set; } = null!;
    public string Kind { get; set; } = "";
    public decimal Principal { get; set; }
    public decimal PerPeriodAmount { get; set; }
    /// <summary>Instalments start in the first register period whose end is on or after this date.</summary>
    public DateOnly StartPeriod { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public Guid? CreatedByStaffId { get; set; }
    public Staff? CreatedByStaff { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancelReason { get; set; }
}

public enum StaffLoanStatus
{
    Active,
    Paid,
    Cancelled
}
