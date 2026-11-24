using System.Globalization;
using HrCore.Features.HrChangeLog;
using HrCore.Features.Payroll;
using HrCore.Features.Staff;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Leave;

public static class LeaveEndpoints
{
    public const int NoteMaxLength = 500;
    public const int CancelReasonMaxLength = 500;
    private const string CrossYearErrorMessage =
        "Leave can't cross calendar years. Submit two requests: one through Dec 31 of the start year, and another starting Jan 1 of the next year.";

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
            .AddEndpointFilter(StaffRoleFilter.ShopHrDesk)
            .WithName("ListLeaveAffectedJobs");
        api.MapPost("/{id:guid}/decision", Decide)
            .AddEndpointFilter(StaffRoleFilter.ShopHrDesk)
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
        bool mine = false,
        string? status = null)
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

        var statusFilter = status?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(statusFilter) && statusFilter is not ("pending" or "decided"))
        {
            return Results.BadRequest(new { error = "Status must be pending or decided." });
        }

        var today = ManilaTime.Today();
        var currentYear = today.Year;
        var query = db.LeaveRequests.AsNoTracking()
            .Include(r => r.Staff)
            .ThenInclude(s => s.Profile)
            .AsQueryable();

        if (year is int explicitYear)
        {
            query = query.Where(r => r.StartDate.Year == explicitYear || r.EndDate.Year == explicitYear);
        }
        else if (statusFilter == "pending")
        {
            // Pending requests span calendar years (e.g., next-year leave waiting for a decision).
        }
        else if (mine && string.IsNullOrEmpty(statusFilter))
        {
            var (mode, hireDate) = await StaffLeavePolicyAsync(db, actor.Id, cancellationToken);
            var activeCycle = LeaveCycle.Resolve(mode, hireDate, today);
            query = query.Where(r => r.Status == LeaveRequestStatus.Pending || r.EndDate >= activeCycle.StartsOn);
        }
        else
        {
            query = query.Where(r => r.StartDate.Year == currentYear || r.EndDate.Year == currentYear);
        }

        // mine=true is "My requests" for every role. Shop-wide list is leave managers only.
        if (mine || !HrRoles.CanManageShopHr(actor))
        {
            query = query.Where(r => r.StaffId == actor.Id);
        }
        else
        {
            var shop = await ShopAsync(http, branches, db, cancellationToken);
            var staffIds = OnShop(db, shop.BranchId).Select(s => s.Id);
            query = query.Where(r => staffIds.Contains(r.StaffId));
        }

        // "pending" is the decision list; "decided" is everything else (approved, rejected, cancelled).
        if (statusFilter == "pending")
        {
            query = query.Where(r => r.Status == LeaveRequestStatus.Pending);
        }
        else if (statusFilter == "decided")
        {
            query = query.Where(r => r.Status != LeaveRequestStatus.Pending);
        }

        var rows = await query
            .OrderByDescending(r => r.StartDate)
            .ThenByDescending(r => r.CreatedAtUtc)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

        var calendars = new Dictionary<(Guid StaffId, int Year), LeaveDayContext>();
        var responses = new List<LeaveRequestResponse>(rows.Count);
        foreach (var row in rows)
        {
            LeaveDayContext calendar;
            if (row.StartDate.Year == row.EndDate.Year)
            {
                var key = (row.StaffId, row.StartDate.Year);
                if (!calendars.TryGetValue(key, out calendar!))
                {
                    calendar = await YearCalendarAsync(db, row.StaffId, row.StartDate.Year, cancellationToken);
                    calendars[key] = calendar;
                }
            }
            else
            {
                calendar = await LeaveDayContext.LoadAsync(db, row.StaffId, row.StartDate, row.EndDate, cancellationToken);
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

        var today = ManilaTime.Today();

        if (shop == 1)
        {
            if (!HrRoles.CanManageShopHr(actor))
            {
                return Forbidden("Shop balances are only available to owners and general managers.");
            }

            var shopContext = await ShopAsync(http, branches, db, cancellationToken);
            var staffMembers = await OnShop(db, shopContext.BranchId)
                .Include(s => s.Profile)
                .Where(s => s.Active && s.Role != StaffRole.Owner)
                .OrderBy(s => s.Profile.DisplayName)
                .ToListAsync(cancellationToken);

            var teamBalances = new List<StaffLeaveBalancesResponse>(staffMembers.Count);
            foreach (var member in staffMembers)
            {
                var cycle = ResolveCycle(shopContext.LeaveExpirationMode, member.HireDate, year, today);
                var balances = await BalancesAsync(db, member.Id, cycle, today, cancellationToken);
                teamBalances.Add(new StaffLeaveBalancesResponse(
                    member.Id,
                    member.Profile?.DisplayName ?? "",
                    member.Role.ToString().ToLowerInvariant(),
                    balances));
            }

            return Results.Ok(teamBalances);
        }

        Guid subjectId;
        if (HrRoles.CanManageShopHr(actor)
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
            // Self-service Access (and tech/sales) always read their own balances. Another staff id is ignored.
            subjectId = actor.Id;
        }

        var (mode, hireDate) = await StaffLeavePolicyAsync(db, subjectId, cancellationToken);
        var subjectCycle = ResolveCycle(mode, hireDate, year, today);
        return Results.Ok(await BalancesAsync(db, subjectId, subjectCycle, today, cancellationToken));
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

        var (mode, hireDate) = await StaffLeavePolicyAsync(db, request.StaffId, cancellationToken);
        var cycle = LeaveCycle.ForYear(mode, hireDate, request.Year);
        if (request.ExpiresOn is DateOnly customExpiresOn)
        {
            if (!HasExpiry(request.LeaveType.Value))
            {
                return Results.BadRequest(new { error = "Only vacation and sick leave can have a custom expiry date." });
            }

            if (customExpiresOn < cycle.StartsOn)
            {
                return Results.BadRequest(new
                {
                    error = string.Create(
                        CultureInfo.InvariantCulture,
                        $"Expiry date can't be before the leave cycle start ({cycle.StartsOn:MMM d, yyyy}).")
                });
            }

            if (customExpiresOn > cycle.ExpiresOn)
            {
                return Results.BadRequest(new
                {
                    error = string.Create(
                        CultureInfo.InvariantCulture,
                        $"Expiry date can't be after the leave cycle end ({cycle.ExpiresOn:MMM d, yyyy}).")
                });
            }
        }

        if (HasDayLimit(request.LeaveType.Value))
        {
            var balance = await BalanceForTypeAsync(
                db, request.StaffId, cycle, request.LeaveType.Value, ManilaTime.Today(), cancellationToken);
            if (request.Days.Value < balance.Used)
            {
                return Results.BadRequest(new
                {
                    error = $"Days can't be less than the {balance.Used:0.#} already approved this year."
                });
            }
        }

        var existing = await db.LeaveAllotments
            .FirstOrDefaultAsync(
                a => a.StaffId == request.StaffId
                    && a.Year == request.Year
                    && a.LeaveType == request.LeaveType.Value,
                cancellationToken);
        var previousDays = existing?.Days;
        var previousExpiresOn = existing?.ExpiresOn;
        if (existing is null)
        {
            existing = new LeaveAllotment
            {
                Id = Guid.NewGuid(),
                StaffId = request.StaffId,
                Year = request.Year,
                LeaveType = request.LeaveType.Value,
                Days = request.Days.Value,
                ExpiresOn = request.ExpiresOn
            };
            db.LeaveAllotments.Add(existing);
        }
        else
        {
            existing.Days = request.Days.Value;
            existing.ExpiresOn = request.ExpiresOn;
        }

        hr.Record(
            http.GetStaff()?.Id,
            existing.StaffId,
            HrChangeField.LeaveAllotment,
            HrChangeLogService.Format(previousDays),
            HrChangeLogService.Format(existing.Days),
            year: existing.Year,
            refType: "leave_allotment");
        hr.Record(
            http.GetStaff()?.Id,
            existing.StaffId,
            HrChangeField.LeaveAllotmentExpiry,
            previousExpiresOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            existing.ExpiresOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            year: existing.Year,
            refType: "leave_allotment");

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new LeaveAllotmentResponse(
            existing.StaffId,
            existing.Year,
            existing.LeaveType,
            existing.Days,
            existing.ExpiresOn));
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

        if (HalfDayError(request.HalfDay, start, end) is { } halfDayError)
        {
            return halfDayError;
        }

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is { Length: > NoteMaxLength })
        {
            return Results.BadRequest(new { error = "Note must be 500 characters or fewer." });
        }

        var (mode, hireDate) = await StaffLeavePolicyAsync(db, actor.Id, cancellationToken);
        var startCycle = LeaveCycle.Resolve(mode, hireDate, start);
        var endCycle = LeaveCycle.Resolve(mode, hireDate, end);
        if (CrossCycleError(startCycle, endCycle) is { } crossCycleError)
        {
            return crossCycleError;
        }

        if (await ExpiryErrorAsync(db, actor.Id, request.LeaveType.Value, startCycle, end, cancellationToken) is { } expiryError)
        {
            return expiryError;
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
        HrChangeLogService hr,
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
            .Include(s => s.Branch)
            .FirstOrDefaultAsync(s => s.Id == request.StaffId, cancellationToken);
        if (targetStaff is null || await FindOnShopAsync(targetStaff.Id, http, branches, db, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        if (targetStaff.Role == StaffRole.Owner)
        {
            return Results.BadRequest(new { error = "Cannot record leave for owner." });
        }

        var mode = targetStaff.Branch?.LeaveExpirationMode ?? Branch.DefaultLeaveExpirationMode;
        var startCycle = LeaveCycle.Resolve(mode, targetStaff.HireDate, start);
        var endCycle = LeaveCycle.Resolve(mode, targetStaff.HireDate, end);
        if (CrossCycleError(startCycle, endCycle) is { } crossCycleError)
        {
            return crossCycleError;
        }

        if (await ExpiryErrorAsync(db, targetStaff.Id, request.LeaveType.Value, startCycle, end, cancellationToken) is { } expiryError)
        {
            return expiryError;
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

        if (HasDayLimit(request.LeaveType.Value))
        {
            var balance = await BalanceForTypeAsync(
                db, targetStaff.Id, startCycle, request.LeaveType.Value, ManilaTime.Today(), cancellationToken);
            if (balance.Allotment - balance.Used - days < 0)
            {
                return Results.BadRequest(new { error = "That leave would make the balance negative." });
            }
        }

        if (await PayrollRunService.IsPeriodLockedAsync(db, targetStaff.Id, start, end, cancellationToken))
        {
            return Results.Conflict(new { error = PayrollRunService.LockedPeriodMessage });
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

        // Owner-recorded leave: who, for whom, dates, and leave type (#961 / leave.request).
        var half = request.HalfDay is null ? "" : $" half={request.HalfDay}";
        hr.Record(
            actor.Id,
            targetStaff.Id,
            HrChangeField.LeaveRequest,
            null,
            $"{request.LeaveType.Value} {start:yyyy-MM-dd}..{end:yyyy-MM-dd}{half}",
            periodFrom: start,
            periodTo: end,
            year: start.Year,
            refType: "leave_request",
            refId: row.Id);

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
        if (actor is null || !HrRoles.CanManageShopHr(actor))
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

        var mode = row.Staff.Branch?.LeaveExpirationMode ?? Branch.DefaultLeaveExpirationMode;
        var startCycle = LeaveCycle.Resolve(mode, row.Staff.HireDate, row.StartDate);
        var calendar = await LeaveDayContext.LoadAsync(db, row.StaffId, row.StartDate, row.EndDate, cancellationToken);
        var days = calendar.Count(row.StartDate, row.EndDate, row.HalfDay);
        if (request.Decision == LeaveRequestStatus.Approved)
        {
            var endCycle = LeaveCycle.Resolve(mode, row.Staff.HireDate, row.EndDate);
            if (CrossCycleError(startCycle, endCycle) is { } crossCycleError)
            {
                return crossCycleError;
            }

            if (await ExpiryErrorAsync(db, row.StaffId, row.LeaveType, startCycle, row.EndDate, cancellationToken) is { } expiryError)
            {
                return expiryError;
            }

            if (HasDayLimit(row.LeaveType))
            {
                var balance = await BalanceForTypeAsync(
                    db, row.StaffId, startCycle, row.LeaveType, ManilaTime.Today(), cancellationToken);
                if (balance.Allotment - balance.Used - days < 0)
                {
                    return Results.BadRequest(new { error = "That approval would make the balance negative." });
                }
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
            // The requester can withdraw their own pending request. Approved leave needs the shop HR desk.
            if (row.Status == LeaveRequestStatus.Approved)
            {
                return Forbidden("Ask an owner or general manager to cancel approved leave.");
            }
        }
        else
        {
            if (!HrRoles.CanManageShopHr(actor))
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

        var calendar = await LeaveDayContext.LoadAsync(db, row.StaffId, row.StartDate, row.EndDate, cancellationToken);
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

    /// <summary>Allotment, used (approved), pending, expired, and remaining working days for every leave type in a cycle.</summary>
    private static async Task<List<LeaveBalanceResponse>> BalancesAsync(
        IHrDb db,
        Guid staffId,
        LeaveCycleWindow cycle,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var allotments = await db.LeaveAllotments.AsNoTracking()
            .Where(a => a.StaffId == staffId && a.Year == cycle.CycleYear)
            .ToListAsync(cancellationToken);
        var spans = await db.LeaveRequests.AsNoTracking()
            .Where(r => r.StaffId == staffId
                && r.StartDate >= cycle.StartsOn
                && r.StartDate <= cycle.ExpiresOn
                && (r.Status == LeaveRequestStatus.Approved || r.Status == LeaveRequestStatus.Pending))
            .Select(r => new LeaveSpan(r.LeaveType, r.Status, r.StartDate, r.EndDate, r.HalfDay))
            .ToListAsync(cancellationToken);
        var calendarTo = spans.Count == 0
            ? cycle.ExpiresOn
            : spans.Max(s => s.EndDate) > cycle.ExpiresOn ? spans.Max(s => s.EndDate) : cycle.ExpiresOn;
        var calendar = await LeaveDayContext.LoadAsync(db, staffId, cycle.StartsOn, calendarTo, cancellationToken);

        var balances = new List<LeaveBalanceResponse>(LeaveTypes.Length);
        foreach (var type in LeaveTypes)
        {
            balances.Add(BalanceFor(type, allotments, spans, calendar, cycle, today));
        }

        return balances;
    }

    /// <summary>
    /// Unpaid leave has no allotment to run out of: it just isn't paid. Every other type is capped
    /// by the days the owner set.
    /// </summary>
    internal static bool HasDayLimit(LeaveType type) => type != LeaveType.Unpaid;

    /// <summary>
    /// Vacation and Sick leave forfeit on the effective expiry date.
    /// Service incentive leave (PH Labor Code Art. 95) converts to cash instead of expiring,
    /// and Unpaid leave has no expiry.
    /// </summary>
    internal static bool HasExpiry(LeaveType type) =>
        type is LeaveType.Vacation or LeaveType.Sick;

    private static async Task<LeaveBalanceResponse> BalanceForTypeAsync(
        IHrDb db,
        Guid staffId,
        LeaveCycleWindow cycle,
        LeaveType type,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var allotments = await db.LeaveAllotments.AsNoTracking()
            .Where(a => a.StaffId == staffId && a.Year == cycle.CycleYear && a.LeaveType == type)
            .ToListAsync(cancellationToken);
        var spans = await db.LeaveRequests.AsNoTracking()
            .Where(r => r.StaffId == staffId
                && r.LeaveType == type
                && r.StartDate >= cycle.StartsOn
                && r.StartDate <= cycle.ExpiresOn
                && (r.Status == LeaveRequestStatus.Approved || r.Status == LeaveRequestStatus.Pending))
            .Select(r => new LeaveSpan(r.LeaveType, r.Status, r.StartDate, r.EndDate, r.HalfDay))
            .ToListAsync(cancellationToken);
        var calendarTo = spans.Count == 0
            ? cycle.ExpiresOn
            : spans.Max(s => s.EndDate) > cycle.ExpiresOn ? spans.Max(s => s.EndDate) : cycle.ExpiresOn;
        var calendar = await LeaveDayContext.LoadAsync(db, staffId, cycle.StartsOn, calendarTo, cancellationToken);
        return BalanceFor(type, allotments, spans, calendar, cycle, today);
    }

    private static LeaveBalanceResponse BalanceFor(
        LeaveType type,
        IReadOnlyList<LeaveAllotment> allotments,
        IEnumerable<LeaveSpan> spans,
        LeaveDayContext calendar,
        LeaveCycleWindow cycle,
        DateOnly today)
    {
        var allotmentRow = allotments.FirstOrDefault(a => a.LeaveType == type);
        var allotment = allotmentRow?.Days ?? 0m;
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

        if (type == LeaveType.Unpaid)
        {
            return new LeaveBalanceResponse(
                type,
                allotment,
                used,
                pending,
                allotment - used - pending,
                ExpiresOn: null,
                Expired: 0m,
                ConvertibleToCash: false,
                UsesHireAnniversary: false,
                HasCustomExpiry: false,
                CycleYear: cycle.CycleYear);
        }

        if (type == LeaveType.ServiceIncentive)
        {
            // PH Labor Code Art. 95: SIL never expires/forfeits and cannot have a custom expiry date.
            return new LeaveBalanceResponse(
                type,
                allotment,
                used,
                pending,
                allotment - used - pending,
                ExpiresOn: cycle.ExpiresOn,
                Expired: 0m,
                ConvertibleToCash: true,
                UsesHireAnniversary: cycle.UsesHireAnniversary,
                HasCustomExpiry: false,
                CycleYear: cycle.CycleYear);
        }

        var customExpiresOn = allotmentRow?.ExpiresOn;
        var effectiveExpiresOn = LeaveCycle.EffectiveExpiresOn(cycle, customExpiresOn);
        var hasCustomExpiry = customExpiresOn is not null;

        var rawRemaining = allotment - used - pending;
        if (today > effectiveExpiresOn)
        {
            var expired = Math.Max(0m, rawRemaining);
            return new LeaveBalanceResponse(
                type,
                allotment,
                used,
                pending,
                Remaining: 0m,
                ExpiresOn: effectiveExpiresOn,
                Expired: expired,
                ConvertibleToCash: false,
                UsesHireAnniversary: cycle.UsesHireAnniversary,
                HasCustomExpiry: hasCustomExpiry,
                CycleYear: cycle.CycleYear);
        }

        return new LeaveBalanceResponse(
            type,
            allotment,
            used,
            pending,
            Remaining: rawRemaining,
            ExpiresOn: effectiveExpiresOn,
            Expired: 0m,
            ConvertibleToCash: false,
            UsesHireAnniversary: cycle.UsesHireAnniversary,
            HasCustomExpiry: hasCustomExpiry,
            CycleYear: cycle.CycleYear);
    }

    private static async Task<(LeaveExpirationMode Mode, DateOnly? HireDate)> StaffLeavePolicyAsync(
        IHrDb db,
        Guid staffId,
        CancellationToken cancellationToken)
    {
        var info = await db.Staff.AsNoTracking()
            .Where(s => s.Id == staffId)
            .Select(s => new
            {
                s.HireDate,
                Mode = s.Branch != null ? s.Branch.LeaveExpirationMode : Branch.DefaultLeaveExpirationMode
            })
            .FirstOrDefaultAsync(cancellationToken);

        return info is null
            ? (Branch.DefaultLeaveExpirationMode, null)
            : (info.Mode, info.HireDate);
    }

    private static LeaveCycleWindow ResolveCycle(
        LeaveExpirationMode mode,
        DateOnly? hireDate,
        int? explicitYear,
        DateOnly today) =>
        explicitYear is int year
            ? LeaveCycle.ForYear(mode, hireDate, year)
            : LeaveCycle.Resolve(mode, hireDate, today);

    private static IResult? CrossCycleError(LeaveCycleWindow startCycle, LeaveCycleWindow endCycle)
    {
        if (startCycle.CycleYear == endCycle.CycleYear)
        {
            return null;
        }

        if (startCycle.UsesHireAnniversary)
        {
            return Results.BadRequest(new
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Leave can't cross your leave anniversary ({endCycle.StartsOn:MMM d, yyyy}). Submit two requests: one through {startCycle.ExpiresOn:MMM d, yyyy}, and another starting {endCycle.StartsOn:MMM d, yyyy}.")
            });
        }

        return Results.BadRequest(new { error = CrossYearErrorMessage });
    }

    private static async Task<IResult?> ExpiryErrorAsync(
        IHrDb db,
        Guid staffId,
        LeaveType leaveType,
        LeaveCycleWindow cycle,
        DateOnly end,
        CancellationToken cancellationToken)
    {
        if (!HasExpiry(leaveType))
        {
            return null;
        }

        var customExpiresOn = await db.LeaveAllotments.AsNoTracking()
            .Where(a => a.StaffId == staffId && a.Year == cycle.CycleYear && a.LeaveType == leaveType)
            .Select(a => a.ExpiresOn)
            .FirstOrDefaultAsync(cancellationToken);

        var effectiveExpiresOn = LeaveCycle.EffectiveExpiresOn(cycle, customExpiresOn);
        if (end > effectiveExpiresOn)
        {
            return Results.BadRequest(new
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"That leave goes past the expiry date ({effectiveExpiresOn:MMM d, yyyy}).")
            });
        }

        return null;
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
        return new ShopContext(branchId, branch.Code, branch.LeaveExpirationMode);
    }

    private static IQueryable<Domain.Staff> OnShop(IHrDb db, Guid branchId) =>
        db.Staff.AsNoTracking().Where(s => s.BranchId == branchId);

    private static async Task<Guid?> FindOnShopAsync(
        Guid staffId,
        HttpContext http,
        BranchResolver branches,
        IHrDb db,
        CancellationToken cancellationToken)
    {
        var shop = await ShopAsync(http, branches, db, cancellationToken);
        var onShop = await OnShop(db, shop.BranchId)
            .AnyAsync(s => s.Id == staffId, cancellationToken);
        return onShop ? staffId : null;
    }

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

    private readonly record struct ShopContext(Guid BranchId, string Code, LeaveExpirationMode LeaveExpirationMode);

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
    decimal Remaining,
    DateOnly? ExpiresOn = null,
    decimal Expired = 0m,
    bool ConvertibleToCash = false,
    bool UsesHireAnniversary = false,
    bool HasCustomExpiry = false,
    int? CycleYear = null);

public sealed record StaffLeaveBalancesResponse(
    Guid StaffId,
    string DisplayName,
    string Role,
    List<LeaveBalanceResponse> Balances);

public sealed record LeaveAllotmentResponse(
    Guid StaffId,
    int Year,
    LeaveType LeaveType,
    decimal Days,
    DateOnly? ExpiresOn = null);

public sealed class UpsertLeaveAllotmentRequest
{
    public Guid StaffId { get; set; }
    public int Year { get; set; }
    public LeaveType? LeaveType { get; set; }
    public decimal? Days { get; set; }
    public DateOnly? ExpiresOn { get; set; }
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
