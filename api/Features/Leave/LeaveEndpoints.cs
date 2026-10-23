using HrCore.Features.HrChangeLog;
using HrCore.Features.Payroll;
using HrCore.Features.Staff;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Leave;

public static class LeaveEndpoints
{
    public const int NoteMaxLength = 500;
    public const int CancelReasonMaxLength = 500;

    private static readonly LeaveType[] LeaveTypes =
    [
        LeaveType.Vacation,
        LeaveType.Sick,
        LeaveType.Unpaid,
        // Statutory service incentive leave. Paid leave: it does not reduce a monthly salary.
        LeaveType.ServiceIncentive
    ];

    public static IEndpointRouteBuilder MapLeaveApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/leave")
            .WithTags("Leave")
            .AddEndpointFilter<StaffAuthorizationFilter>();

        api.MapGet("/", List).WithName("ListLeave");
        api.MapGet("/balances", Balances).WithName("LeaveBalances");
        api.MapPut("/allotments", UpsertAllotment)
            .AddEndpointFilter(StaffRoleFilter.OwnerOnly)
            .WithName("UpsertLeaveAllotment");
        api.MapPost("/", Create).WithName("CreateLeaveRequest");
        api.MapPost("/for-staff", CreateForStaff)
            .AddEndpointFilter(StaffRoleFilter.OwnerOnly)
            .WithName("CreateStaffLeave");
        api.MapGet("/{id:guid}/affected-jobs", AffectedJobs)
            .AddEndpointFilter(StaffRoleFilter.OwnerOrAdmin)
            .WithName("ListLeaveAffectedJobs");
        api.MapPost("/{id:guid}/decision", Decide)
            .AddEndpointFilter(StaffRoleFilter.OwnerOrAdmin)
            .WithName("DecideLeaveRequest");
        api.MapPost("/{id:guid}/cancel", Cancel).WithName("CancelLeaveRequest");
        return app;
    }

    private static async Task<IResult> List(
        HttpContext http,
        BranchResolver branches,
        IHrDb db,
        CancellationToken cancellationToken,
        int? year = null,
        bool mine = false)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Forbidden("Staff access required.");
        }

        if (YearError(year) is { } yearError)
        {
            return yearError;
        }

        var resolvedYear = ResolveYear(year);
        var query = db.LeaveRequests.AsNoTracking()
            .Include(r => r.Staff)
            .ThenInclude(s => s.Profile)
            .Where(r => r.StartDate.Year == resolvedYear);

        // mine=true is "My requests" for every role, including owner and admin.
        if (mine || !HrRoles.IsDesk(actor.Role))
        {
            query = query.Where(r => r.StaffId == actor.Id);
        }
        else if (actor.Role is StaffRole.Owner or StaffRole.Admin)
        {
            var shop = await ShopAsync(http, branches, db, cancellationToken);
            var staffIds = OnShop(db, shop.BranchId, shop.Code).Select(s => s.Id);
            query = query.Where(r => staffIds.Contains(r.StaffId));
        }
        else
        {
            query = query.Where(r => r.StaffId == actor.Id);
        }

        var rows = await query
            .OrderByDescending(r => r.StartDate)
            .ThenByDescending(r => r.CreatedAtUtc)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

        var calendars = new Dictionary<Guid, LeaveDayContext>();
        var responses = new List<LeaveRequestResponse>(rows.Count);
        foreach (var row in rows)
        {
            if (!calendars.TryGetValue(row.StaffId, out var calendar))
            {
                calendar = await YearCalendarAsync(db, row.StaffId, resolvedYear, cancellationToken);
                calendars[row.StaffId] = calendar;
            }

            responses.Add(ToResponse(row, row.Staff.Profile.DisplayName, calendar.Count(row.StartDate, row.EndDate, row.HalfDay)));
        }

        return Results.Ok(responses);
    }

    private static async Task<IResult> Balances(
        HttpContext http,
        BranchResolver branches,
        IHrDb db,
        CancellationToken cancellationToken,
        int? year = null,
        Guid? staffId = null,
        int? shop = null)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Forbidden("Staff access required.");
        }

        if (YearError(year) is { } yearError)
        {
            return yearError;
        }

        var resolvedYear = ResolveYear(year);

        if (shop == 1)
        {
            if (actor.Role is not (StaffRole.Owner or StaffRole.Admin))
            {
                return Forbidden("Shop balances are only available to owners and admins.");
            }

            var shopContext = await ShopAsync(http, branches, db, cancellationToken);
            var staffMembers = await OnShop(db, shopContext.BranchId, shopContext.Code)
                .Include(s => s.Profile)
                .Where(s => s.Active && s.Role != StaffRole.Owner)
                .OrderBy(s => s.Profile.DisplayName)
                .ToListAsync(cancellationToken);

            var teamBalances = new List<StaffLeaveBalancesResponse>(staffMembers.Count);
            foreach (var member in staffMembers)
            {
                var balances = await BalancesAsync(db, member.Id, resolvedYear, cancellationToken);
                teamBalances.Add(new StaffLeaveBalancesResponse(
                    member.Id,
                    member.Profile?.DisplayName ?? "",
                    member.Role.ToString().ToLowerInvariant(),
                    balances));
            }

            return Results.Ok(teamBalances);
        }

        Guid subjectId;
        if (actor.Role is StaffRole.Owner or StaffRole.Admin
            && staffId is Guid requested
            && requested != Guid.Empty)
        {
            if (await FindOnShopAsync(requested, http, branches, db, cancellationToken) is null)
            {
                return Results.NotFound();
            }

            subjectId = requested;
        }
        else
        {
            // Technician and sales always read their own balances. Another staff id is ignored.
            subjectId = actor.Id;
        }

        return Results.Ok(await BalancesAsync(db, subjectId, resolvedYear, cancellationToken));
    }

    private static async Task<IResult> UpsertAllotment(
        UpsertLeaveAllotmentRequest request,
        HttpContext http,
        BranchResolver branches,
        IHrDb db,
        HrChangeLogService hr,
        CancellationToken cancellationToken)
    {
        if (request.StaffId == Guid.Empty)
        {
            return Results.BadRequest(new { error = "Staff is required." });
        }

        if (request.Year < 1 || request.Year > 9999)
        {
            return Results.BadRequest(new { error = "Year is required." });
        }

        if (request.LeaveType is null || !Enum.IsDefined(request.LeaveType.Value))
        {
            return Results.BadRequest(new { error = "Leave type is required." });
        }

        if (request.Days is null || request.Days < 0)
        {
            return Results.BadRequest(new { error = "Days must be zero or more." });
        }

        if (await FindOnShopAsync(request.StaffId, http, branches, db, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        var existing = await db.LeaveAllotments
            .FirstOrDefaultAsync(
                a => a.StaffId == request.StaffId
                    && a.Year == request.Year
                    && a.LeaveType == request.LeaveType.Value,
                cancellationToken);
        var previousDays = existing?.Days;
        if (existing is null)
        {
            existing = new LeaveAllotment
            {
                Id = Guid.NewGuid(),
                StaffId = request.StaffId,
                Year = request.Year,
                LeaveType = request.LeaveType.Value,
                Days = request.Days.Value
            };
            db.LeaveAllotments.Add(existing);
        }
        else
        {
            existing.Days = request.Days.Value;
        }

        hr.Record(
            http.GetStaff()?.Id,
            existing.StaffId,
            HrChangeField.LeaveAllotment,
            HrChangeLogService.Format(previousDays),
            HrChangeLogService.Format(existing.Days),
            year: existing.Year,
            refType: "leave_allotment");

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new LeaveAllotmentResponse(
            existing.StaffId,
            existing.Year,
            existing.LeaveType,
            existing.Days));
    }

    private static async Task<IResult> Create(
        CreateLeaveRequest request,
        HttpContext http,
        BranchResolver branches,
        IHrDb db,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Forbidden("Staff access required.");
        }

        if (actor.Role == StaffRole.Owner)
        {
            return Forbidden("Owners do not have personal leave.");
        }

        // A staff id in the body is ignored. The request is always for the signed-in person.
        if (request.LeaveType is null || !Enum.IsDefined(request.LeaveType.Value))
        {
            return Results.BadRequest(new { error = "Leave type is required." });
        }

        if (request.StartDate is null || request.EndDate is null)
        {
            return Results.BadRequest(new { error = "Start and end dates are required." });
        }

        var start = request.StartDate.Value;
        var end = request.EndDate.Value;
        if (end < start)
        {
            return Results.BadRequest(new { error = "End date must be on or after the start date." });
        }

        if (start.Year != end.Year)
        {
            return Results.BadRequest(new { error = "Start and end must be in the same calendar year." });
        }

        if (HalfDayError(request.HalfDay, start, end) is { } halfDayError)
        {
            return halfDayError;
        }

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is { Length: > NoteMaxLength })
        {
            return Results.BadRequest(new { error = "Note must be 500 characters or fewer." });
        }

        var calendar = await LeaveDayContext.LoadAsync(db, actor.Id, start, end, cancellationToken);
        var days = calendar.Count(start, end, request.HalfDay);
        if (days == 0m)
        {
            return Results.BadRequest(new { error = "Those dates are all rest days or holidays." });
        }

        if (await OverlapsAsync(db, actor.Id, start, end, cancellationToken))
        {
            return Results.BadRequest(new { error = "This overlaps another leave request." });
        }

        var branchId = await branches.ResolveOpsAsync(http, cancellationToken);
        var row = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            StaffId = actor.Id,
            BranchId = branchId,
            LeaveType = request.LeaveType.Value,
            StartDate = start,
            EndDate = end,
            HalfDay = request.HalfDay,
            Status = LeaveRequestStatus.Pending,
            Note = note,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.LeaveRequests.Add(row);
        await db.SaveChangesAsync(cancellationToken);

        var name = actor.Profile?.DisplayName ?? "";
        return Results.Created($"/api/leave/{row.Id}", ToResponse(row, name, days));
    }

    private static async Task<IResult> CreateForStaff(
        CreateStaffLeaveRequest request,
        HttpContext http,
        BranchResolver branches,
        IHrDb db,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null || actor.Role != StaffRole.Owner)
        {
            return Forbidden("Only owners can record leave for staff.");
        }

        if (request.StaffId == Guid.Empty)
        {
            return Results.BadRequest(new { error = "Staff is required." });
        }

        if (request.LeaveType is null || !Enum.IsDefined(request.LeaveType.Value))
        {
            return Results.BadRequest(new { error = "Leave type is required." });
        }

        if (request.StartDate is null || request.EndDate is null)
        {
            return Results.BadRequest(new { error = "Start and end dates are required." });
        }

        var start = request.StartDate.Value;
        var end = request.EndDate.Value;
        if (end < start)
        {
            return Results.BadRequest(new { error = "End date must be on or after the start date." });
        }

        if (start.Year != end.Year)
        {
            return Results.BadRequest(new { error = "Start and end must be in the same calendar year." });
        }

        if (HalfDayError(request.HalfDay, start, end) is { } halfDayError)
        {
            return halfDayError;
        }

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is { Length: > NoteMaxLength })
        {
            return Results.BadRequest(new { error = "Note must be 500 characters or fewer." });
        }

        var targetStaff = await db.Staff
            .Include(s => s.Profile)
            .FirstOrDefaultAsync(s => s.Id == request.StaffId, cancellationToken);
        if (targetStaff is null || await FindOnShopAsync(targetStaff.Id, http, branches, db, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        if (targetStaff.Role == StaffRole.Owner)
        {
            return Results.BadRequest(new { error = "Cannot record leave for owner." });
        }

        var calendar = await LeaveDayContext.LoadAsync(db, targetStaff.Id, start, end, cancellationToken);
        var days = calendar.Count(start, end, request.HalfDay);
        if (days == 0m)
        {
            return Results.BadRequest(new { error = "Those dates are all rest days or holidays." });
        }

        if (await OverlapsAsync(db, targetStaff.Id, start, end, cancellationToken))
        {
            return Results.BadRequest(new { error = "This overlaps another leave request." });
        }

        var year = start.Year;
        var balance = await BalanceForTypeAsync(db, targetStaff.Id, year, request.LeaveType.Value, cancellationToken);
        if (balance.Allotment - balance.Used - days < 0)
        {
            return Results.BadRequest(new { error = "That leave would make the balance negative." });
        }

        var branchId = await branches.ResolveOpsAsync(http, cancellationToken);
        var row = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            StaffId = targetStaff.Id,
            BranchId = branchId,
            LeaveType = request.LeaveType.Value,
            StartDate = start,
            EndDate = end,
            HalfDay = request.HalfDay,
            Status = LeaveRequestStatus.Approved,
            Note = note,
            CreatedAtUtc = DateTime.UtcNow,
            DecidedByStaffId = actor.Id,
            DecidedAtUtc = DateTime.UtcNow
        };
        db.LeaveRequests.Add(row);
        await db.SaveChangesAsync(cancellationToken);

        var name = targetStaff.Profile?.DisplayName ?? "";
        return Results.Created($"/api/leave/{row.Id}", ToResponse(row, name, days));
    }

    private static async Task<IResult> Decide(
        Guid id,
        LeaveDecisionRequest request,
        HttpContext http,
        BranchResolver branches,
        IHrDb db,
        HrChangeLogService hr,
        IStaffWorkHooks work,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null || actor.Role is not (StaffRole.Owner or StaffRole.Admin))
        {
            return Forbidden("You cannot approve leave.");
        }

        if (request.Decision is not (LeaveRequestStatus.Approved or LeaveRequestStatus.Rejected))
        {
            return Results.BadRequest(new { error = "Decision must be approved or rejected." });
        }

        var row = await db.LeaveRequests
            .Include(r => r.Staff)
            .ThenInclude(s => s.Profile)
            .Include(r => r.Staff)
            .ThenInclude(s => s.Branch)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (row is null || await FindOnShopAsync(row.StaffId, http, branches, db, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        if (row.StaffId == actor.Id)
        {
            return Forbidden("Someone else must decide your leave.");
        }

        if (row.Status != LeaveRequestStatus.Pending)
        {
            return Results.BadRequest(new { error = "This request is already decided." });
        }

        var reassignments = request.Reassignments ?? [];
        if (reassignments.Count > 0 && request.Decision != LeaveRequestStatus.Approved)
        {
            return Results.BadRequest(new { error = "Job reassignments only apply when approving leave." });
        }

        if (await PayrollRunService.IsPeriodLockedAsync(db, row.StaffId, row.StartDate, row.EndDate, cancellationToken))
        {
            return Results.Conflict(new { error = PayrollRunService.LockedPeriodMessage });
        }

        var year = row.StartDate.Year;
        var calendar = await YearCalendarAsync(db, row.StaffId, year, cancellationToken);
        var days = calendar.Count(row.StartDate, row.EndDate, row.HalfDay);
        if (request.Decision == LeaveRequestStatus.Approved)
        {
            var balance = await BalanceForTypeAsync(db, row.StaffId, year, row.LeaveType, cancellationToken);
            if (balance.Allotment - balance.Used - days < 0)
            {
                return Results.BadRequest(new { error = "That approval would make the balance negative." });
            }
        }

        // Approval and any job moves commit together. A bad reassignment rolls the whole decision back.
        var now = DateTime.UtcNow;
        var error = await StockTransaction.RunAsync(db, async () =>
        {
            if (reassignments.Count > 0)
            {
                var moveError = await work.ReassignForLeaveAsync(row, reassignments, actor.Id, now, cancellationToken);
                if (moveError is not null)
                {
                    return moveError;
                }
            }

            var previousStatus = row.Status;
            row.Status = request.Decision!.Value;
            row.DecidedByStaffId = actor.Id;
            row.DecidedAtUtc = now;

            hr.Record(
                actor.Id,
                row.StaffId,
                HrChangeField.LeaveDecision,
                previousStatus.ToString(),
                row.Status.ToString(),
                year: row.StartDate.Year,
                refType: "leave_request",
                refId: row.Id);

            await db.SaveChangesAsync(cancellationToken);
            return null;
        }, cancellationToken);

        if (error is not null)
        {
            return Results.BadRequest(new { error });
        }

        return Results.Ok(ToResponse(row, row.Staff.Profile.DisplayName, days));
    }

    private static async Task<IResult> AffectedJobs(
        Guid id,
        HttpContext http,
        BranchResolver branches,
        IHrDb db,
        IStaffWorkHooks work,
        CancellationToken cancellationToken)
    {
        var row = await db.LeaveRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (row is null || await FindOnShopAsync(row.StaffId, http, branches, db, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(await work.ListOpenWorkAsync(row.StaffId, cancellationToken));
    }

    private static async Task<IResult> Cancel(
        Guid id,
        CancelLeaveRequest request,
        HttpContext http,
        BranchResolver branches,
        IHrDb db,
        HrChangeLogService hr,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Forbidden("Staff access required.");
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (reason is { Length: > CancelReasonMaxLength })
        {
            return Results.BadRequest(new { error = "Reason must be 500 characters or fewer." });
        }

        var row = await db.LeaveRequests
            .Include(r => r.Staff)
            .ThenInclude(s => s.Profile)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (row is null || await FindOnShopAsync(row.StaffId, http, branches, db, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        if (row.Status is LeaveRequestStatus.Rejected or LeaveRequestStatus.Cancelled)
        {
            return Results.BadRequest(new { error = "This request can't be cancelled." });
        }

        if (row.StaffId == actor.Id)
        {
            // The requester can withdraw their own pending request. Approved leave needs an owner or admin.
            if (row.Status == LeaveRequestStatus.Approved)
            {
                return Forbidden("Ask an owner or admin to cancel approved leave.");
            }
        }
        else
        {
            if (actor.Role is not (StaffRole.Owner or StaffRole.Admin))
            {
                return Forbidden("You cannot cancel someone else's leave.");
            }

            if (row.Status == LeaveRequestStatus.Approved && reason is null)
            {
                return Results.BadRequest(new { error = "A reason is required to cancel approved leave." });
            }
        }

        if (await PayrollRunService.IsPeriodLockedAsync(db, row.StaffId, row.StartDate, row.EndDate, cancellationToken))
        {
            return Results.Conflict(new { error = PayrollRunService.LockedPeriodMessage });
        }

        var year = row.StartDate.Year;
        var calendar = await YearCalendarAsync(db, row.StaffId, year, cancellationToken);
        var days = calendar.Count(row.StartDate, row.EndDate, row.HalfDay);

        ApplyCancellation(row, actor.Id, reason, hr, DateTime.UtcNow);

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(row, row.Staff.Profile.DisplayName, days));
    }

    /// <summary>
    /// Marks a request cancelled and writes the HR change log entries. Shared by the cancel endpoint and
    /// staff deactivation. The caller checks the payroll lock and saves.
    /// </summary>
    internal static void ApplyCancellation(
        LeaveRequest row,
        Guid actorStaffId,
        string? reason,
        HrChangeLogService hr,
        DateTime nowUtc)
    {
        var year = row.StartDate.Year;
        var previousStatus = row.Status;
        row.Status = LeaveRequestStatus.Cancelled;
        row.CancelledByStaffId = actorStaffId;
        row.CancelledAtUtc = nowUtc;
        row.CancelReason = reason;

        hr.Record(
            actorStaffId,
            row.StaffId,
            HrChangeField.LeaveDecision,
            previousStatus.ToString(),
            row.Status.ToString(),
            year: year,
            refType: "leave_request",
            refId: row.Id);
        if (reason is not null)
        {
            hr.Record(
                actorStaffId,
                row.StaffId,
                HrChangeField.LeaveCancelReason,
                null,
                ClipForHrLog(reason),
                year: year,
                refType: "leave_request",
                refId: row.Id);
        }
    }

    /// <summary>HR log values are capped at 200 characters. Longer reasons end with an ellipsis.</summary>
    private const int HrLogValueMaxLength = 200;

    internal static string ClipForHrLog(string value) =>
        value.Length <= HrLogValueMaxLength
            ? value
            : string.Concat(value.AsSpan(0, HrLogValueMaxLength - 3), "...");

    /// <summary>Allotment, used (approved) and pending working days for every leave type in a year.</summary>
    private static async Task<List<LeaveBalanceResponse>> BalancesAsync(
        IHrDb db,
        Guid staffId,
        int year,
        CancellationToken cancellationToken)
    {
        var calendar = await YearCalendarAsync(db, staffId, year, cancellationToken);
        var allotments = await db.LeaveAllotments.AsNoTracking()
            .Where(a => a.StaffId == staffId && a.Year == year)
            .ToListAsync(cancellationToken);
        var spans = await db.LeaveRequests.AsNoTracking()
            .Where(r => r.StaffId == staffId
                && r.StartDate.Year == year
                && (r.Status == LeaveRequestStatus.Approved || r.Status == LeaveRequestStatus.Pending))
            .Select(r => new LeaveSpan(r.LeaveType, r.Status, r.StartDate, r.EndDate, r.HalfDay))
            .ToListAsync(cancellationToken);

        var balances = new List<LeaveBalanceResponse>(LeaveTypes.Length);
        foreach (var type in LeaveTypes)
        {
            balances.Add(BalanceFor(type, allotments, spans, calendar));
        }

        return balances;
    }

    private static async Task<LeaveBalanceResponse> BalanceForTypeAsync(
        IHrDb db,
        Guid staffId,
        int year,
        LeaveType type,
        CancellationToken cancellationToken)
    {
        var calendar = await YearCalendarAsync(db, staffId, year, cancellationToken);
        var allotments = await db.LeaveAllotments.AsNoTracking()
            .Where(a => a.StaffId == staffId && a.Year == year && a.LeaveType == type)
            .ToListAsync(cancellationToken);
        var spans = await db.LeaveRequests.AsNoTracking()
            .Where(r => r.StaffId == staffId
                && r.LeaveType == type
                && r.StartDate.Year == year
                && (r.Status == LeaveRequestStatus.Approved || r.Status == LeaveRequestStatus.Pending))
            .Select(r => new LeaveSpan(r.LeaveType, r.Status, r.StartDate, r.EndDate, r.HalfDay))
            .ToListAsync(cancellationToken);
        return BalanceFor(type, allotments, spans, calendar);
    }

    private static LeaveBalanceResponse BalanceFor(
        LeaveType type,
        IReadOnlyList<LeaveAllotment> allotments,
        IEnumerable<LeaveSpan> spans,
        LeaveDayContext calendar)
    {
        var allotment = allotments.FirstOrDefault(a => a.LeaveType == type)?.Days ?? 0m;
        decimal used = 0m;
        decimal pending = 0m;
        foreach (var span in spans.Where(s => s.LeaveType == type))
        {
            var days = calendar.Count(span.StartDate, span.EndDate, span.HalfDay);
            if (span.Status == LeaveRequestStatus.Approved)
            {
                used += days;
            }
            else
            {
                pending += days;
            }
        }

        return new LeaveBalanceResponse(type, allotment, used, pending, allotment - used - pending);
    }

    private static Task<LeaveDayContext> YearCalendarAsync(
        IHrDb db,
        Guid staffId,
        int year,
        CancellationToken cancellationToken) =>
        LeaveDayContext.LoadAsync(db, staffId, new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), cancellationToken);

    /// <summary>True when the same staff member already has a pending or approved request on any of these dates.</summary>
    private static Task<bool> OverlapsAsync(
        IHrDb db,
        Guid staffId,
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken) =>
        db.LeaveRequests.AsNoTracking().AnyAsync(
            r => r.StaffId == staffId
                && (r.Status == LeaveRequestStatus.Pending || r.Status == LeaveRequestStatus.Approved)
                && r.StartDate <= end
                && r.EndDate >= start,
            cancellationToken);

    private static IResult? HalfDayError(LeaveHalfDay? halfDay, DateOnly start, DateOnly end)
    {
        if (halfDay is null)
        {
            return null;
        }

        if (!Enum.IsDefined(halfDay.Value))
        {
            return Results.BadRequest(new { error = "Half day must be am or pm." });
        }

        return start != end
            ? Results.BadRequest(new { error = "A half day must be a single date." })
            : null;
    }

    private static async Task<ShopContext> ShopAsync(
        HttpContext http,
        BranchResolver branches,
        IHrDb db,
        CancellationToken cancellationToken)
    {
        var branchId = await branches.ResolveOpsAsync(http, cancellationToken);
        var branch = await db.Branches.AsNoTracking()
            .FirstAsync(b => b.Id == branchId, cancellationToken);
        return new ShopContext(branchId, branch.Code);
    }

    private static IQueryable<Domain.Staff> OnShop(IHrDb db, Guid branchId, string branchCode)
    {
        var query = db.Staff.AsNoTracking().Where(s => s.BranchId == branchId);
        if (!string.Equals(branchCode, BranchCodes.Demo, StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(s => s.Branch!.Code != BranchCodes.Demo);
        }

        return query;
    }

    private static async Task<Guid?> FindOnShopAsync(
        Guid staffId,
        HttpContext http,
        BranchResolver branches,
        IHrDb db,
        CancellationToken cancellationToken)
    {
        var shop = await ShopAsync(http, branches, db, cancellationToken);
        var onShop = await OnShop(db, shop.BranchId, shop.Code)
            .AnyAsync(s => s.Id == staffId, cancellationToken);
        return onShop ? staffId : null;
    }

    private static int ResolveYear(int? year) => year ?? ManilaTime.Today().Year;

    private static IResult? YearError(int? year)
    {
        if (year is < 1 or > 9999)
        {
            return Results.BadRequest(new { error = "Year is required." });
        }

        return null;
    }

    private static LeaveRequestResponse ToResponse(LeaveRequest row, string displayName, decimal days) => new(
        row.Id,
        row.StaffId,
        displayName,
        row.LeaveType,
        row.StartDate,
        row.EndDate,
        row.Status,
        row.Note,
        row.HalfDay,
        days,
        row.CancelReason);

    private static IResult Forbidden(string error) =>
        Results.Json(new { error }, statusCode: StatusCodes.Status403Forbidden);

    private readonly record struct ShopContext(Guid BranchId, string Code);

    private readonly record struct LeaveSpan(
        LeaveType LeaveType,
        LeaveRequestStatus Status,
        DateOnly StartDate,
        DateOnly EndDate,
        LeaveHalfDay? HalfDay);
}

public sealed record LeaveRequestResponse(
    Guid Id,
    Guid StaffId,
    string DisplayName,
    LeaveType LeaveType,
    DateOnly StartDate,
    DateOnly EndDate,
    LeaveRequestStatus Status,
    string? Note,
    LeaveHalfDay? HalfDay,
    decimal Days,
    string? CancelReason);

public sealed record LeaveBalanceResponse(
    LeaveType LeaveType,
    decimal Allotment,
    decimal Used,
    decimal Pending,
    decimal Remaining);

public sealed record StaffLeaveBalancesResponse(
    Guid StaffId,
    string DisplayName,
    string Role,
    List<LeaveBalanceResponse> Balances);

public sealed record LeaveAllotmentResponse(
    Guid StaffId,
    int Year,
    LeaveType LeaveType,
    decimal Days);

public sealed class UpsertLeaveAllotmentRequest
{
    public Guid StaffId { get; set; }
    public int Year { get; set; }
    public LeaveType? LeaveType { get; set; }
    public decimal? Days { get; set; }
}

public sealed class CreateLeaveRequest
{
    public LeaveType? LeaveType { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Note { get; set; }

    /// <summary>Am or pm for a single-day request. Counts 0.5 day. Omit for a full day.</summary>
    public LeaveHalfDay? HalfDay { get; set; }

    /// <summary>Ignored. Leave is always filed for the signed-in staff member.</summary>
    public Guid? StaffId { get; set; }
}

public sealed class CreateStaffLeaveRequest
{
    public Guid StaffId { get; set; }
    public LeaveType? LeaveType { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Note { get; set; }
    public LeaveHalfDay? HalfDay { get; set; }
}

public sealed class LeaveDecisionRequest
{
    public LeaveRequestStatus? Decision { get; set; }

    /// <summary>Approval only. Open work the requester holds, each moved to a new assignee or kept (null).</summary>
    public List<LeaveReassignmentItem>? Reassignments { get; set; }
}

public sealed class LeaveReassignmentItem
{
    /// <summary>The open work item (a job in the shop app). The wire name stays <c>jobId</c>.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("jobId")]
    public Guid WorkId { get; set; }

    /// <summary>The new assignee, or null to keep the work with its current assignees.</summary>
    public Guid? ToStaffId { get; set; }
}

public sealed class CancelLeaveRequest
{
    /// <summary>Required to cancel approved leave. Optional for a pending request.</summary>
    public string? Reason { get; set; }
}
