using System.Globalization;
using System.Text;
using HrCore.Features.Payroll;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HrCore.Features.Attendance;

public sealed class AttendanceService(IHrDb db, IFileStore files, OvertimeService overtime)
{
    public const string OverlapMessage = "This overlaps another punch for this person.";
    public const string AlreadyClockedInMessage = "Already clocked in.";
    public const string NotSetToClockInMessage =
        "You're not set up to clock in. Ask the owner or admin if that's wrong.";

    public async Task<AttendancePunchDto> ClockInAsync(
        Domain.Staff staff,
        Guid branchId,
        ClockInRequest? gps,
        IFormFile? photo,
        CancellationToken cancellationToken)
    {
        if (!staff.MustClock)
        {
            throw new AttendanceBadRequestException(NotSetToClockInMessage);
        }

        var branch = await db.Branches.AsNoTracking()
            .FirstAsync(b => b.Id == branchId, cancellationToken);
        var (status, reason) = ResolveSelfPunch(
            branch,
            gps?.Latitude,
            gps?.Longitude,
            gps?.OffsiteReason,
            "Turn on location to clock in from your phone.",
            "clock-in");

        return await OpenShiftAsync(
            staff,
            branch,
            AttendancePunch.SourceSelf,
            DateTime.UtcNow,
            gps?.Latitude,
            gps?.Longitude,
            photo,
            self: true,
            cancellationToken,
            status,
            reason);
    }

