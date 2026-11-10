using System.Text.Json.Serialization;
using HrCore.Features.HrChangeLog;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Payroll;

public sealed record PayoutHoldDto(
    Guid Id,
    Guid StaffId,
    string StaffName,
    string Reason,
    DateTime HeldAtUtc,
    string? HeldByName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateOnly? ReleasedOn,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ReleasedByName,
    bool Active);

public sealed record PayoutHoldListResponse(IReadOnlyList<PayoutHoldDto> Holds);

public sealed record PayoutHoldWriteResult(
    PayoutHoldDto? Saved,
    Dictionary<string, string[]>? Errors,
    string? Error,
    int Status)
{
    public static PayoutHoldWriteResult Ok(PayoutHoldDto saved) => new(saved, null, null, StatusCodes.Status200OK);

    public static PayoutHoldWriteResult Bad(Dictionary<string, string[]> errors) =>
        new(null, errors, null, StatusCodes.Status400BadRequest);

    public static PayoutHoldWriteResult Missing(string error) =>
        new(null, null, error, StatusCodes.Status404NotFound);

    public static PayoutHoldWriteResult Conflict(string error) =>
        new(null, null, error, StatusCodes.Status409Conflict);
}

public sealed partial class PayrollService
{
    public const string PayoutHoldRefType = "payout_hold";

    public static string ReleasedPayoutLog(DateOnly date) =>
        $"released {date:yyyy-MM-dd}";

    public async Task<PayoutHoldListResponse> ListPayoutHoldsAsync(
        IReadOnlyList<Branch> shops,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        var staffIds = await ShopStaffIdsAsync(shops, includeUnscopedOwners, cancellationToken);
        if (staffIds.Count == 0)
        {
            return new PayoutHoldListResponse([]);
        }

        var rows = await db.StaffPayoutHolds().AsNoTracking()
            .Include(hold => hold.Staff)
            .ThenInclude(staff => staff.Profile)
            .Include(hold => hold.HeldByStaff)
            .ThenInclude(staff => staff!.Profile)
            .Where(hold => staffIds.Contains(hold.StaffId) && hold.ReleasedOn == null)
            .OrderBy(hold => hold.Staff.Profile.DisplayName)
            .ToListAsync(cancellationToken);
        return new PayoutHoldListResponse(rows.Select(ToPayoutHoldDto).ToList());
    }

    public async Task<PayoutHoldWriteResult> SetPayoutHoldAsync(
        IReadOnlyList<Branch> shops,
        Guid staffId,
        string? reason,
        bool includeUnscopedOwners,
        Guid actorStaffId,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeHoldReason(reason, out var normalized, out var errors))
        {
            return PayoutHoldWriteResult.Bad(errors!);
        }

        var staff = await db.Staff
            .Include(person => person.Profile)
            .FirstOrDefaultAsync(person => person.Id == staffId, cancellationToken);
        if (staff is null || !IsInRegisterScope(staff, shops, includeUnscopedOwners))
        {
            return PayoutHoldWriteResult.Missing("That person is not on this shop scope.");
        }

        var hold = await db.StaffPayoutHolds()
            .FirstOrDefaultAsync(row => row.StaffId == staff.Id, cancellationToken);
        if (hold is { ReleasedOn: null } && hold.Reason == normalized)
        {
            return PayoutHoldWriteResult.Ok(await LoadPayoutHoldAsync(hold.Id, cancellationToken));
        }

        string? previous = null;
        if (hold is not null)
        {
            previous = hold.ReleasedOn is DateOnly released
                ? ReleasedPayoutLog(released)
                : hold.Reason;
        }
        else
        {
            hold = new StaffPayoutHold
            {
                Id = Guid.NewGuid(),
                StaffId = staff.Id
            };
            db.StaffPayoutHolds().Add(hold);
        }

