using HrCore.Features.HrChangeLog;
using HrCore.Features.Payroll;
using HrCore.Features.Staff;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Attendance;

public sealed record OvertimeRowDto(
    Guid Id,
    Guid StaffId,
    string StaffName,
    DateOnly WorkDate,
    OvertimeKind Kind,
    Guid? PunchId,
    decimal ClaimedHours,
    decimal? ApprovedHours,
    OvertimeStatus Status,
    string? Reason,
    string? DecisionNote,
    string? DecidedByName,
    DateTime? DecidedAtUtc,
    bool CanApprove);

public sealed record OvertimeListResponse(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<OvertimeRowDto> Rows);

public sealed record SyncPunchClaimsResult(
    int Scanned,
    int Created,
    int Updated,
    int Unchanged);

public sealed record BackfillPunchClaimsResult(
    int Scanned,
    int Created,
    int Updated,
    int Unchanged)
{
    public int Total => Scanned;
}

public sealed class FileOvertimeRequest
{
    public DateOnly WorkDate { get; set; }
    public decimal Hours { get; set; }
    public string? Reason { get; set; }
}

public sealed class DecideOvertimeRequest
{
    public decimal? Hours { get; set; }
    public string? Note { get; set; }
}

public sealed class OvertimeService(IHrDb db, HrChangeLogService hr)
{
    public const string OwnerOnlyMessage = "Only the owner can approve overtime.";
    public const string CannotApproveMessage = "You can't approve overtime.";

    public async Task<OvertimeListResponse> ListAsync(
        Domain.Staff actor,
        IReadOnlyList<Branch>? shops,
        DateOnly? from,
        DateOnly? to,
        bool mine,
        CancellationToken cancellationToken)
    {
        var (start, end) = Range(from, to);
        if (mine)
        {
            if (actor.Role == StaffRole.Owner)
            {
                return new OvertimeListResponse(start, end, []);
            }

            var ownRows = await LoadRowsAsync(actor, [actor.Id], start, end, mine: true, cancellationToken);
            return new OvertimeListResponse(start, end, ownRows);
        }

        var staffIds = await VisibleStaffIdsAsync(actor, shops, cancellationToken);
        var rows = await LoadRowsAsync(actor, staffIds, start, end, mine: false, cancellationToken);
        return new OvertimeListResponse(start, end, rows);
    }

    public async Task<OvertimeRowDto> FileAsync(
        Domain.Staff actor,
        FileOvertimeRequest request,
        CancellationToken cancellationToken)
    {
        if (actor.Role == StaffRole.Owner)
        {
            throw new AttendanceBadRequestException("The owner does not file overtime.");
        }

        if (actor.BranchId is not Guid branchId)
        {
            throw new AttendanceBadRequestException("This person is not on a shop.");
        }

        var hours = OvertimeMath.RoundHours(request.Hours);
        if (hours <= 0m || hours > 24m)
        {
            throw new AttendanceBadRequestException("Overtime hours must be greater than 0 and at most 24.");
        }

        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new AttendanceBadRequestException("Say what the overtime is for.");
        }

        if (reason.Length > 500)
        {
            throw new AttendanceBadRequestException("The reason must be 500 characters or fewer.");
        }

        var open = await db.OvertimeEntries.AnyAsync(
            entry => entry.StaffId == actor.Id
                && entry.WorkDate == request.WorkDate
                && entry.Kind == OvertimeKind.Request
                && entry.Status != OvertimeStatus.Rejected,
            cancellationToken);
        if (open)
        {
            throw new InvalidOperationException("There's already an overtime request for that day.");
        }

