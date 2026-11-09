using Microsoft.EntityFrameworkCore;

namespace HrCore.Data;

/// <summary>
/// Access to HR entities that are part of <see cref="HrModel"/> but not members of <see cref="IHrDb"/>.
/// The host DbContext already is the <see cref="IHrDb"/>, so this does not add a member the host must implement.
/// The schedule sets exist only when <see cref="HrCoreFeatures.PayrollSchedules"/> is on.
/// <see cref="BranchRemittanceSettings"/> exists only when <see cref="HrCoreFeatures.EmployerRemittance"/> is on.
/// Calling one while the flag is off throws, because the type is not in the model.
/// </summary>
public static class HrDbSets
{
    public static DbSet<BranchPayrollCutoff> BranchPayrollCutoffs(this IHrDb db) =>
        ((DbContext)db).Set<BranchPayrollCutoff>();

    public static DbSet<BranchDeductionSchedule> BranchDeductionSchedules(this IHrDb db) =>
        ((DbContext)db).Set<BranchDeductionSchedule>();

    public static DbSet<PayrollCatchUp> PayrollCatchUps(this IHrDb db) =>
        ((DbContext)db).Set<PayrollCatchUp>();

    /// <summary>
    /// Exists only when <see cref="HrCoreFeatures.EmployerRemittance"/> is on.
    /// Calling it while the flag is off throws, because the type is not in the model.
    /// </summary>
    public static DbSet<BranchRemittanceSetting> BranchRemittanceSettings(this IHrDb db) =>
        ((DbContext)db).Set<BranchRemittanceSetting>();
}