    public async Task<AttendancePunchDto> ClockOutAsync(
        Domain.Staff staff,
        ClockInRequest? gps,
        IFormFile? photo,
        CancellationToken cancellationToken)
    {
        var open = await FindOpenAsync(staff.Id, cancellationToken);
        if (open is null)
        {
            throw new InvalidOperationException("You're not clocked in.");
        }

        var branch = await db.Branches.AsNoTracking()
            .FirstAsync(b => b.Id == open.BranchId, cancellationToken);
        var (status, reason) = ResolveSelfPunch(
            branch,
            gps?.Latitude,
            gps?.Longitude,
            gps?.OffsiteReason,
            "Turn on location to clock out from your phone.",
            "clock-out");
        EnsurePhoto(branch.RequireClockPhoto, photo, "A photo is required to clock out.");
        if (photo is { Length: > 0 })
        {
            await StorePhotoAsync((AttendancePunch)open, photo, clockIn: false, cancellationToken);
        }

        open.OutAtUtc = DateTime.UtcNow;
        open.OutLatitude = gps?.Latitude;
        open.OutLongitude = gps?.Longitude;
        if (status == PunchApprovalStatus.Pending)
        {
            open.ApprovalStatus = PunchApprovalStatus.Pending;
            open.OffsiteReason = AppendOffsiteReason(open.OffsiteReason, reason);
        }

        await overtime.SyncPunchClaimsForPunchesAsync([open], cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync((AttendancePunch)open, cancellationToken);
    }

    public async Task<IReadOnlyList<KioskStaffDto>> ListKioskAsync(
        Domain.Staff actor,
        Guid branchId,
        CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        var people = await db.Staff.AsNoTracking()
            .Where(s => s.Active
                        && s.BranchId == branchId
                        && s.MustClock
                        && s.Role != StaffRole.Owner)
            .OrderBy(s => s.Profile.DisplayName)
            .Select(s => new { s.Id, Name = s.Profile.DisplayName, s.MustClock })
            .ToListAsync(cancellationToken);

        var ids = people.Select(s => s.Id).ToList();
        var open = await db.AttendancePunches.AsNoTracking()
            .Where(p => ids.Contains(p.StaffId) && p.OutAtUtc == null)
            .Select(p => new { p.Id, p.StaffId, p.InAtUtc })
            .ToListAsync(cancellationToken);
        var byStaff = open.ToDictionary(p => p.StaffId);

        return people.Select(person =>
        {
            byStaff.TryGetValue(person.Id, out var punch);
            return new KioskStaffDto(
                person.Id,
                person.Name,
                person.MustClock,
                punch is not null,
                punch?.Id,
                punch?.InAtUtc,
                punch is null ? null : ManilaTime.FormatPunchLabel(punch.InAtUtc));
        }).ToList();
    }

    public async Task<AttendancePunchDto> KioskClockInAsync(
        Domain.Staff actor,
        Guid branchId,
        Guid staffId,
        IFormFile? photo,
        CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        var target = await RequireClockTargetAsync(branchId, staffId, cancellationToken);
        var branch = await db.Branches.AsNoTracking()
            .FirstAsync(b => b.Id == branchId, cancellationToken);
        return await OpenShiftAsync(
            target,
            branch,
            AttendancePunch.SourceKiosk,
            DateTime.UtcNow,
            null,
            null,
            photo,
            self: false,
            cancellationToken);
    }

    public async Task<AttendancePunchDto> KioskClockOutAsync(
        Domain.Staff actor,
        Guid branchId,
        Guid staffId,
        IFormFile? photo,
        CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        var target = await RequireClockTargetAsync(branchId, staffId, cancellationToken);
        var open = await FindOpenAsync(target.Id, cancellationToken);
        if (open is null || open.BranchId != branchId)
        {
            throw new InvalidOperationException("They're not clocked in.");
        }

        var requirePhoto = await db.Branches.AsNoTracking()
            .Where(b => b.Id == branchId)
            .Select(b => b.RequireClockPhoto)
            .FirstAsync(cancellationToken);
        EnsurePhoto(requirePhoto, photo, "A photo is required to clock out.");
        if (photo is { Length: > 0 })
        {
            await StorePhotoAsync((AttendancePunch)open, photo, clockIn: false, cancellationToken);
        }

        open.OutAtUtc = DateTime.UtcNow;
        await overtime.SyncPunchClaimsForPunchesAsync([open], cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync((AttendancePunch)open, cancellationToken);
    }

    public async Task<AttendancePunchDto> PunchForAsync(
        Domain.Staff actor,
        Guid branchId,
        PunchForRequest request,
        CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        var target = await RequireClockTargetAsync(branchId, request.StaffId, cancellationToken);
        var inAt = RequireInstant(request.InAtUtc, request.InAtManila, "clock-in");
        DateTime? outAt = request.OutAtUtc is null && string.IsNullOrWhiteSpace(request.OutAtManila)
            ? null
            : RequireInstant(request.OutAtUtc, request.OutAtManila, "clock-out");
        if (outAt is DateTime closed && closed < inAt)
        {
            throw new AttendanceBadRequestException("Clock-out must be after clock-in.");
        }

        if (outAt is null)
        {
            var existing = await FindOpenAsync(target.Id, cancellationToken);
            if (existing is not null)
            {
                throw new InvalidOperationException("They're already clocked in.");
            }
        }

        await EnsureUnlockedAsync(target.Id, inAt, outAt ?? inAt, cancellationToken);
        await EnsureNoOverlapAsync(target.Id, inAt, outAt, cancellationToken);

        var punch = new AttendancePunch
        {
            Id = Guid.NewGuid(),
            StaffId = target.Id,
            BranchId = branchId,
            InAtUtc = inAt,
            OutAtUtc = outAt,
            Source = AttendancePunch.SourcePunchFor
        };
        db.AttendancePunches.Add(punch);
        if (outAt is not null)
        {
            await overtime.SyncPunchClaimsForPunchesAsync([punch], cancellationToken);
        }
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsOpenShiftConflict(ex))
        {
            throw new InvalidOperationException(AlreadyClockedInMessage);
        }

        return await ToDtoAsync(punch, cancellationToken);
    }

    public async Task<AttendancePunchDto> CloseAsync(
        Domain.Staff actor,
        Guid branchId,
        Guid punchId,
        ClosePunchRequest request,
        CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        var reason = request.Reason?.Trim() ?? "";
        if (reason.Length == 0)
        {
            throw new AttendanceBadRequestException("A reason is required to close the punch.");
        }

        if (reason.Length > 500)
        {
            throw new AttendanceBadRequestException("Reason must be 500 characters or fewer.");
        }

        var punch = await db.AttendancePunches
            .FirstOrDefaultAsync(p => p.Id == punchId && p.BranchId == branchId, cancellationToken);
        if (punch is null)
        {
            throw new KeyNotFoundException("That punch was not found.");
        }

        if (punch.OutAtUtc is not null)
        {
            throw new InvalidOperationException("That punch is already closed.");
        }

        var outAt = RequireInstant(request.OutAtUtc, request.OutAtManila, "clock-out");
        if (outAt < punch.InAtUtc)
        {
            throw new AttendanceBadRequestException("Clock-out must be after clock-in.");
        }

        await EnsureUnlockedAsync(punch.StaffId, punch.InAtUtc, outAt, cancellationToken);
        var overlapsLater = await db.AttendancePunches.AsNoTracking()
            .AnyAsync(
                p => p.StaffId == punch.StaffId
                     && p.Id != punch.Id
                     && p.InAtUtc > punch.InAtUtc
                     && p.InAtUtc < outAt,
                cancellationToken);
        if (overlapsLater)
        {
            throw new AttendanceBadRequestException(OverlapMessage);
        }

        await ApplyCloseAsync(punch, outAt, reason, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(punch, cancellationToken);
    }

    /// <summary>
    /// Sets the clock-out and syncs overtime claims. Keeps the original source (ops, kiosk, or punch-for);
    /// CloseReason records the manual close. The caller checks the payroll lock and saves.
    /// </summary>
    internal async Task ApplyCloseAsync(AttendancePunch punch, DateTime outAtUtc, string reason, CancellationToken cancellationToken)
    {
        punch.OutAtUtc = outAtUtc;
        punch.CloseReason = reason;
        await overtime.SyncPunchClaimsForPunchesAsync([punch], cancellationToken);
    }

    public async Task<AttendancePunchDto> ReviewAsync(
        Domain.Staff actor,
        Guid branchId,
        Guid punchId,
        PunchApprovalStatus decision,
        string? note,
        CancellationToken cancellationToken)
    {
        EnsureManager(actor);
        if (decision is not PunchApprovalStatus.Approved and not PunchApprovalStatus.Rejected)
        {
            throw new AttendanceBadRequestException("Decision must be approved or rejected.");
        }

        var trimmed = note?.Trim() ?? "";
        if (trimmed.Length > 500)
        {
            throw new AttendanceBadRequestException("Note must be 500 characters or fewer.");
        }

        var punch = await db.AttendancePunches
            .FirstOrDefaultAsync(p => p.Id == punchId && p.BranchId == branchId, cancellationToken);
        if (punch is null)
        {
            throw new KeyNotFoundException("That punch was not found.");
        }

        if (punch.ApprovalStatus != PunchApprovalStatus.Pending)
        {
            throw new InvalidOperationException("That punch is not pending.");
        }

        await EnsureUnlockedAsync(punch.StaffId, punch.InAtUtc, punch.OutAtUtc ?? punch.InAtUtc, cancellationToken);

        punch.ApprovalStatus = decision;
        punch.ReviewedByStaffId = actor.Id;
        punch.ReviewedAtUtc = DateTime.UtcNow;
        punch.ReviewNote = trimmed.Length == 0 ? null : trimmed;
        await overtime.SyncPunchClaimsForPunchesAsync([punch], cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(punch, cancellationToken);
    }

    public async Task<AttendanceMeResponse> GetMeAsync(
        Domain.Staff staff,
        Guid branchId,
        CancellationToken cancellationToken)
    {
        var start = ManilaTime.StartOfTodayUtc();
        var end = ManilaTime.StartOfDayUtc(ManilaTime.Today().AddDays(1));

        var rows = await db.AttendancePunches.AsNoTracking()
            .Where(p => p.StaffId == staff.Id &&
                        (p.OutAtUtc == null || (p.InAtUtc >= start && p.InAtUtc < end)))
            .OrderByDescending(p => p.InAtUtc)
            .Take(20)
            .Select(p => new
            {
                p.Id,
                p.StaffId,
                p.BranchId,
                BranchName = p.Branch.Name,
                p.InAtUtc,
                p.OutAtUtc,
                p.Source,
                p.CloseReason,
                p.InLatitude,
                p.InLongitude,
                p.OutLatitude,
                p.OutLongitude,
                HasInPhoto = p.InPhotoStoragePath != null,
                HasOutPhoto = p.OutPhotoStoragePath != null,
                p.ApprovalStatus,
                p.OffsiteReason,
                p.ReviewNote,
                ReviewedBy = p.ReviewedByStaff != null ? p.ReviewedByStaff.Profile.DisplayName : null,
                p.ReviewedAtUtc
            })
            .ToListAsync(cancellationToken);

        var dtos = rows
            .Select(p => Map(
                p.Id,
                p.StaffId,
                p.BranchId,
                p.BranchName,
                p.InAtUtc,
                p.OutAtUtc,
                p.Source,
                p.HasInPhoto,
                p.HasOutPhoto,
                p.CloseReason,
                p.InLatitude,
                p.InLongitude,
                p.OutLatitude,
                p.OutLongitude,
                p.ApprovalStatus,
                p.OffsiteReason,
                p.ReviewNote,
                p.ReviewedBy,
                p.ReviewedAtUtc))
            .ToList();
        var open = dtos.FirstOrDefault(p => p.OutAtUtc is null);
        var status = open is null
            ? "You're clocked out"
            : $"You're clocked in since {open.InAtManila}";
        var settingBranchId = open?.BranchId ?? branchId;
        var requireClockPhoto = await db.Branches.AsNoTracking()
            .Where(b => b.Id == settingBranchId)
            .Select(b => b.RequireClockPhoto)
            .FirstOrDefaultAsync(cancellationToken);

        return new AttendanceMeResponse(open is not null, status, open, dtos, requireClockPhoto, staff.MustClock);
    }

    public async Task<AttendanceRegisterResponse> GetRegisterAsync(
        Domain.Staff? actor,
        IReadOnlyList<Branch>? shops,
        DateOnly? from,
        DateOnly? to,
        Guid? staffId,
        Guid? branchId,
        string? approval,
        CancellationToken cancellationToken)
    {
        var (start, end, fromUtc, toExclusiveUtc) = ResolveWeekRange(from, to);
        var rows = await QueryRowsAsync(
            actor, shops, fromUtc, toExclusiveUtc, staffId, branchId, approval, cancellationToken);
        return new AttendanceRegisterResponse(start, end, rows);
    }

    public async Task<AttendanceCsvFile> BuildRegisterCsvAsync(
        Domain.Staff? actor,
        IReadOnlyList<Branch>? shops,
        DateOnly? from,
        DateOnly? to,
        Guid? staffId,
        Guid? branchId,
        string? approval,
        CancellationToken cancellationToken)
    {
        var register = await GetRegisterAsync(
            actor, shops, from, to, staffId, branchId, approval, cancellationToken);
        var lines = new List<string> { JoinCsv("Name", "Branch", "Kind", "In", "Out", "Hours") };
        foreach (var row in register.Rows)
        {
            lines.Add(JoinCsv(
                row.StaffName,
                row.BranchName,
                row.BranchKind == BranchKind.Franchise ? "Franchise" : "Owned",
                row.InAtManila,
                row.IsOpen ? "" : row.OutAtManila,
                row.Hours is decimal hours
                    ? hours.ToString("0.00", CultureInfo.InvariantCulture)
                    : ""));
        }

        var bytes = Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n");
        var fileName = $"rakkii-attendance-{register.From:yyyy-MM-dd}-{register.To:yyyy-MM-dd}.csv";
        return new AttendanceCsvFile(fileName, bytes);
    }

    internal static decimal? Hours(
        DateTime inAtUtc,
        DateTime? outAtUtc,
        PunchApprovalStatus approvalStatus = PunchApprovalStatus.Approved)
    {
        if (approvalStatus != PunchApprovalStatus.Approved)
        {
            return null;
        }

        if (outAtUtc is not DateTime closed)
        {
            return null;
        }

        var hours = (decimal)(closed - inAtUtc).TotalHours;
        if (hours < 0)
        {
            return null;
        }

        return Math.Round(hours, 2, MidpointRounding.AwayFromZero);
    }

    internal static string CsvEscape(string? value)
    {
        var text = value ?? "";
        if (text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r'))
        {
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        return text;
    }

    private static bool IsSelfOnly(Domain.Staff? actor) =>
        actor is not null && actor.Role is not StaffRole.Owner and not StaffRole.Admin;

    private static (DateOnly Start, DateOnly End, DateTime FromUtc, DateTime ToExclusiveUtc) ResolveWeekRange(
        DateOnly? from,
        DateOnly? to)
    {
        var (weekFrom, weekTo) = ManilaTime.CurrentWeek();
        var start = from ?? weekFrom;
        var end = to ?? weekTo;
        if (start > end)
        {
            (start, end) = (end, start);
        }

        return (start, end, ManilaTime.StartOfDayUtc(start), ManilaTime.StartOfDayUtc(end.AddDays(1)));
    }

    private async Task<IReadOnlyList<AttendanceRegisterRowDto>> QueryRowsAsync(
        Domain.Staff? actor,
        IReadOnlyList<Branch>? shops,
        DateTime fromUtc,
        DateTime toExclusiveUtc,
        Guid? staffId,
        Guid? branchId,
        string? approval,
        CancellationToken cancellationToken)
    {
        var query = db.AttendancePunches.AsNoTracking()
            .Where(p => p.InAtUtc >= fromUtc && p.InAtUtc < toExclusiveUtc);
        var approvalStatus = ParseApproval(approval);
        if (approvalStatus is PunchApprovalStatus filter)
        {
            query = query.Where(p => p.ApprovalStatus == filter);
        }

        if (IsSelfOnly(actor))
        {
            query = query.Where(p => p.StaffId == actor!.Id);
        }
        else
        {
            var shopIds = (shops ?? []).Select(s => s.Id).ToList();
            if (branchId is Guid requested && shopIds.Contains(requested))
            {
                shopIds = [requested];
            }

            if (shopIds.Count == 0)
            {
                return [];
            }

            query = query.Where(p => shopIds.Contains(p.BranchId));
            if (staffId is Guid filterStaff)
            {
                query = query.Where(p => p.StaffId == filterStaff);
            }
        }

        var raw = await query
            .OrderBy(p => p.InAtUtc)
            .ThenBy(p => p.Staff.Profile.DisplayName)
            .Select(p => new
            {
                p.StaffId,
                StaffName = p.Staff.Profile.DisplayName,
                p.BranchId,
                BranchName = p.Branch.Name,
                BranchCode = p.Branch.Code,
                BranchKind = p.Branch.Kind,
                p.Id,
                p.InAtUtc,
                p.OutAtUtc,
                p.CloseReason,
                p.InLatitude,
                p.InLongitude,
                p.OutLatitude,
                p.OutLongitude,
                HasInPhoto = p.InPhotoStoragePath != null,
                HasOutPhoto = p.OutPhotoStoragePath != null,
                p.ApprovalStatus,
                p.OffsiteReason,
                p.ReviewNote,
                ReviewedBy = p.ReviewedByStaff != null ? p.ReviewedByStaff.Profile.DisplayName : null,
                p.ReviewedAtUtc
            })
            .ToListAsync(cancellationToken);

        return raw.Select(p => MapRegisterRow(
            p.StaffId,
            p.StaffName,
            p.BranchId,
            p.BranchName,
            p.BranchCode,
            p.BranchKind,
            p.InAtUtc,
            p.OutAtUtc,
            p.Id,
            p.HasInPhoto,
            p.HasOutPhoto,
            p.CloseReason,
            p.InLatitude,
            p.InLongitude,
            p.OutLatitude,
            p.OutLongitude,
            p.ApprovalStatus,
            p.OffsiteReason,
            p.ReviewNote,
            p.ReviewedBy,
            p.ReviewedAtUtc)).ToList();
    }

    private static AttendanceRegisterRowDto MapRegisterRow(
        Guid staffId,
        string staffName,
        Guid branchId,
        string branchName,
        string branchCode,
        BranchKind branchKind,
        DateTime inAtUtc,
        DateTime? outAtUtc,
        Guid punchId,
        bool hasInPhoto,
        bool hasOutPhoto,
        string? closeReason = null,
        double? inLatitude = null,
        double? inLongitude = null,
        double? outLatitude = null,
        double? outLongitude = null,
        PunchApprovalStatus approvalStatus = PunchApprovalStatus.Approved,
        string? offsiteReason = null,
        string? reviewNote = null,
        string? reviewedBy = null,
        DateTime? reviewedAtUtc = null)
    {
        var open = outAtUtc is null;
        return new AttendanceRegisterRowDto(
            staffId,
            staffName,
            branchId,
            branchName,
            branchCode,
            branchKind,
            inAtUtc,
            ManilaTime.FormatRegisterDateTime(inAtUtc),
            ManilaTime.FormatDate(inAtUtc),
            open ? null : outAtUtc,
            open ? null : ManilaTime.FormatRegisterDateTime(outAtUtc!.Value),
            Hours(inAtUtc, outAtUtc, approvalStatus),
            open,
            punchId,
            hasInPhoto,
            hasOutPhoto,
            closeReason,
            inLatitude,
            inLongitude,
            outLatitude,
            outLongitude,
            approvalStatus,
            offsiteReason,
            reviewNote,
            reviewedBy,
            reviewedAtUtc);
    }

    private static string JoinCsv(params string?[] cells) =>
        string.Join(",", cells.Select(CsvEscape));

    private Task<AttendancePunch?> FindOpenAsync(Guid staffId, CancellationToken cancellationToken) =>
        db.AttendancePunches.FirstOrDefaultAsync(
            p => p.StaffId == staffId && p.OutAtUtc == null,
            cancellationToken);

    /// <summary>
    /// Refuses edits that touch a locked payroll run for this person. The check covers every Manila
    /// date from the first instant to the last.
    /// </summary>
    private async Task EnsureUnlockedAsync(
        Guid staffId,
        DateTime firstUtc,
        DateTime lastUtc,
        CancellationToken cancellationToken)
    {
        var from = ManilaTime.ToManilaDate(firstUtc);
        var to = ManilaTime.ToManilaDate(lastUtc);
        if (await PayrollRunService.IsPeriodLockedAsync(db, staffId, from, to, cancellationToken))
        {
            throw new InvalidOperationException(PayrollRunService.LockedPeriodMessage);
        }
    }

    /// <summary>
    /// Rejects a punch whose [in, out) range overlaps an existing punch. An open existing punch runs
    /// to now. A new open punch runs to infinity, so it overlaps anything that ends after it starts.
    /// </summary>
    private async Task EnsureNoOverlapAsync(
        Guid staffId,
        DateTime inAtUtc,
        DateTime? outAtUtc,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var query = db.AttendancePunches.AsNoTracking().Where(p => p.StaffId == staffId);
        if (outAtUtc is DateTime end)
        {
            query = query.Where(p => p.InAtUtc < end);
        }

        var overlaps = await query.AnyAsync(
            p => (p.OutAtUtc == null && now > inAtUtc)
                 || (p.OutAtUtc != null && p.OutAtUtc > inAtUtc),
            cancellationToken);
        if (overlaps)
        {
            throw new AttendanceBadRequestException(OverlapMessage);
        }
    }

    public async Task<IReadOnlyList<OpenPunchDto>> ListOpenPunchesAsync(
        IReadOnlyList<Branch> shops,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var (_, _, fromUtc, toExclusiveUtc) = ResolveWeekRange(from, to);
        var shopIds = shops.Select(s => s.Id).ToList();
        if (shopIds.Count == 0)
        {
            return [];
        }

        var rows = await db.AttendancePunches.AsNoTracking()
            .Where(p => p.OutAtUtc == null
                        && p.InAtUtc >= fromUtc
                        && p.InAtUtc < toExclusiveUtc
                        && shopIds.Contains(p.BranchId))
            .OrderBy(p => p.InAtUtc)
            .ThenBy(p => p.Staff.Profile.DisplayName)
            .Select(p => new { p.Id, p.StaffId, StaffName = p.Staff.Profile.DisplayName, p.InAtUtc })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new OpenPunchDto(
                r.Id,
                r.StaffId,
                r.StaffName,
                r.InAtUtc,
                ManilaTime.FormatPunchLabel(r.InAtUtc)))
            .ToList();
    }

    private async Task<AttendancePunchDto> ToDtoAsync(AttendancePunch punch, CancellationToken cancellationToken)
    {
        var branchName = punch.Branch?.Name;
        if (string.IsNullOrWhiteSpace(branchName))
        {
            branchName = await db.Branches.AsNoTracking()
                .Where(b => b.Id == punch.BranchId)
                .Select(b => b.Name)
                .FirstAsync(cancellationToken);
        }

        string? reviewedBy = null;
        if (punch.ReviewedByStaffId is Guid reviewerId)
        {
            reviewedBy = punch.ReviewedByStaff?.Profile?.DisplayName;
            if (string.IsNullOrWhiteSpace(reviewedBy))
            {
                reviewedBy = await db.Staff.AsNoTracking()
                    .Where(s => s.Id == reviewerId)
                    .Select(s => s.Profile.DisplayName)
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        return Map(
            punch.Id,
            punch.StaffId,
            punch.BranchId,
            branchName,
            punch.InAtUtc,
            punch.OutAtUtc,
            punch.Source,
            HasPhoto(punch.InPhotoStoragePath),
            HasPhoto(punch.OutPhotoStoragePath),
            punch.CloseReason,
            punch.InLatitude,
            punch.InLongitude,
            punch.OutLatitude,
            punch.OutLongitude,
            punch.ApprovalStatus,
            punch.OffsiteReason,
            punch.ReviewNote,
            reviewedBy,
            punch.ReviewedAtUtc);
    }

    private async Task<AttendancePunchDto> OpenShiftAsync(
        Domain.Staff staff,
        Branch branch,
        string source,
        DateTime inAtUtc,
        double? latitude,
        double? longitude,
        IFormFile? photo,
        bool self,
        CancellationToken cancellationToken,
        PunchApprovalStatus approvalStatus = PunchApprovalStatus.Approved,
        string? offsiteReason = null)
    {
        var open = await FindOpenAsync(staff.Id, cancellationToken);
        if (open is not null)
        {
            throw new InvalidOperationException(self ? "You're already clocked in." : "They're already clocked in.");
        }

        EnsurePhoto(
            branch.RequireClockPhoto,
            photo,
            "A photo is required to clock in.");

        var punch = new AttendancePunch
        {
            Id = Guid.NewGuid(),
            StaffId = staff.Id,
            BranchId = branch.Id,
            InAtUtc = inAtUtc,
            Source = source,
            InLatitude = latitude,
            InLongitude = longitude,
            ApprovalStatus = approvalStatus,
            OffsiteReason = offsiteReason
        };
        if (photo is { Length: > 0 })
        {
            await StorePhotoAsync(punch, photo, clockIn: true, cancellationToken);
        }

        db.AttendancePunches.Add(punch);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsOpenShiftConflict(ex))
        {
            throw new InvalidOperationException(AlreadyClockedInMessage);
        }

        return await ToDtoAsync(punch, cancellationToken);
    }

    private async Task<Domain.Staff> RequireClockTargetAsync(
        Guid branchId,
        Guid staffId,
        CancellationToken cancellationToken)
    {
        var target = await db.Staff.Include(s => s.Profile)
            .FirstOrDefaultAsync(
                s => s.Id == staffId && s.Active && s.BranchId == branchId,
                cancellationToken);
        if (target is null)
        {
            throw new KeyNotFoundException("That person is not on this shop.");
        }

        if (target.Role == StaffRole.Owner)
        {
            throw new AttendanceBadRequestException("Owners do not use the time clock.");
        }

        if (!target.MustClock)
        {
            throw new AttendanceBadRequestException("This person does not use the time clock.");
        }

        return target;
    }

    private static void EnsureManager(Domain.Staff actor)
    {
        if (actor.Role is not StaffRole.Owner and not StaffRole.Admin)
        {
            throw new AttendanceForbiddenException("Only an owner or admin can do that.");
        }
    }

    private static DateTime RequireInstant(DateTime? utc, string? manila, string label)
    {
        if (utc is DateTime given)
        {
            return given.Kind switch
            {
                DateTimeKind.Utc => given,
                DateTimeKind.Local => given.ToUniversalTime(),
                _ => DateTime.SpecifyKind(given, DateTimeKind.Utc)
            };
        }

        if (ManilaTime.TryParseLocalToUtc(manila, out var parsed))
        {
            return parsed;
        }

        throw new AttendanceBadRequestException($"Enter a {label} time in Manila.");
    }

    private async Task StorePhotoAsync(
        AttendancePunch punch,
        IFormFile photo,
        bool clockIn,
        CancellationToken cancellationToken)
    {
        var processed = await ProcessClockPhotoAsync(photo, cancellationToken);
        var ext = Path.GetExtension(processed.FileName);
        if (string.IsNullOrWhiteSpace(ext))
        {
            ext = ".jpg";
        }

        var side = clockIn ? "in" : "out";
        var storagePath = $"attendance/{punch.BranchId:N}/{punch.Id:N}/{side}-{Guid.NewGuid():N}{ext}";
        try
        {
            await using var stream = new MemoryStream(processed.Bytes, writable: false);
            await files.SaveAsync(storagePath, stream, processed.ContentType, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            throw new ClockPhotoException("Could not store the photo.");
        }

        if (clockIn)
        {
            punch.InPhotoStoragePath = storagePath;
            punch.InPhotoFileName = processed.FileName;
            punch.InPhotoContentType = processed.ContentType;
        }
        else
        {
            punch.OutPhotoStoragePath = storagePath;
            punch.OutPhotoFileName = processed.FileName;
            punch.OutPhotoContentType = processed.ContentType;
        }
    }

    private static async Task<ProcessedFile> ProcessClockPhotoAsync(IFormFile photo, CancellationToken cancellationToken)
    {
        ProcessedFile processed;
        try
        {
            processed = await PhotoShrink.ProcessFormFileAsync(photo, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw new ClockPhotoException(ex.Message);
        }

        if (!processed.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ClockPhotoException("Clock photos must be a picture.");
        }

        return processed;
    }

    private static void EnsurePhoto(bool required, IFormFile? photo, string message)
    {
        if (required && (photo is null || photo.Length == 0))
        {
            throw new ClockPhotoException(message);
        }
    }

    private static bool HasPhoto(string? storagePath) => !string.IsNullOrWhiteSpace(storagePath);

    private static AttendancePunchDto Map(
        Guid id,
        Guid staffId,
        Guid branchId,
        string branchName,
        DateTime inAtUtc,
        DateTime? outAtUtc,
        string source,
        bool hasInPhoto = false,
        bool hasOutPhoto = false,
        string? closeReason = null,
        double? inLatitude = null,
        double? inLongitude = null,
        double? outLatitude = null,
        double? outLongitude = null,
        PunchApprovalStatus approvalStatus = PunchApprovalStatus.Approved,
        string? offsiteReason = null,
        string? reviewNote = null,
        string? reviewedBy = null,
        DateTime? reviewedAtUtc = null) =>
        new(
            id,
            staffId,
            branchId,
            branchName,
            inAtUtc,
            outAtUtc,
            ManilaTime.FormatPunchLabel(inAtUtc),
            outAtUtc is DateTime closedAt ? ManilaTime.FormatPunchLabel(closedAt) : null,
            source,
            hasInPhoto,
            hasOutPhoto,
            closeReason,
            inLatitude,
            inLongitude,
            outLatitude,
            outLongitude,
            approvalStatus,
            offsiteReason,
            reviewNote,
            reviewedBy,
            reviewedAtUtc);

    /// <summary>
    /// True when the save failed on the unique open-punch index (a second open shift for one person).
    /// This happens when two clock-ins race past the open-shift check.
    /// </summary>
    private static bool IsOpenShiftConflict(DbUpdateException ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return true;
            }
        }

        return false;
    }

    private static (PunchApprovalStatus Status, string? Reason) ResolveSelfPunch(
        Branch branch,
        double? latitude,
        double? longitude,
        string? offsiteReason,
        string missingGpsMessage,
        string action)
    {
        if (!GeoDistance.IsOffsite(branch, latitude, longitude))
        {
            return (PunchApprovalStatus.Approved, null);
        }

        if (!branch.AllowOffsitePunchWithApproval)
        {
            GeoDistance.EnsureInside(branch, latitude, longitude, missingGpsMessage, action);
        }

        return (PunchApprovalStatus.Pending, RequireOffsiteReason(offsiteReason));
    }

    private static string RequireOffsiteReason(string? reason)
    {
        var trimmed = reason?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            throw new AttendanceBadRequestException("A reason is required for an off-site punch.");
        }

        if (trimmed.Length > 500)
        {
            throw new AttendanceBadRequestException("Reason must be 500 characters or fewer.");
        }

        return trimmed;
    }

    private static string AppendOffsiteReason(string? existing, string? next)
    {
        var added = next?.Trim() ?? "";
        if (added.Length == 0)
        {
            return existing?.Trim() ?? "";
        }

        if (string.IsNullOrWhiteSpace(existing))
        {
            return added;
        }

        var combined = existing.Trim() + "; " + added;
        return combined.Length <= 500 ? combined : combined[..500];
    }

    private static PunchApprovalStatus? ParseApproval(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "pending" => PunchApprovalStatus.Pending,
            "approved" => PunchApprovalStatus.Approved,
            "rejected" => PunchApprovalStatus.Rejected,
            _ => throw new AttendanceBadRequestException("approval must be pending, approved, or rejected.")
        };
    }
}
