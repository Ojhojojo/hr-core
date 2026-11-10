using System.Globalization;
using HrCore.Features.SettingsLog;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Payroll;

public sealed partial class PayrollService
{
    public async Task<LoanKindListResponse> ListLoanKindsAsync(CancellationToken cancellationToken)
    {
        var rows = await db.LoanKinds().AsNoTracking()
            .OrderBy(row => row.Name)
            .ToListAsync(cancellationToken);
        return new LoanKindListResponse(rows.Select(ToLoanKindDto).ToList());
    }

    public async Task<LoanKindWriteResult> CreateLoanKindAsync(
        SaveLoanKindRequest request,
        Guid? actorStaffId,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeLoanKind(request.Name, request.CutoffOrdinal, out var name, out var ordinal, out var errors))
        {
            return LoanKindWriteResult.Bad(errors!);
        }

        if (await LoanKindNameTakenAsync(name, null, cancellationToken))
        {
            return LoanKindWriteResult.Conflict("A loan kind with this name already exists.");
        }

        var active = request.Active ?? true;
        var row = new LoanKind
        {
            Id = Guid.NewGuid(),
            Name = name,
            Active = active,
            CutoffOrdinal = ordinal
        };
        db.LoanKinds().Add(row);
        new SettingsChangeLogService(db).Record(
            actorStaffId,
            null,
            SettingsArea.PayrollLoanKind,
            "kind",
            null,
            LoanKindLog(name, active, ordinal));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return LoanKindWriteResult.Conflict("A loan kind with this name already exists.");
        }

        return LoanKindWriteResult.Ok(ToLoanKindDto(row));
    }

    public async Task<LoanKindWriteResult> UpdateLoanKindAsync(
        Guid id,
        SaveLoanKindRequest request,
        Guid? actorStaffId,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeLoanKind(request.Name, request.CutoffOrdinal, out var name, out var ordinal, out var errors))
        {
            return LoanKindWriteResult.Bad(errors!);
        }

        var row = await db.LoanKinds().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return LoanKindWriteResult.NotFound();
        }

        if (await LoanKindNameTakenAsync(name, row.Id, cancellationToken))
        {
            return LoanKindWriteResult.Conflict("A loan kind with this name already exists.");
        }

        var active = request.Active ?? true;
        var before = LoanKindLog(row.Name, row.Active, row.CutoffOrdinal);
        row.Name = name;
        row.Active = active;
        row.CutoffOrdinal = ordinal;
        new SettingsChangeLogService(db).Record(
            actorStaffId,
            null,
            SettingsArea.PayrollLoanKind,
            "kind",
            before,
            LoanKindLog(name, active, ordinal));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return LoanKindWriteResult.Conflict("A loan kind with this name already exists.");
        }

        return LoanKindWriteResult.Ok(ToLoanKindDto(row));
    }

    public async Task<LoanKindDeleteResult> DeleteLoanKindAsync(
        Guid id,
        Guid? actorStaffId,
        CancellationToken cancellationToken)
    {
        var row = await db.LoanKinds().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return LoanKindDeleteResult.NotFound();
        }

        var inUse = await db.StaffLoans.AnyAsync(loan => loan.LoanKindId == id, cancellationToken);
        if (inUse)
        {
            return LoanKindDeleteResult.Conflict(
                "This loan kind is in use. Deactivate it instead of deleting it.");
        }

        var before = LoanKindLog(row.Name, row.Active, row.CutoffOrdinal);
        db.LoanKinds().Remove(row);
        new SettingsChangeLogService(db).Record(
            actorStaffId,
            null,
            SettingsArea.PayrollLoanKind,
            "kind",
            before,
            null);
        await db.SaveChangesAsync(cancellationToken);
        return LoanKindDeleteResult.Ok();
    }

    private async Task<Dictionary<Guid, LoanKind>> LoadLinkedLoanKindsAsync(
        IEnumerable<StaffLoan> loans,
        CancellationToken cancellationToken)
    {
        if (!features.LoanKinds)
        {
            return [];
        }

        var ids = loans
            .Where(row => row.LoanKindId is Guid)
            .Select(row => row.LoanKindId!.Value)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.LoanKinds().AsNoTracking()
            .Where(row => ids.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
    }

    private static PayrollLoanSchedule.LoanInput ToLoanInput(
        StaffLoan row,
        IReadOnlyDictionary<Guid, decimal> paid,
        IReadOnlyDictionary<Guid, LoanKind> kinds)
    {
        LoanKind? linked = null;
        if (row.LoanKindId is Guid kindId)
        {
            kinds.TryGetValue(kindId, out linked);
        }

        return new PayrollLoanSchedule.LoanInput(
            row.Id,
            linked?.Name ?? row.Kind,
            RoundMoney(row.Principal - paid.GetValueOrDefault(row.Id)),
            row.PerPeriodAmount,
            row.StartPeriod,
            row.CreatedAtUtc,
            row.CancelledAtUtc is not null,
            linked is not null,
            linked?.CutoffOrdinal);
    }

    private async Task<Dictionary<Guid, int?>> LoadKindOrdinalsAsync(
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken)
    {
        if (!features.LoanKinds || !features.PayrollSchedules)
        {
            return [];
        }

        var cutoffs = await db.BranchPayrollCutoffs().AsNoTracking()
            .OrderBy(row => row.Ordinal)
            .ToListAsync(cancellationToken);
        var ordinals = new Dictionary<Guid, int?>();
        foreach (var group in cutoffs.GroupBy(row => row.BranchId))
        {
            var rules = group
                .Select(row => new PayrollCutoffRule(row.Ordinal, row.StartDay, row.EndDay))
                .ToList();
            var periods = PayrollCutoffCalendar.PeriodsCovering(start, end, rules);
            var match = periods.FirstOrDefault(period => period.From == start && period.To == end);
            ordinals[group.Key] = match?.Ordinal;
        }

        return ordinals;
    }

    /// <summary>
    /// A linked kind with no cut-off number stays. Cut-off N only stays when this register
    /// is that cut-off of the worker's shop. Anything else is not taken.
    /// </summary>
    private static IReadOnlyList<PayrollLoanSchedule.LoanInput> ApplyKindCutoff(
        IReadOnlyList<PayrollLoanSchedule.LoanInput> loans,
        Guid? branchId,
        LoanContext context)
    {
        if (!loans.Any(loan => loan.UsesKindRule))
        {
            return loans;
        }

        int? matched = null;
        if (branchId is Guid home && context.OrdinalsByBranch.TryGetValue(home, out var ordinal))
        {
            matched = ordinal;
        }

        var kept = new List<PayrollLoanSchedule.LoanInput>(loans.Count);
        foreach (var loan in loans)
        {
            if (loan.UsesKindRule && loan.KindCutoffOrdinal is int only && only != matched)
            {
                continue;
            }

            kept.Add(loan);
        }

        return kept;
    }

    private async Task<(LoanKind? Kind, LoanWriteResult? Error)> ResolveLoanLinkAsync(
        Guid? requestedId,
        Guid? currentId,
        bool creating,
        CancellationToken cancellationToken)
    {
        if (!features.LoanKinds || requestedId is not Guid kindId)
        {
            return (null, null);
        }

        var kind = await db.LoanKinds().FirstOrDefaultAsync(row => row.Id == kindId, cancellationToken);
        if (kind is null)
        {
            return (null, LoanWriteResult.Bad("That loan kind does not exist."));
        }

        if (!kind.Active && (creating || currentId != kind.Id))
        {
            return (null, LoanWriteResult.Bad("That loan kind is not active."));
        }

        return (kind, null);
    }

    private async Task<bool> LoanKindNameTakenAsync(
        string name,
        Guid? exceptId,
        CancellationToken cancellationToken)
    {
        var rows = await db.LoanKinds().AsNoTracking()
            .Select(row => new { row.Id, row.Name })
            .ToListAsync(cancellationToken);
        return rows.Any(row =>
            row.Id != exceptId
            && string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryNormalizeLoanKind(
        string? name,
        int? cutoffOrdinal,
        out string trimmed,
        out int? ordinal,
        out Dictionary<string, string[]>? errors)
    {
        trimmed = name?.Trim() ?? "";
        ordinal = cutoffOrdinal;
        var bag = new Dictionary<string, string[]>();
        if (trimmed.Length == 0 || trimmed.Length > 80)
        {
            bag["name"] = ["Name is required and must be 80 characters or fewer."];
        }

        if (cutoffOrdinal is < 1)
        {
            bag["cutoffOrdinal"] = ["Cut-off must be 1 or more. Leave it empty to deduct every period."];
        }

        if (bag.Count > 0)
        {
            errors = bag;
            return false;
        }

        errors = null;
        return true;
    }

    private static string LoanKindLog(string name, bool active, int? ordinal) =>
        name
        + (active ? " active" : " inactive")
        + (ordinal is int cutoff
            ? " cut-off " + cutoff.ToString(CultureInfo.InvariantCulture)
            : " every period");

    private static LoanKindDto ToLoanKindDto(LoanKind row) =>
        new(row.Id, row.Name, row.Active, row.CutoffOrdinal);
}
