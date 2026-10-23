using HrCore.Features.HrChangeLog;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Payroll;

/// <summary>
/// Dated pay rates for one worker. History is append-only: a new row supersedes the previous one
/// from its <see cref="StaffPayRate.EffectiveFrom"/> date, and the Staff row stays the current
/// snapshot so every existing screen keeps working.
/// </summary>
public sealed class PayrollRateService(IHrDb db, HrChangeLogService hr)
{
    public const string LockedPeriodMessage =
        "That date is inside a locked payroll run. Post the change in the next period.";

    public const int MaxReasonLength = 200;

    public async Task<PayrollRatesResponse?> GetRatesAsync(
        IReadOnlyList<Branch> shops,
        Guid staffId,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        var staff = await LoadStaffAsync(staffId, cancellationToken);
        if (staff is null || !PayrollService.IsInRegisterScope(staff, shops, includeUnscopedOwners))
        {
            return null;
        }

        var history = await HistoryRowsAsync(staff.Id, cancellationToken);
        return ToResponse(staff, history);
    }

    /// <summary>
    /// Appends one rate row, refreshes the Staff snapshot, and writes the HR trail.
    /// Returns an error when the effective date falls inside a locked run period.
    /// </summary>
    public async Task<(PayrollRatesResponse? Result, string? Error)> SetRateAsync(
        IReadOnlyList<Branch> shops,
        Guid staffId,
        SetPayrollRateRequest request,
        Guid? actorStaffId,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        var staff = await LoadStaffAsync(staffId, cancellationToken);
        if (staff is null || !PayrollService.IsInRegisterScope(staff, shops, includeUnscopedOwners))
        {
            return (null, null);
        }

        if (request.RatePhp < 0)
        {
            return (null, "Rate must be zero or more.");
        }

        if (request.Reason is { Length: > MaxReasonLength })
        {
            return (null, $"Reason must be {MaxReasonLength} characters or fewer.");
        }

        var (otAfterHours, otMultiplier) = PayrollService.NormaliseOt(
            request.Basis, request.OtAfterHoursPerDay, request.OtMultiplier);
        var rate = PayrollService.RoundMoney(request.RatePhp);
        var effectiveFrom = request.EffectiveFrom;
        if (await PayrollRunService.IsPeriodLockedAsync(db, staff.Id, effectiveFrom, effectiveFrom, cancellationToken))
        {
            return (null, LockedPeriodMessage);
        }

        var existing = await db.StaffPayRates
            .FirstOrDefaultAsync(r => r.StaffId == staff.Id && r.EffectiveFrom == effectiveFrom, cancellationToken);
        if (existing is null)
        {
            db.StaffPayRates.Add(new StaffPayRate
            {
                Id = Guid.NewGuid(),
                StaffId = staff.Id,
                EffectiveFrom = effectiveFrom,
                Basis = request.Basis,
                RatePhp = rate,
                OtAfterHoursPerDay = otAfterHours,
                OtMultiplier = otMultiplier,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByStaffId = actorStaffId,
                Reason = BlankToNull(request.Reason)
            });
        }
        else
        {
            // Same effective date: correct that row instead of breaking the unique index.
            existing.Basis = request.Basis;
            existing.RatePhp = rate;
            existing.OtAfterHoursPerDay = otAfterHours;
            existing.OtMultiplier = otMultiplier;
            existing.Reason = BlankToNull(request.Reason) ?? existing.Reason;
        }

        RecordSnapshotChanges(staff, request.Basis, rate, otAfterHours, otMultiplier, actorStaffId);

        staff.PayBasis = request.Basis;
        staff.RatePhp = rate;
        staff.OtAfterHoursPerDay = otAfterHours;
        staff.OtMultiplier = otMultiplier;
        await db.SaveChangesAsync(cancellationToken);

        var history = await HistoryRowsAsync(staff.Id, cancellationToken);
        return (ToResponse(staff, history), null);
    }

    /// <summary>
    /// Rate spans for one worker across [start, end]. Rules: a day is priced by the latest row
    /// effective on or before that day; days before the earliest considered row use that earliest
    /// row (it extends backwards); a worker with no history at all falls back to the Staff snapshot.
    /// Consecutive days with the same basis, rate, and OT merge into one slice.
    /// </summary>
    public async Task<IReadOnlyList<RateSlice>> ResolveRateSlicesAsync(
        Guid staffId,
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken)
    {
        var staff = await LoadStaffAsync(staffId, cancellationToken);
        var rows = await HistoryRowsAsync(staffId, cancellationToken);
        return BuildSlices(staff, rows, start, end);
    }