        var entry = new OvertimeEntry
        {
            Id = Guid.NewGuid(),
            StaffId = actor.Id,
            BranchId = branchId,
            WorkDate = request.WorkDate,
            Kind = OvertimeKind.Request,
            ClaimedHours = hours,
            Status = OvertimeStatus.Pending,
            Reason = reason,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.OvertimeEntries.Add(entry);
        hr.Record(
            actor.Id,
            actor.Id,
            HrChangeField.OvertimeRequest,
            null,
            $"{hours:0.00} {reason}",
            refType: "overtime",
            refId: entry.Id);
        await db.SaveChangesAsync(cancellationToken);
        return await MapOneAsync(actor, entry.Id, cancellationToken);
    }

    public async Task<OvertimeRowDto> DecideAsync(
        Domain.Staff actor,
        IReadOnlyList<Branch>? shops,
        Guid id,
        OvertimeStatus decision,
        DecideOvertimeRequest? request,
        CancellationToken cancellationToken)
    {
        if (decision is not OvertimeStatus.Approved and not OvertimeStatus.Rejected)
        {
            throw new AttendanceBadRequestException("Choose approve or reject.");
        }

        if (!HrRoles.IsDesk(actor.Role))
        {
            throw new AttendanceForbiddenException(CannotApproveMessage);
        }

        var entry = await db.OvertimeEntries
            .Include(row => row.Branch)
            .FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (entry is null || !InScope(actor, shops, entry))
        {
            throw new KeyNotFoundException("Overtime not found.");
        }

        if (actor.Role == StaffRole.Admin && !entry.Branch.AdminsCanApproveOt)
        {
            throw new AttendanceForbiddenException(OwnerOnlyMessage);
        }

        if (actor.Role != StaffRole.Owner && actor.Role != StaffRole.Admin)
        {
            throw new AttendanceForbiddenException(CannotApproveMessage);
        }

        if (entry.Status != OvertimeStatus.Pending)
        {
            throw new InvalidOperationException("This overtime was already decided.");
        }

        var note = BlankToNull(request?.Note);
        if (note is { Length: > 500 })
        {
            throw new AttendanceBadRequestException("The note must be 500 characters or fewer.");
        }

        decimal? approvedHours = null;
        if (decision == OvertimeStatus.Approved)
        {
            approvedHours = request?.Hours is decimal edited
                ? OvertimeMath.RoundHours(edited)
                : entry.ClaimedHours;
            if (approvedHours <= 0m)
            {
                throw new AttendanceBadRequestException("Enter the hours to approve.");
            }

            if (approvedHours > entry.ClaimedHours)
            {
                throw new AttendanceBadRequestException("Approved hours can't be more than the claimed hours.");
            }
        }

        entry.Status = decision;
        entry.ApprovedHours = approvedHours;
        entry.DecisionNote = note;
        entry.DecidedByStaffId = actor.Id;
        entry.DecidedAtUtc = DateTime.UtcNow;
        hr.Record(
            actor.Id,
            entry.StaffId,
            HrChangeField.OvertimeDecision,
            OvertimeStatus.Pending.ToString(),
            decision == OvertimeStatus.Approved
                ? $"approved {approvedHours:0.00}"
                : "rejected",
            refType: "overtime",
            refId: entry.Id);
        await db.SaveChangesAsync(cancellationToken);
        return await MapOneAsync(actor, entry.Id, cancellationToken);
    }

