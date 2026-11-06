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
}