        hold.Reason = normalized;
        hold.HeldAtUtc = DateTime.UtcNow;
        hold.HeldByStaffId = actorStaffId;
        hold.ReleasedOn = null;
        hold.ReleasedByStaffId = null;
        new HrChangeLogService(db).Record(
            actorStaffId,
            staff.Id,
            HrChangeField.PayrollPayoutHold,
            previous,
            normalized,
            refType: PayoutHoldRefType,
            refId: hold.Id);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return PayoutHoldWriteResult.Conflict("This person already has a payout hold.");
        }

        return PayoutHoldWriteResult.Ok(await LoadPayoutHoldAsync(hold.Id, cancellationToken));
    }

    public async Task<PayoutHoldWriteResult> ReleasePayoutHoldAsync(
        IReadOnlyList<Branch> shops,
        Guid staffId,
        bool includeUnscopedOwners,
        Guid actorStaffId,
        CancellationToken cancellationToken)
    {
        var staff = await db.Staff.AsNoTracking()
            .FirstOrDefaultAsync(person => person.Id == staffId, cancellationToken);
        if (staff is null || !IsInRegisterScope(staff, shops, includeUnscopedOwners))
        {
            return PayoutHoldWriteResult.Missing("That person is not on this shop scope.");
        }

        var hold = await db.StaffPayoutHolds()
            .FirstOrDefaultAsync(row => row.StaffId == staff.Id && row.ReleasedOn == null, cancellationToken);
        if (hold is null)
        {
            return PayoutHoldWriteResult.Missing("This person is not on hold.");
        }

        var releasedOn = ManilaTime.Today();
        hold.ReleasedOn = releasedOn;
        hold.ReleasedByStaffId = actorStaffId;
        var lines = await db.PayrollRunLines
            .Where(line => line.StaffId == staff.Id && line.PayoutStatus == PayoutHoldStatus.Held)
            .ToListAsync(cancellationToken);
        foreach (var line in lines)
        {
            line.PayoutStatus = PayoutHoldStatus.Released;
            line.ReleasedOn = releasedOn;
            line.ReleasedByStaffId = actorStaffId;
        }

        new HrChangeLogService(db).Record(
            actorStaffId,
            staff.Id,
            HrChangeField.PayrollPayoutHold,
            hold.Reason,
            ReleasedPayoutLog(releasedOn),
            refType: PayoutHoldRefType,
            refId: hold.Id);
        await db.SaveChangesAsync(cancellationToken);
        return PayoutHoldWriteResult.Ok(await LoadPayoutHoldAsync(hold.Id, cancellationToken));
    }

    /// <summary>
    /// Copies an active hold onto the lines of a run being locked. Amounts are left alone.
    /// No query when the flag is off, so the hold table stays out of the model.
    /// </summary>
    internal async Task StampPayoutHoldsAsync(
        IReadOnlyCollection<PayrollRunLine> lines,
        CancellationToken cancellationToken)
    {
        if (!features.PayoutHold || lines.Count == 0)
        {
            return;
        }

        var staffIds = lines.Select(line => line.StaffId).Distinct().ToList();
        var holds = await db.StaffPayoutHolds().AsNoTracking()
            .Where(hold => staffIds.Contains(hold.StaffId) && hold.ReleasedOn == null)
            .ToListAsync(cancellationToken);
        var byStaff = holds.ToDictionary(hold => hold.StaffId);
        foreach (var line in lines)
        {
            if (!byStaff.TryGetValue(line.StaffId, out var hold))
            {
                continue;
            }

            line.PayoutStatus = PayoutHoldStatus.Held;
            line.HoldReason = hold.Reason;
        }
    }

    internal async Task<Dictionary<Guid, string>> ActivePayoutHoldReasonsAsync(
        IReadOnlyCollection<Guid> staffIds,
        CancellationToken cancellationToken)
    {
        if (!features.PayoutHold || staffIds.Count == 0)
        {
            return [];
        }

        var rows = await db.StaffPayoutHolds().AsNoTracking()
            .Where(hold => staffIds.Contains(hold.StaffId) && hold.ReleasedOn == null)
            .Select(hold => new { hold.StaffId, hold.Reason })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.StaffId, row => row.Reason);
    }

    internal async Task<Dictionary<Guid, string>> PayoutReleasedByNamesAsync(
        IEnumerable<Guid?> ids,
        CancellationToken cancellationToken)
    {
        var wanted = ids.OfType<Guid>().Distinct().ToList();
        if (!features.PayoutHold || wanted.Count == 0)
        {
            return [];
        }

        return await db.Staff.AsNoTracking()
            .Where(staff => wanted.Contains(staff.Id))
            .Select(staff => new { staff.Id, staff.Profile.DisplayName })
            .ToDictionaryAsync(row => row.Id, row => row.DisplayName, cancellationToken);
    }

    internal async Task<Dictionary<Guid, decimal>> PayoutHeldTotalsAsync(
        IReadOnlyCollection<Guid> runIds,
        CancellationToken cancellationToken)
    {
        if (!features.PayoutHold || runIds.Count == 0)
        {
            return [];
        }

        return await db.PayrollRunLines.AsNoTracking()
            .Where(line => runIds.Contains(line.RunId) && line.PayoutStatus == PayoutHoldStatus.Held)
            .GroupBy(line => line.RunId)
            .Select(group => new { group.Key, Total = group.Sum(line => line.Net) })
            .ToDictionaryAsync(row => row.Key, row => row.Total, cancellationToken);
    }

    internal static decimal? SumHeldNet(IEnumerable<PayrollStaffRowDto> rows)
    {
        decimal? total = null;
        foreach (var row in rows)
        {
            if (row.PayoutStatus == PayoutHoldStatus.Held)
            {
                total = (total ?? 0m) + row.Net;
            }
        }

        return total;
    }

    internal static bool TryNormalizeHoldReason(
        string? reason,
        out string normalized,
        out Dictionary<string, string[]>? errors)
    {
        normalized = (reason ?? "").Trim();
        if (normalized.Length == 0)
        {
            errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["reason"] = ["A reason is required."]
            };
            return false;
        }

        if (normalized.Length > StaffPayoutHold.ReasonMaxLength)
        {
            errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["reason"] = [$"Reason must be {StaffPayoutHold.ReasonMaxLength} characters or fewer."]
            };
            return false;
        }

        errors = null;
        return true;
    }

    private async Task<List<Guid>> ShopStaffIdsAsync(
        IReadOnlyList<Branch> shops,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        var shopIds = shops.Select(shop => shop.Id).ToList();
        var query = db.Staff.AsNoTracking().AsQueryable();
        query = includeUnscopedOwners
            ? query.Where(person =>
                (person.BranchId != null && shopIds.Contains(person.BranchId.Value)) ||
                (person.BranchId == null && person.Role == StaffRole.Owner))
            : query.Where(person => person.BranchId != null && shopIds.Contains(person.BranchId.Value));
        return await query.Select(person => person.Id).ToListAsync(cancellationToken);
    }

    private async Task<PayoutHoldDto> LoadPayoutHoldAsync(Guid id, CancellationToken cancellationToken)
    {
        var hold = await db.StaffPayoutHolds().AsNoTracking()
            .Include(row => row.Staff)
            .ThenInclude(staff => staff.Profile)
            .Include(row => row.HeldByStaff)
            .ThenInclude(staff => staff!.Profile)
            .Include(row => row.ReleasedByStaff)
            .ThenInclude(staff => staff!.Profile)
            .FirstAsync(row => row.Id == id, cancellationToken);
        return ToPayoutHoldDto(hold);
    }

    private static PayoutHoldDto ToPayoutHoldDto(StaffPayoutHold hold) =>
        new(
            hold.Id,
            hold.StaffId,
            hold.Staff.Profile.DisplayName,
            hold.Reason,
            hold.HeldAtUtc,
            hold.HeldByStaff?.Profile.DisplayName,
            hold.ReleasedOn,
            hold.ReleasedByStaff?.Profile.DisplayName,
            hold.ReleasedOn is null);
}