    public async Task<BackfillPunchClaimsResult> BackfillPunchClaimsAsync(
        Domain.Staff actor,
        CancellationToken cancellationToken = default)
    {
        if (actor.Role != StaffRole.Owner)
        {
            throw new AttendanceForbiddenException(OwnerOnlyMessage);
        }

        var punches = await db.AttendancePunches
            .Where(punch => punch.OutAtUtc != null && punch.ApprovalStatus == PunchApprovalStatus.Approved)
            .ToListAsync(cancellationToken);

        var result = await SyncPunchClaimsForPunchesAsync(punches, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new BackfillPunchClaimsResult(result.Scanned, result.Created, result.Updated, result.Unchanged);
    }

    public async Task<SyncPunchClaimsResult> SyncPunchClaimsForPunchesAsync(
        IReadOnlyList<Guid> punchIds,
        CancellationToken cancellationToken = default)
    {
        if (punchIds.Count == 0)
        {
            return new SyncPunchClaimsResult(0, 0, 0, 0);
        }

        var punches = await db.AttendancePunches
            .Where(punch => punchIds.Contains(punch.Id))
            .ToListAsync(cancellationToken);

        return await SyncPunchClaimsForPunchesAsync(punches, cancellationToken);
    }

    public async Task<SyncPunchClaimsResult> SyncPunchClaimsForPunchesAsync(
        IReadOnlyList<AttendancePunch> punches,
        CancellationToken cancellationToken = default)
    {
        if (punches.Count == 0)
        {
            return new SyncPunchClaimsResult(0, 0, 0, 0);
        }

        var branchIds = punches.Select(punch => punch.BranchId).Distinct().ToList();
        var branches = await db.Branches
            .Where(branch => branchIds.Contains(branch.Id))
            .ToDictionaryAsync(branch => branch.Id, cancellationToken);

        var punchIds = punches.Select(punch => punch.Id).ToList();
        var existing = await db.OvertimeEntries
            .Where(entry => entry.PunchId != null && punchIds.Contains(entry.PunchId.Value))
            .ToListAsync(cancellationToken);

        var localEntries = db.OvertimeEntries.Local
            .Where(entry => entry.PunchId != null && punchIds.Contains(entry.PunchId.Value));

        var byPunch = existing.Union(localEntries)
            .DistinctBy(entry => entry.PunchId!.Value)
            .ToDictionary(entry => entry.PunchId!.Value);

        int created = 0;
        int updated = 0;
        int unchanged = 0;

        foreach (var punch in punches)
        {
            byPunch.TryGetValue(punch.Id, out var claim);

            // Decided claims (Approved or Rejected) must NEVER be changed.
            if (claim is { Status: OvertimeStatus.Approved or OvertimeStatus.Rejected })
            {
                unchanged++;
                continue;
            }

            if (punch.ApprovalStatus != PunchApprovalStatus.Approved || punch.OutAtUtc is not DateTime outAt)
            {
                if (claim is { Status: OvertimeStatus.Pending })
                {
                    db.OvertimeEntries.Remove(claim);
                    updated++;
                }
                else
                {
                    unchanged++;
                }

                continue;
            }

            if (!branches.TryGetValue(punch.BranchId, out var branch))
            {
                unchanged++;
                continue;
            }

            var claimed = OvertimeMath.SplitPunch(
                punch.InAtUtc,
                outAt,
                branch.ShiftEnd,
                branch.OtGraceMinutes).ClaimedHours;

            if (claimed <= 0m)
            {
                if (claim is { Status: OvertimeStatus.Pending })
                {
                    db.OvertimeEntries.Remove(claim);
                    updated++;
                }
                else
                {
                    unchanged++;
                }

                continue;
            }

            var workDate = ManilaTime.ToManilaDate(punch.InAtUtc);

            if (claim is null)
            {
                var entry = new OvertimeEntry
                {
                    Id = Guid.NewGuid(),
                    StaffId = punch.StaffId,
                    BranchId = punch.BranchId,
                    WorkDate = workDate,
                    Kind = OvertimeKind.Punch,
                    PunchId = punch.Id,
                    ClaimedHours = claimed,
                    Status = OvertimeStatus.Pending,
                    CreatedAtUtc = DateTime.UtcNow
                };
                db.OvertimeEntries.Add(entry);
                byPunch[punch.Id] = entry;
                created++;
            }
            else if (claim.Status == OvertimeStatus.Pending)
            {
                if (claim.ClaimedHours != claimed || claim.WorkDate != workDate || claim.BranchId != punch.BranchId)
                {
                    claim.ClaimedHours = claimed;
                    claim.WorkDate = workDate;
                    claim.BranchId = punch.BranchId;
                    updated++;
                }
                else
                {
                    unchanged++;
                }
            }
        }

        return new SyncPunchClaimsResult(punches.Count, created, updated, unchanged);
    }

    private async Task<IReadOnlyList<Guid>> VisibleStaffIdsAsync(
        Domain.Staff actor,
        IReadOnlyList<Branch>? shops,
        CancellationToken cancellationToken)
    {
        if (!HrRoles.IsDesk(actor.Role))
        {
            return [actor.Id];
        }

        var shopIds = shops?.Select(shop => shop.Id).ToList() ?? [];
        if (shopIds.Count == 0)
        {
            return [];
        }

        return await db.Staff.AsNoTracking()
            .Where(staff => staff.BranchId != null && shopIds.Contains(staff.BranchId.Value))
            .Select(staff => staff.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<OvertimeRowDto>> LoadRowsAsync(
        Domain.Staff actor,
        IReadOnlyList<Guid> staffIds,
        DateOnly start,
        DateOnly end,
        bool mine,
        CancellationToken cancellationToken)
    {
        if (staffIds.Count == 0)
        {
            return [];
        }

        var rows = await db.OvertimeEntries.AsNoTracking()
            .Include(entry => entry.Staff).ThenInclude(staff => staff.Profile)
            .Include(entry => entry.Branch)
            .Include(entry => entry.DecidedByStaff).ThenInclude(staff => staff!.Profile)
            .Where(entry => staffIds.Contains(entry.StaffId)
                && entry.WorkDate >= start
                && entry.WorkDate <= end)
            .OrderBy(entry => entry.WorkDate)
            .ThenBy(entry => entry.Staff.Profile.DisplayName)
            .ToListAsync(cancellationToken);
        return rows.Select(entry => Map(actor, entry, mine)).ToList();
    }

    private async Task<OvertimeRowDto> MapOneAsync(
        Domain.Staff actor,
        Guid id,
        CancellationToken cancellationToken)
    {
        var entry = await db.OvertimeEntries.AsNoTracking()
            .Include(row => row.Staff).ThenInclude(staff => staff.Profile)
            .Include(row => row.Branch)
            .Include(row => row.DecidedByStaff).ThenInclude(staff => staff!.Profile)
            .FirstAsync(row => row.Id == id, cancellationToken);
        return Map(actor, entry);
    }

    private static OvertimeRowDto Map(Domain.Staff actor, OvertimeEntry entry, bool mine = false) => new(
        entry.Id,
        entry.StaffId,
        entry.Staff.Profile.DisplayName,
        entry.WorkDate,
        entry.Kind,
        entry.PunchId,
        entry.ClaimedHours,
        entry.ApprovedHours,
        entry.Status,
        entry.Reason,
        entry.DecisionNote,
        entry.DecidedByStaff?.Profile.DisplayName,
        entry.DecidedAtUtc,
        !mine && CanApprove(actor, entry.Branch) && entry.Status == OvertimeStatus.Pending);

    private static bool CanApprove(Domain.Staff actor, Branch branch) =>
        actor.Role == StaffRole.Owner
        || (actor.Role == StaffRole.Admin && branch.AdminsCanApproveOt);

    private static bool InScope(Domain.Staff actor, IReadOnlyList<Branch>? shops, OvertimeEntry entry)
    {
        if (!HrRoles.IsDesk(actor.Role))
        {
            return entry.StaffId == actor.Id;
        }

        return shops?.Any(shop => shop.Id == entry.BranchId) == true;
    }

    private static (DateOnly Start, DateOnly End) Range(DateOnly? from, DateOnly? to)
    {
        var (weekFrom, weekTo) = ManilaTime.CurrentWeek();
        var start = from ?? weekFrom;
        var end = to ?? weekTo;
        if (start > end)
        {
            (start, end) = (end, start);
        }

        return (start, end);
    }

    private static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
