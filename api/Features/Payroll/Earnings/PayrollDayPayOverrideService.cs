using System.Globalization;
using HrCore.Features.HrChangeLog;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Payroll;

public sealed partial class PayrollService
{
    /// <summary>#390: Owner/Admin only — one place for the day-pay permission check.</summary>
    public static bool CanManageDayPayOverride(StaffRole? role) =>
        role is StaffRole.Owner or StaffRole.Admin;

    private async Task<Dictionary<Guid, List<PayrollDayPayOverride>>> LoadDayPayOverridesByStaffAsync(
        IReadOnlyList<Guid> staffIds,
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken)
    {
        if (staffIds.Count == 0)
        {
            return new Dictionary<Guid, List<PayrollDayPayOverride>>();
        }

        var rows = await db.PayrollDayPayOverrides.AsNoTracking()
            .Where(row => staffIds.Contains(row.StaffId) && row.WorkDate >= start && row.WorkDate <= end)
            .OrderBy(row => row.WorkDate)
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(row => row.StaffId)
            .ToDictionary(group => group.Key, group => group.ToList());
    }

    private static PayrollDayPayOverrideDto ToDayPayOverrideDto(PayrollDayPayOverride row) =>
        new(
            row.Id,
            row.StaffId,
            row.WorkDate,
            row.Mode,
            row.CustomAmount,
            row.Reason,
            row.SetByStaffId,
            row.SetAtUtc);

    public async Task<PayrollDayPayOverrideSaveResult> SaveDayPayOverrideAsync(
        IReadOnlyList<Branch> shops,
        SavePayrollDayPayOverrideRequest request,
        bool includeUnscopedOwners,
        Guid? actorStaffId,
        StaffRole? actorRole,
        CancellationToken cancellationToken)
    {
        if (!CanManageDayPayOverride(actorRole))
        {
            return PayrollDayPayOverrideSaveResult.Forbidden(
                "Only an Owner or Admin can override a day's pay.");
        }

        var clearing = request.Mode is null;
        string? reason = null;
        decimal? customAmount = null;
        if (!clearing)
        {
            reason = request.Reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                return PayrollDayPayOverrideSaveResult.Bad("A reason is required.");
            }

            if (reason.Length > 500)
            {
                return PayrollDayPayOverrideSaveResult.Bad("Reason must be 500 characters or fewer.");
            }

            if (request.Mode == PayrollDayPayMode.Custom)
            {
                if (request.CustomAmount is null)
                {
                    return PayrollDayPayOverrideSaveResult.Bad("Custom amount is required for a custom day override.");
                }

                if (request.CustomAmount < 0)
                {
                    return PayrollDayPayOverrideSaveResult.Bad("Custom amount cannot be negative.");
                }

                customAmount = RoundMoney(request.CustomAmount.Value);
            }
            else if (request.CustomAmount is not null)
            {
                return PayrollDayPayOverrideSaveResult.Bad(
                    "Custom amount is only allowed when mode is custom.");
            }
        }

        var staff = await db.Staff
            .Include(row => row.Profile)
            .Include(row => row.Branch)
            .FirstOrDefaultAsync(row => row.Id == request.StaffId, cancellationToken);
        if (staff is null || !IsInRegisterScope(staff, shops, includeUnscopedOwners))
        {
            return PayrollDayPayOverrideSaveResult.NotFound();
        }

        if (!clearing && request.Mode == PayrollDayPayMode.Custom && customAmount is decimal custom)
        {
            var slices = await RateSlicesByStaffAsync([staff], request.WorkDate, request.WorkDate, cancellationToken);
            var slice = slices.GetValueOrDefault(staff.Id)?.FirstOrDefault();
            if (slice is not null)
            {
                var normal = NormalDayPay(slice, regularHours: 0m);
                if (custom > normal + 0.009m)
                {
                    return PayrollDayPayOverrideSaveResult.Bad(
                        $"Custom amount cannot exceed that day's normal pay ({normal:0.00}).");
                }
            }
        }

        // Lock check uses the Manila period that contains the work date (same week/semimonth window
        // the register uses when from/to are omitted around that day).
        var (periodStart, periodEnd, _, _) = ResolvePeriod(request.WorkDate, request.WorkDate);
        if (await PayrollRunService.IsPeriodLockedAsync(db, staff.Id, periodStart, periodEnd, cancellationToken))
        {
            return PayrollDayPayOverrideSaveResult.Locked(PayrollRunService.LockedPeriodMessage);
        }

        var existing = await db.PayrollDayPayOverrides.FirstOrDefaultAsync(
            row => row.StaffId == staff.Id && row.WorkDate == request.WorkDate,
            cancellationToken);
        var hr = new HrChangeLogService(db);
        var refType = request.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (clearing)
        {
            if (existing is null)
            {
                return PayrollDayPayOverrideSaveResult.Removed(
                    new PayrollDayPayOverrideClearedDto(staff.Id, request.WorkDate, true));
            }

            var before = FormatDayOverride(existing.Mode, existing.CustomAmount);
            db.PayrollDayPayOverrides.Remove(existing);
            hr.Record(
                actorStaffId,
                staff.Id,
                HrChangeField.PayrollDayOverride,
                before,
                null,
                periodStart,
                periodEnd,
                refType: refType);
            await db.SaveChangesAsync(cancellationToken);
            return PayrollDayPayOverrideSaveResult.Removed(
                new PayrollDayPayOverrideClearedDto(staff.Id, request.WorkDate, true));
        }

        var mode = request.Mode!.Value;
        var beforeValue = existing is null ? null : FormatDayOverride(existing.Mode, existing.CustomAmount);
        if (existing is null)
        {
            existing = new PayrollDayPayOverride
            {
                Id = Guid.NewGuid(),
                StaffId = staff.Id,
                WorkDate = request.WorkDate
            };
            db.PayrollDayPayOverrides.Add(existing);
        }

        existing.Mode = mode;
        existing.CustomAmount = customAmount;
        existing.Reason = reason!;
        existing.SetByStaffId = actorStaffId;
        existing.SetAtUtc = DateTime.UtcNow;
        hr.Record(
            actorStaffId,
            staff.Id,
            HrChangeField.PayrollDayOverride,
            beforeValue,
            FormatDayOverride(mode, customAmount) + " | " + reason,
            periodStart,
            periodEnd,
            refType: refType);
        await db.SaveChangesAsync(cancellationToken);
        return PayrollDayPayOverrideSaveResult.Ok(ToDayPayOverrideDto(existing));
    }

    private static string FormatDayOverride(PayrollDayPayMode mode, decimal? customAmount) =>
        mode == PayrollDayPayMode.Custom
            ? $"custom:{customAmount?.ToString("0.00", CultureInfo.InvariantCulture)}"
            : mode.ToString().ToLowerInvariant();
}
