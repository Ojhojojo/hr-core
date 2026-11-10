namespace HrCore;

/// <summary>
/// Opt-in switches a host turns on. Every flag defaults to off, so a host that
/// does not set one keeps the model, routes, and payroll totals it already has.
/// Pass the same instance to <c>HrModel.Configure</c> and <c>AddHrCore</c>.
/// </summary>
public sealed class HrCoreFeatures
{
    /// <summary>
    /// Branch payroll cut-offs, deduction schedules, and catch-up rows
    /// (<c>BranchPayrollCutoff</c>, <c>BranchDeductionSchedule</c>, <c>PayrollCatchUp</c>).
    /// Off: those entities are not in the EF model, their routes are not mapped,
    /// and payroll does not query them. On: the cut-off and deduction schedule from #24 and #25.
    /// </summary>
    public bool PayrollSchedules { get; set; }

    /// <summary>
    /// Monthly remittance report, the per-branch on/off row (<c>BranchRemittanceSetting</c>),
    /// and the per-month export row (<c>BranchRemittanceExport</c>).
    /// Off: those entities are not in the EF model, the employer-share columns and the review
    /// columns on <c>PayrollRun</c> are ignored, the remittance routes are not mapped, and
    /// lock and void do not query them.
    /// On: a shop can turn the report on. Lock leaves the employer shares null. Confirm stores
    /// them after review. Export records the month and then the shares are read-only.
    /// </summary>
    public bool EmployerRemittance { get; set; }

    /// <summary>
    /// Loan kinds as a list (<c>LoanKind</c>) and the optional link on <c>StaffLoan</c>.
    /// Off: that table is not in the EF model, <c>StaffLoan.LoanKindId</c> is ignored,
    /// the loan-kind routes are not mapped, and payroll does not query them. Free-text
    /// loans keep deducting every period. On: each kind has a name, an active flag, and
    /// a cut-off rule. A loan that points at a kind follows that rule.
    /// </summary>
    public bool LoanKinds { get; set; }

    /// <summary>
    /// Per-staff payout hold (<c>StaffPayoutHold</c>) and the hold columns on <c>PayrollRunLine</c>.
    /// Off: that table is not in the EF model, those columns are ignored, the hold routes are not
    /// mapped, and payroll does not query them. Lock, the register, and payslips stay as they are.
    /// On: an owner or admin can hold a person's payout with a reason. Lock still computes the line
    /// and marks it <c>held</c>. The run reports that net as <c>totalHeld</c>, separate from
    /// <c>totalNet</c>. Release records the Manila date and who released it, on the line and the
    /// HR change log.
    /// </summary>
    public bool PayoutHold { get; set; }

    /// <summary>
    /// Deposited amount on a locked <c>PayrollRunLine</c>.
    /// Off: the column is ignored, the deposit route is not mapped, and payroll does not
    /// read or write it. On: an owner or admin can record what was deposited after lock.
    /// Discrepancy is net minus that amount. The locked line values stay as they are.
    /// </summary>
    public bool Deposits { get; set; }
}
