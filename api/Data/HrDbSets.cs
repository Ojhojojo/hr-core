using Microsoft.EntityFrameworkCore;

namespace HrCore.Data;

/// <summary>
/// Access to HR entities that are part of <see cref="HrModel"/> but not members of <see cref="IHrDb"/>.
/// The host DbContext already is the <see cref="IHrDb"/>, so this does not add a member the host must implement.
/// </summary>
public static class HrDbSets
{
    public static DbSet<BranchPayrollCutoff> BranchPayrollCutoffs(this IHrDb db) =>
        ((DbContext)db).Set<BranchPayrollCutoff>();
}