    internal static IReadOnlyList<RateSlice> BuildSlices(
        Domain.Staff? staff,
        IReadOnlyList<StaffPayRate> rows,
        DateOnly start,
        DateOnly end)
    {
        if (start > end)
        {
            (start, end) = (end, start);
        }

        var snapshot = new RateSlice(
            start,
            end,
            staff?.PayBasis,
            staff?.RatePhp,
            staff?.OtAfterHoursPerDay,
            staff?.OtMultiplier);

        if (rows.Count == 0)
        {
            return [snapshot];
        }

        var considered = rows
            .Where(r => r.EffectiveFrom <= end)
            .OrderBy(r => r.EffectiveFrom)
            .ToList();
        if (considered.Count == 0)
        {
            // Every row starts after the period. The snapshot is the current authority and mirrors
            // the newest row, so keep pricing the period the way the app always did.
            return [snapshot];
        }

        var slices = new List<RateSlice>();
        for (var i = 0; i < considered.Count; i++)
        {
            var row = considered[i];
            var from = i == 0 || row.EffectiveFrom < start ? start : row.EffectiveFrom;
            var to = end;
            if (i + 1 < considered.Count)
            {
                var nextStart = considered[i + 1].EffectiveFrom.AddDays(-1);
                if (nextStart < to)
                {
                    to = nextStart;
                }
            }

            if (to < from)
            {
                continue;
            }

            Append(slices, new RateSlice(
                from, to, row.Basis, row.RatePhp, row.OtAfterHoursPerDay, row.OtMultiplier));
        }

        if (slices.Count == 0)
        {
            return [snapshot];
        }

        if (slices[0].From > start)
        {
            // Safety net: the earliest row extends backwards over any leading gap.
            slices[0] = slices[0] with { From = start };
        }

        return slices;
    }

    private static void Append(List<RateSlice> slices, RateSlice slice)
    {
        if (slices.Count > 0 && SameRate(slices[^1], slice))
        {
            slices[^1] = slices[^1] with { To = slice.To };
            return;
        }

        slices.Add(slice);
    }

    private static bool SameRate(RateSlice left, RateSlice right) =>
        left.Basis == right.Basis
        && left.RatePhp == right.RatePhp
        && left.OtAfterHoursPerDay == right.OtAfterHoursPerDay
        && left.OtMultiplier == right.OtMultiplier;

    private void RecordSnapshotChanges(
        Domain.Staff staff,
        PayBasis basis,
        decimal rate,
        decimal? otAfterHours,
        decimal? otMultiplier,
        Guid? actorStaffId)
    {
        hr.Record(
            actorStaffId,
            staff.Id,
            HrChangeField.StaffPayBasis,
            staff.PayBasis?.ToString(),
            basis.ToString());
        hr.RecordMoney(actorStaffId, staff.Id, HrChangeField.StaffRate, staff.RatePhp, rate);
        hr.RecordMoney(
            actorStaffId, staff.Id, HrChangeField.StaffOtAfterHours, staff.OtAfterHoursPerDay, otAfterHours);
        hr.RecordMoney(
            actorStaffId, staff.Id, HrChangeField.StaffOtMultiplier, staff.OtMultiplier, otMultiplier);
    }

    // Tracked on purpose: SetRateAsync refreshes the current snapshot on this row.
    private async Task<Domain.Staff?> LoadStaffAsync(Guid staffId, CancellationToken cancellationToken) =>
        await db.Staff
            .Include(s => s.Profile)
            .Include(s => s.Branch)
            .FirstOrDefaultAsync(s => s.Id == staffId, cancellationToken);

    private async Task<IReadOnlyList<StaffPayRate>> HistoryRowsAsync(
        Guid staffId,
        CancellationToken cancellationToken) =>
        await db.StaffPayRates.AsNoTracking()
            .Include(r => r.CreatedByStaff)
            .ThenInclude(s => s!.Profile)
            .Where(r => r.StaffId == staffId)
            .OrderBy(r => r.EffectiveFrom)
            .ToListAsync(cancellationToken);

    private static PayrollRatesResponse ToResponse(
        Domain.Staff staff,
        IReadOnlyList<StaffPayRate> history) =>
        new(
            staff.Id,
            staff.Profile.DisplayName,
            staff.BranchId,
            staff.Branch?.Name,
            staff.PayBasis,
            staff.RatePhp,
            staff.OtAfterHoursPerDay,
            staff.OtMultiplier,
            history.Select(row => new PayrollRateHistoryRowDto(
                row.Id,
                row.EffectiveFrom,
                row.Basis,
                row.RatePhp,
                row.OtAfterHoursPerDay,
                row.OtMultiplier,
                row.CreatedAtUtc,
                row.CreatedByStaff?.Profile.DisplayName,
                row.Reason)).ToList());

    private static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
