namespace HrCore.Domain;

/// <summary>
/// One instalment written when a payroll run locks. Voiding that run sets <see cref="ReversedAtUtc"/>
/// so the balance goes back up. A reversed row stays for the audit trail.
/// </summary>
public sealed class StaffLoanPayment
{
    public Guid Id { get; set; }
    public Guid LoanId { get; set; }
    public StaffLoan Loan { get; set; } = null!;
    public Guid RunId { get; set; }
    public PayrollRun Run { get; set; } = null!;
    public Guid StaffId { get; set; }
    public Staff Staff { get; set; } = null!;
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReversedAtUtc { get; set; }
}
