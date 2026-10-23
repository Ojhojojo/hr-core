using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using HrCore.Features.Attendance;
using HrCore.Features.HrChangeLog;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Staff;

public static class StaffEndpoints
{
    public static IEndpointRouteBuilder MapStaffApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/staff")
            .WithTags("Staff")
            .AddEndpointFilter<StaffAuthorizationFilter>();
        api.MapGet("/", ListStaff).WithName("ListStaff");
        api.MapPost("/", CreateStaff)
            .AddEndpointFilter(StaffRoleFilter.OwnerOnly)
            .WithName("CreateStaff");
        api.MapPost("/shop-owners", CreateShopOwner)
            .AddEndpointFilter(StaffRoleFilter.OwnerOnly)
            .WithName("CreateShopOwner");
        api.MapPost("/{id:guid}/resend-invite", ResendInvite)
            .AddEndpointFilter(StaffRoleFilter.OwnerOnly)
            .WithName("ResendStaffInvite");
        api.MapPost("/{id:guid}", UpdateStaff)
            .AddEndpointFilter(StaffRoleFilter.OwnerOnly)
            .WithName("UpdateStaff");

        // Shop-wide snapshot for one day: approved leave, clock-in. Feeds the board badge and the assignee picker.
        app.MapGet("/api/ops/staff/availability", StaffAvailability)
            .WithTags("Staff")
            .AddEndpointFilter<StaffAuthorizationFilter>()
            .AddEndpointFilter(StaffRoleFilter.OwnerOrAdmin)
            .WithName("StaffAvailability");
        return app;
    }

    private static async Task<IResult> StaffAvailability(
        string? date,
        HttpContext http,
        BranchResolver branches,
        IHrDb db,
        CancellationToken cancellationToken)
    {
        DateOnly day;
        if (string.IsNullOrWhiteSpace(date))
        {
            day = ManilaTime.Today();
        }
        else if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
        {
            return Results.BadRequest(new { error = "Date must be YYYY-MM-DD." });
        }

        var branchId = await branches.ResolveOpsAsync(http, cancellationToken);
        var staff = await db.Staff.AsNoTracking()
            .Include(s => s.Profile)
            .Where(s => s.BranchId == branchId && s.Active)
            .OrderBy(s => s.Profile.DisplayName)
            .ToListAsync(cancellationToken);
        var staffIds = staff.Select(s => s.Id).ToList();

        var leave = await db.LeaveRequests.AsNoTracking()
            .Where(r => staffIds.Contains(r.StaffId)
                && r.Status == LeaveRequestStatus.Approved
                && r.StartDate <= day
                && r.EndDate >= day)
            .Select(r => new { r.StaffId, r.LeaveType, r.HalfDay })
            .ToListAsync(cancellationToken);

        // Manila day: an open punch that started on that day means clocked in.
        var dayStart = ManilaTime.StartOfDayUtc(day);
        var dayEnd = ManilaTime.StartOfDayUtc(day.AddDays(1));
        var clockedIn = (await db.AttendancePunches.AsNoTracking()
                .Where(p => staffIds.Contains(p.StaffId)
                    && p.OutAtUtc == null
                    && p.InAtUtc >= dayStart
                    && p.InAtUtc < dayEnd)
                .Select(p => p.StaffId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var leaveByStaff = leave
            .GroupBy(r => r.StaffId)
            .ToDictionary(g => g.Key, g => g.First());

        var rows = staff.Select(s =>
        {
            leaveByStaff.TryGetValue(s.Id, out var onLeave);
            return new StaffAvailabilityResponse(
                s.Id,
                s.Profile.DisplayName,
                s.Role,
                onLeave is not null,
                onLeave?.LeaveType,
                onLeave?.HalfDay,
                clockedIn.Contains(s.Id));
        }).ToList();

        return Results.Ok(new StaffAvailabilityDayResponse(day, rows));
    }

    private static async Task<IResult> ListStaff(HttpContext http, BranchResolver branches, IHrDb db, CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        var query = db.Staff.AsNoTracking().Include(s => s.Profile).Include(s => s.Branch).AsQueryable();
        if (actor is not null && HrRoles.SeesOnlySelfInStaffList(actor.Role))
        {
            query = query.Where(s => s.Id == actor.Id);
        }
        else if (actor.IsShopScopedOwner())
        {
            query = query.Where(s => s.BranchId == actor!.BranchId);
        }
        else if (actor is null || actor.Role == StaffRole.Owner)
        {
            query = query.Where(s => s.BranchId == null || s.Branch!.Code != BranchCodes.Demo);
        }
        else
        {
            var branchId = await branches.ResolveOpsAsync(http, cancellationToken);
            query = query.Where(s => s.BranchId == branchId);
        }

        var items = await query.OrderBy(s => s.Profile.DisplayName).ToListAsync(cancellationToken);
        return Results.Ok(items.Select(s => ToResponse(s, actor)));
    }

    private static async Task<IResult> CreateStaff(
        CreateStaffRequest request,
        HttpContext http,
        BranchResolver branches,
        IHrDb db,
        IStaffInvite invite,
        HrChangeLogService hr,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return Results.BadRequest(new { error = "Email and name are required." });
        }

        var actor = http.GetStaff();
        var hasPayFields = request.PayBasis != null
            || request.RatePhp != null
            || request.OtAfterHoursPerDay != null
            || request.OtMultiplier != null;

        if (hasPayFields && actor?.Role != StaffRole.Owner)
        {
            return Results.Json(
                new { error = "Only the company owner can configure pay fields." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (request.MustClock == true && request.Role == StaffRole.Owner)
        {
            return Results.BadRequest(new { error = "Owner does not clock in." });
        }

        if (request.Role == StaffRole.Owner)
        {
            if (actor.IsShopScopedOwner())
            {
                return Results.Json(
                    new { error = "Only the company owner can invite an owner." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            if (request.BranchId is Guid shopId)
            {
                return await CreateShopScopedOwnerCoreAsync(
                    db, invite, request.Email, request.DisplayName, shopId, actor, hr, cancellationToken);
            }
        }

        Guid? homeId;
        if (actor.IsShopScopedOwner())
        {
            homeId = actor!.BranchId;
        }
        else
        {
            var fallback = request.Role == StaffRole.Owner
                ? (Guid?)null
                : await branches.ResolveOpsAsync(http, cancellationToken);
            var home = await ResolveHomeBranchAsync(db, request.BranchId, request.Role, fallback, cancellationToken);
            if (home.Error is not null)
            {
                return home.Error;
            }

            homeId = home.BranchId;
        }

        var otAfterHours = request.OtAfterHoursPerDay;
        var otMultiplier = request.OtMultiplier;
        if (request.PayBasis == PayBasis.Hourly)
        {
            otAfterHours ??= 8m;
            otMultiplier ??= 1.00m;
        }
        else if (request.PayBasis == PayBasis.Monthly)
        {
            otAfterHours = null;
            otMultiplier = null;
        }

        return ToCreatedStaff(await PersistStaffAsync(
            db, invite, request.Email, request.DisplayName, request.Role, homeId,
            request.PayBasis, request.RatePhp, otAfterHours, otMultiplier,
            actor, request.AppFields,
            request.MustClock,
            request.Mobile, request.HireDate, request.EmergencyContactName, request.EmergencyContactPhone,
            hr, cancellationToken), actor);
    }

    private static async Task<IResult> CreateShopOwner(
        CreateShopOwnerRequest request,
        HttpContext http,
        IHrDb db,
        IStaffInvite invite,
        HrChangeLogService hr,
        CancellationToken cancellationToken)
    {
        if (http.GetStaff().IsShopScopedOwner())
        {
            return Results.Json(
                new { error = "Only the company owner can add an owner for a shop." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return Results.BadRequest(new { error = "Email and name are required." });
        }

        if (request.BranchId == Guid.Empty)
        {
            return Results.BadRequest(new { error = "Pick a Franchise shop." });
        }

        return await CreateShopScopedOwnerCoreAsync(
            db, invite, request.Email, request.DisplayName, request.BranchId, http.GetStaff(), hr, cancellationToken);
    }

    /// <summary>
    /// Deactivation runs in one transaction with the guard (open jobs block it) and the side effects
    /// (pending leave cancelled, open punch closed). Other edits pass straight through.
    /// </summary>
    private static async Task<IResult> UpdateStaff(
        Guid id,
        UpdateStaffRequest request,
        HttpContext http,
        IHrDb db,
        HrChangeLogService hr,
        AttendanceService attendance,
        IStaffWorkHooks work,
        CancellationToken cancellationToken)
    {
        var deactivating = request.Active is false
            && await db.Staff.AsNoTracking().Where(s => s.Id == id).Select(s => s.Active).FirstOrDefaultAsync(cancellationToken);
        await using var tx = deactivating ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        if (deactivating)
        {
            var blocking = await work.LockAndListOpenWorkAsync(id, cancellationToken);
            if (blocking.Count > 0)
            {
                return Results.Conflict(new { error = work.BlockedDeactivationMessage, jobs = blocking });
            }
        }

        var result = await UpdateStaffCore(id, request, http, db, hr, cancellationToken);
        if (deactivating && result is IStatusCodeHttpResult { StatusCode: >= 200 and < 300 })
        {
            var staff = await db.Staff.FirstAsync(s => s.Id == id, cancellationToken);
            await StaffDeactivation.ApplyAsync(
                db,
                staff,
                http.GetStaff()?.Id ?? id,
                hr,
                attendance,
                DateTime.UtcNow,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        return result;
    }

    private static async Task<IResult> UpdateStaffCore(
        Guid id,
        UpdateStaffRequest request,
        HttpContext http,
        IHrDb db,
        HrChangeLogService hr,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        var staff = await db.Staff.Include(s => s.Profile).Include(s => s.Branch)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (staff is null)
        {
            return Results.NotFound();
        }

        var lengthError = StaffFieldLengthError(request);
        if (lengthError is not null)
        {
            return lengthError;
        }

        var restDays = RestDayList.ParseUpdate(request.RestDays, request.ClearRestDays);
        if (restDays.Error is not null)
        {
            return Results.BadRequest(new { error = restDays.Error });
        }

        var before = StaffAuditSnapshot.Capture(staff);

        var nextRole = request.Role ?? staff.Role;
        if (request.MustClock == true && nextRole == StaffRole.Owner)
        {
            return Results.BadRequest(new { error = "Owner does not clock in." });
        }

        var hasPayEdits = request.PayBasis != null
            || request.RatePhp != null
            || request.OtAfterHoursPerDay != null
            || request.OtMultiplier != null
            || request.ClearPayBasis
            || request.ClearRate;

        if (hasPayEdits && actor?.Role != StaffRole.Owner)
        {
            return Results.Json(
                new { error = "Only the company owner can configure pay fields." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (actor.IsShopScopedOwner())
        {
            if (staff.BranchId != actor!.BranchId)
            {
                return Results.Json(
                    new { error = "That person is not on your shop." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            if (request.ClearHomeBranch)
            {
                return Results.BadRequest(new { error = "Home branch cannot be cleared." });
            }

            var requestedRole = request.Role ?? staff.Role;
            if (requestedRole == StaffRole.Owner && staff.Role != StaffRole.Owner)
            {
                return Results.Json(
                    new { error = "Only the company owner can invite an owner." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            staff.BranchId = actor.BranchId;
            staff.Role = requestedRole == StaffRole.Owner ? staff.Role : requestedRole;
            await ApplyStaffEditsAsync(staff, request, actor, db, hr, before, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await db.Entry(staff).Reference(s => s.Branch).LoadAsync(cancellationToken);
            return Results.Ok(ToResponse(staff, actor));
        }

        var role = request.Role ?? staff.Role;
        if (request.ClearHomeBranch)
        {
            if (role != StaffRole.Owner)
            {
                return Results.BadRequest(new { error = "Home branch is required." });
            }

            if (staff.IsShopScopedOwner() && role == StaffRole.Owner)
            {
                return Results.BadRequest(new { error = "Home branch cannot be cleared." });
            }

            staff.BranchId = null;
        }
        else if (request.BranchId is Guid requested)
        {
            if (role == StaffRole.Owner)
            {
                var shop = await db.Branches.AsNoTracking()
                    .FirstOrDefaultAsync(b => b.Id == requested, cancellationToken);
                var shopError = ShopOwnerBranchError(shop);
                if (shopError is not null)
                {
                    return shopError;
                }

                staff.BranchId = requested;
            }
            else
            {
                var home = await ResolveHomeBranchAsync(db, requested, role, fallbackForNonOwner: null, cancellationToken);
                if (home.Error is not null)
                {
                    return home.Error;
                }

                staff.BranchId = home.BranchId;
            }
        }

        if (role != StaffRole.Owner && staff.BranchId is null)
        {
            return Results.BadRequest(new { error = "Home branch is required." });
        }

        if (role == StaffRole.Owner && staff.BranchId is Guid ownerHome && ownerHome != PlatformIds.DemoBranchId)
        {
            var shop = await db.Branches.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == ownerHome, cancellationToken);
            var shopError = ShopOwnerBranchError(shop);
            if (shopError is not null)
            {
                return shopError;
            }
        }

        staff.Role = role;
        await ApplyStaffEditsAsync(staff, request, actor, db, hr, before, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await db.Entry(staff).Reference(s => s.Branch).LoadAsync(cancellationToken);
        return Results.Ok(ToResponse(staff, actor));
    }

    private static async Task<IResult> ResendInvite(
        Guid id,
        HttpContext http,
        IHrDb db,
        IStaffInvite invite,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        var staff = await db.Staff.Include(s => s.Profile).Include(s => s.Branch)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (staff is null)
        {
            return Results.NotFound();
        }

        if (actor.IsShopScopedOwner() && staff.BranchId != actor!.BranchId)
        {
            return Results.Json(
                new { error = "That person is not on your shop." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(staff.Profile.Email))
        {
            return Results.BadRequest(new { error = "That person has no email to invite." });
        }

        var inviteResult = await invite.InviteAsync(staff.Profile.Email, staff.Profile.DisplayName, cancellationToken);
        if (!string.IsNullOrWhiteSpace(inviteResult.AuthUserId)
            && (string.IsNullOrWhiteSpace(staff.Profile.AuthUserId)
                || !string.Equals(staff.Profile.AuthUserId, inviteResult.AuthUserId, StringComparison.Ordinal)))
        {
            staff.Profile.AuthUserId = inviteResult.AuthUserId;
            await db.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(ToResponse(staff, actor, inviteResult.EmailSent, inviteResult.Note));
    }

    internal static StaffResponse ToResponse(
        Domain.Staff staff,
        Domain.Staff? actor = null,
        bool? inviteSent = null,
        string? inviteNote = null)
    {
        var canViewPay = actor?.Role == StaffRole.Owner;
        return new(
            staff.Id,
            staff.ProfileId,
            staff.BranchId,
            staff.Branch?.Code,
            staff.Branch?.Name,
            staff.Role,
            staff.Active,
            staff.Profile.Email,
            staff.Profile.DisplayName,
            canViewPay ? staff.PayBasis : null,
            canViewPay ? staff.RatePhp : null,
            canViewPay ? staff.OtAfterHoursPerDay : null,
            canViewPay ? staff.OtMultiplier : null,
            inviteSent,
            inviteNote,
            string.IsNullOrWhiteSpace(staff.Profile.AuthUserId),
            staff.MustClock,
            staff.HireDate,
            staff.Profile.Mobile,
            staff.EmergencyContactName,
            staff.EmergencyContactPhone,
            RestDayList.ToArray(staff.RestDays))
        {
            AppFields = StaffAccessFlags.Describe(staff)
        };
    }

    internal static StaffResponse ToResponse(
        Domain.Staff staff,
        bool? inviteSent,
        string? inviteNote) =>
        ToResponse(staff, actor: null, inviteSent, inviteNote);

    private static async Task<IResult> CreateShopScopedOwnerCoreAsync(
        IHrDb db,
        IStaffInvite invite,
        string email,
        string displayName,
        Guid branchId,
        Domain.Staff? actor,
        HrChangeLogService hr,
        CancellationToken cancellationToken)
    {
        var branch = await db.Branches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == branchId, cancellationToken);
        var error = ShopOwnerBranchError(branch);
        if (error is not null)
        {
            return error;
        }

        return ToCreatedStaff(await PersistStaffAsync(
            db, invite, email, displayName, StaffRole.Owner, branchId,
            payBasis: null, ratePhp: null, otAfterHoursPerDay: null, otMultiplier: null,
            actor: actor, cancellationToken: cancellationToken, hr: hr), actor);
    }

    /// <summary>Same limits the employee directory uses, for the directory fields on this edit path.</summary>
    private static IResult? StaffFieldLengthError(UpdateStaffRequest request)
    {
        if (request.Position is not null && request.Position.Trim().Length > 100)
        {
            return Results.BadRequest(new { error = "Position must be 100 characters or fewer." });
        }

        if (request.Department is not null && request.Department.Trim().Length > 100)
        {
            return Results.BadRequest(new { error = "Department must be 100 characters or fewer." });
        }

        if (request.EmployeeNo is not null && request.EmployeeNo.Trim().Length > 100)
        {
            return Results.BadRequest(new { error = "Employee number must be 100 characters or fewer." });
        }

        if (request.EmergencyContactName is not null && request.EmergencyContactName.Trim().Length > 200)
        {
            return Results.BadRequest(new { error = "Emergency contact name must be 200 characters or fewer." });
        }

        if (request.EmergencyContactPhone is not null && request.EmergencyContactPhone.Trim().Length > 40)
        {
            return Results.BadRequest(new { error = "Emergency contact phone must be 40 characters or fewer." });
        }

        return StatutoryLengthError(request.Tin, "TIN")
            ?? StatutoryLengthError(request.Sss, "SSS")
            ?? StatutoryLengthError(request.PhilHealth, "PhilHealth")
            ?? StatutoryLengthError(request.PagIbig, "Pag-IBIG");
    }

    private static IResult? StatutoryLengthError(string? value, string label)
    {
        if (value is not null && value.Trim().Length > 40)
        {
            return Results.BadRequest(new { error = $"{label} must be 40 characters or fewer." });
        }

        return null;
    }

    internal static IResult? ShopOwnerBranchError(Branch? branch)
    {
        if (branch is null
            || branch.Code == BranchCodes.Demo
            || branch.OrganizationId != PlatformIds.OrganizationId)
        {
            return Results.BadRequest(new { error = "Home branch must be one of your shops." });
        }

        if (branch.Id == PlatformIds.BranchId || branch.Code == BranchCodes.Roosevelt)
        {
            return Results.BadRequest(new { error = "The default shop (Roosevelt) cannot have a shop owner." });
        }

        if (branch.Kind != BranchKind.Franchise)
        {
            return Results.BadRequest(new { error = "A shop owner can only be added for a Franchise shop." });
        }

        return null;
    }

    private static IResult ToCreatedStaff(StaffPersistResult persisted, Domain.Staff? actor)
    {
        if (persisted.Error is not null)
        {
            return persisted.Error;
        }

        var staff = persisted.Staff!;
        return Results.Created(
            $"/api/staff/{staff.Id}",
            ToResponse(staff, actor, persisted.InviteSent, persisted.InviteNote));
    }

    internal static async Task<StaffPersistResult> PersistStaffAsync(
        IHrDb db,
        IStaffInvite invite,
        string email,
        string displayName,
        StaffRole role,
        Guid? branchId,
        PayBasis? payBasis = null,
        decimal? ratePhp = null,
        decimal? otAfterHoursPerDay = null,
        decimal? otMultiplier = null,
        Domain.Staff? actor = null,
        IReadOnlyDictionary<string, JsonElement>? appFields = null,
        bool? mustClock = null,
        string? mobile = null,
        DateOnly? hireDate = null,
        string? emergencyContactName = null,
        string? emergencyContactPhone = null,
        HrChangeLogService? hr = null,
        CancellationToken cancellationToken = default)
    {
        var emergencyName = BlankToNull(emergencyContactName);
        var emergencyPhone = BlankToNull(emergencyContactPhone);
        if (emergencyName is { Length: > 200 })
        {
            return new StaffPersistResult(
                Results.BadRequest(new { error = "Emergency contact name must be 200 characters or fewer." }),
                null, null, null);
        }

        if (emergencyPhone is { Length: > 40 })
        {
            return new StaffPersistResult(
                Results.BadRequest(new { error = "Emergency contact phone must be 40 characters or fewer." }),
                null, null, null);
        }

        string? mobileValue = null;
        if (mobile is not null)
        {
            mobileValue = mobile.Trim();
            if (mobileValue.Length > 40)
            {
                return new StaffPersistResult(
                    Results.BadRequest(new { error = "Mobile must be 40 characters or fewer." }),
                    null, null, null);
            }
        }

        var normalized = email.Trim().ToLowerInvariant();
        var existing = await db.Profiles.FirstOrDefaultAsync(p => p.Email == normalized, cancellationToken);
        Profile profile;
        if (existing is null)
        {
            profile = new Profile
            {
                Id = Guid.NewGuid(),
                Kind = ProfileKind.Staff,
                Email = normalized,
                DisplayName = displayName.Trim(),
                Mobile = string.IsNullOrEmpty(mobileValue) ? null : mobileValue,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Profiles.Add(profile);
        }
        else
        {
            profile = existing;
            profile.Kind = ProfileKind.Staff;
            profile.DisplayName = displayName.Trim();
            if (mobile is not null)
            {
                profile.Mobile = string.IsNullOrEmpty(mobileValue) ? null : mobileValue;
            }
        }

        if (await db.Staff.AnyAsync(s => s.ProfileId == profile.Id, cancellationToken))
        {
            return new StaffPersistResult(Results.Conflict(new { error = "That email is already on staff." }), null, null, null);
        }

        if (actor?.Role != StaffRole.Owner)
        {
            payBasis = null;
            ratePhp = null;
            otAfterHoursPerDay = null;
            otMultiplier = null;
        }

        if (payBasis == PayBasis.Hourly)
        {
            otAfterHoursPerDay ??= 8m;
            otMultiplier ??= 1.00m;
        }
        else if (payBasis == PayBasis.Monthly)
        {
            otAfterHoursPerDay = null;
            otMultiplier = null;
        }

        if (role == StaffRole.Owner && mustClock == true)
        {
            return new StaffPersistResult(Results.BadRequest(new { error = "Owner does not clock in." }), null, null, null);
        }

        var clock = role == StaffRole.Owner
            ? false
            : mustClock ?? HrRoles.ClocksByDefault(role);

        var staff = new Domain.Staff
        {
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            BranchId = branchId,
            Role = role,
            Active = true,
            PayBasis = payBasis,
            RatePhp = ratePhp,
            OtAfterHoursPerDay = otAfterHoursPerDay,
            OtMultiplier = otMultiplier,
            MustClock = clock,
            HireDate = hireDate,
            EmergencyContactName = emergencyName,
            EmergencyContactPhone = emergencyPhone,
            CreatedAtUtc = DateTime.UtcNow
        };
        StaffAccessFlags.ApplyOnCreate(staff, appFields);
        db.Staff.Add(staff);
        await db.SaveChangesAsync(cancellationToken);

        // Create trail: every value the create set is a new value.
        var log = hr ?? new HrChangeLogService(db);
        StaffAudit.RecordChanges(log, actor?.Id, staff, StaffAuditSnapshot.Empty);
        await StaffAudit.SyncRateHistoryAsync(db, staff, actor?.Id, payEdited: true, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var inviteResult = await invite.InviteAsync(normalized, profile.DisplayName, cancellationToken);
        if (!string.IsNullOrWhiteSpace(inviteResult.AuthUserId)
            && (string.IsNullOrWhiteSpace(profile.AuthUserId)
                || !string.Equals(profile.AuthUserId, inviteResult.AuthUserId, StringComparison.Ordinal)))
        {
            profile.AuthUserId = inviteResult.AuthUserId;
            await db.SaveChangesAsync(cancellationToken);
        }

        staff.Profile = profile;
        if (staff.BranchId is not null)
        {
            await db.Entry(staff).Reference(s => s.Branch).LoadAsync(cancellationToken);
        }

        return new StaffPersistResult(null, staff, inviteResult.EmailSent, inviteResult.Note);
    }

    private static string? BlankToNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static async Task ApplyStaffEditsAsync(
        Domain.Staff staff,
        UpdateStaffRequest request,
        Domain.Staff? actor,
        IHrDb db,
        HrChangeLogService hr,
        StaffAuditSnapshot before,
        CancellationToken cancellationToken)
    {
        if (request.Active is not null)
        {
            staff.Active = request.Active.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            staff.Profile.DisplayName = request.DisplayName.Trim();
        }

        ApplyDirectoryFields(staff, request);

        if (actor?.Role != StaffRole.Owner)
        {
            StaffAudit.RecordChanges(hr, actor?.Id, staff, before);
            return;
        }

        if (request.ClearPayBasis)
        {
            staff.PayBasis = null;
        }
        else if (request.PayBasis is not null)
        {
            staff.PayBasis = request.PayBasis;
        }

        if (request.ClearRate)
        {
            staff.RatePhp = null;
        }
        else if (request.RatePhp is not null)
        {
            staff.RatePhp = request.RatePhp;
        }

        if (request.OtAfterHoursPerDay is not null)
        {
            staff.OtAfterHoursPerDay = request.OtAfterHoursPerDay;
        }

        if (request.OtMultiplier is not null)
        {
            staff.OtMultiplier = request.OtMultiplier;
        }

        if (staff.PayBasis == PayBasis.Hourly)
        {
            staff.OtAfterHoursPerDay ??= 8m;
            staff.OtMultiplier ??= 1.00m;
        }
        else if (staff.PayBasis == PayBasis.Monthly)
        {
            staff.OtAfterHoursPerDay = null;
            staff.OtMultiplier = null;
        }

        var payEdited = request.PayBasis is not null
            || request.RatePhp is not null
            || request.OtAfterHoursPerDay is not null
            || request.OtMultiplier is not null
            || request.ClearPayBasis
            || request.ClearRate;

        StaffAccessFlags.ApplyOnUpdate(staff, request.AppFields);

        if (staff.Role == StaffRole.Owner)
        {
            staff.MustClock = false;
        }
        else if (request.MustClock is bool mustClock)
        {
            staff.MustClock = mustClock;
        }

        StaffAudit.RecordChanges(hr, actor?.Id, staff, before);
        await StaffAudit.SyncRateHistoryAsync(db, staff, actor?.Id, payEdited, cancellationToken);
    }

    /// <summary>
    /// Directory fields on the same edit path. Null means "leave as is"; a blank string clears.
    /// </summary>
    private static void ApplyDirectoryFields(Domain.Staff staff, UpdateStaffRequest request)
    {
        if (request.HireDate is DateOnly hireDate)
        {
            staff.HireDate = hireDate;
        }

        if (request.Position is not null)
        {
            staff.Position = BlankToNull(request.Position);
        }

        if (request.Department is not null)
        {
            staff.Department = BlankToNull(request.Department);
        }

        if (request.EmployeeNo is not null)
        {
            staff.EmployeeNo = BlankToNull(request.EmployeeNo);
        }

        if (request.EmergencyContactName is not null)
        {
            staff.EmergencyContactName = BlankToNull(request.EmergencyContactName);
        }

        if (request.EmergencyContactPhone is not null)
        {
            staff.EmergencyContactPhone = BlankToNull(request.EmergencyContactPhone);
        }

        if (request.Tin is not null)
        {
            staff.Tin = BlankToNull(request.Tin);
        }

        if (request.Sss is not null)
        {
            staff.Sss = BlankToNull(request.Sss);
        }

        if (request.PhilHealth is not null)
        {
            staff.PhilHealth = BlankToNull(request.PhilHealth);
        }

        if (request.PagIbig is not null)
        {
            staff.PagIbig = BlankToNull(request.PagIbig);
        }

        var restDays = RestDayList.ParseUpdate(request.RestDays, request.ClearRestDays);
        if (restDays.Changed)
        {
            staff.RestDays = restDays.Stored;
        }
    }

    private static async Task<(Guid? BranchId, Branch? Branch, IResult? Error)> ResolveHomeBranchAsync(
        IHrDb db,
        Guid? requested,
        StaffRole role,
        Guid? fallbackForNonOwner,
        CancellationToken cancellationToken)
    {
        if (requested is Guid id)
        {
            var branch = await OwnedHomeBranchAsync(db, id, cancellationToken);
            if (branch is null)
            {
                return (null, null, Results.BadRequest(new { error = "Home branch must be one of your shops." }));
            }

            return (branch.Id, branch, null);
        }

        if (role == StaffRole.Owner)
        {
            return (null, null, null);
        }

        if (fallbackForNonOwner is Guid fallback)
        {
            var fallbackBranch = await db.Branches.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == fallback, cancellationToken);
            return (fallback, fallbackBranch, null);
        }

        return (null, null, Results.BadRequest(new { error = "Home branch is required." }));
    }

    private static Task<Branch?> OwnedHomeBranchAsync(IHrDb db, Guid id, CancellationToken cancellationToken) =>
        db.Branches.AsNoTracking().FirstOrDefaultAsync(
            b => b.Id == id && b.OrganizationId == PlatformIds.OrganizationId && b.Code != BranchCodes.Demo,
            cancellationToken);

    public sealed class CreateStaffRequest
    {
        public string Email { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public StaffRole Role { get; set; } = StaffRole.Admin;
        public Guid? BranchId { get; set; }
        public PayBasis? PayBasis { get; set; }
        public decimal? RatePhp { get; set; }
        public decimal? OtAfterHoursPerDay { get; set; }
        public decimal? OtMultiplier { get; set; }
        public bool? MustClock { get; set; }
        public string? Mobile { get; set; }
        public DateOnly? HireDate { get; set; }
        public string? EmergencyContactName { get; set; }
        public string? EmergencyContactPhone { get; set; }

        /// <summary>The app's own access flags (see <see cref="StaffAccessFlags"/>), sent as top-level fields.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AppFields { get; set; }
    }

    public sealed class CreateShopOwnerRequest
    {
        public string Email { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public Guid BranchId { get; set; }
    }

    public sealed class UpdateStaffRequest
    {
        public StaffRole? Role { get; set; }
        public bool? Active { get; set; }
        public string? DisplayName { get; set; }
        public Guid? BranchId { get; set; }
        public bool ClearHomeBranch { get; set; }
        public PayBasis? PayBasis { get; set; }
        public decimal? RatePhp { get; set; }
        public decimal? OtAfterHoursPerDay { get; set; }
        public decimal? OtMultiplier { get; set; }
        public bool ClearPayBasis { get; set; }
        public bool ClearRate { get; set; }
        public bool? MustClock { get; set; }
        public DateOnly? HireDate { get; set; }
        public string? Position { get; set; }
        public string? Department { get; set; }
        public string? EmployeeNo { get; set; }
        public string? EmergencyContactName { get; set; }
        public string? EmergencyContactPhone { get; set; }
        public string? Tin { get; set; }
        public string? Sss { get; set; }
        public string? PhilHealth { get; set; }
        public string? PagIbig { get; set; }
        public int[]? RestDays { get; set; }
        public bool ClearRestDays { get; set; }

        /// <summary>The app's own access flags (see <see cref="StaffAccessFlags"/>), sent as top-level fields.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AppFields { get; set; }
    }

    public sealed record StaffResponse(
        Guid Id,
        Guid ProfileId,
        Guid? BranchId,
        string? BranchCode,
        string? BranchName,
        StaffRole Role,
        bool Active,
        string? Email,
        string DisplayName,
        PayBasis? PayBasis = null,
        decimal? RatePhp = null,
        decimal? OtAfterHoursPerDay = null,
        decimal? OtMultiplier = null,
        bool? InviteSent = null,
        string? InviteNote = null,
        bool WaitingForLogin = false,
        bool MustClock = false,
        DateOnly? HireDate = null,
        string? Mobile = null,
        string? EmergencyContactName = null,
        string? EmergencyContactPhone = null,
        int[]? RestDays = null)
    {
        /// <summary>The app's own access flags (see <see cref="StaffAccessFlags"/>), written as top-level fields.</summary>
        [JsonExtensionData]
        public Dictionary<string, object?>? AppFields { get; init; }
    }

    internal sealed record StaffPersistResult(
        IResult? Error,
        Domain.Staff? Staff,
        bool? InviteSent,
        string? InviteNote);

    /// <summary>
    /// Auditable staff values. Null means "no previous value" (a create), so every field the
    /// create or edit set produces one HR trail row.
    /// </summary>
    internal sealed record StaffAuditSnapshot(
        StaffRole? Role,
        bool? Active,
        PayBasis? PayBasis,
        decimal? RatePhp,
        decimal? OtAfterHoursPerDay,
        decimal? OtMultiplier,
        DateOnly? HireDate,
        string? Position,
        string? Department,
        string? EmployeeNo,
        string? EmergencyContactName,
        string? EmergencyContactPhone,
        string? Tin,
        string? Sss,
        string? PhilHealth,
        string? PagIbig,
        bool? MustClock,
        string? RestDays)
    {
        public static StaffAuditSnapshot Empty { get; } =
            new(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);

        public static StaffAuditSnapshot Capture(Domain.Staff staff) =>
            new(
                staff.Role,
                staff.Active,
                staff.PayBasis,
                staff.RatePhp,
                staff.OtAfterHoursPerDay,
                staff.OtMultiplier,
                staff.HireDate,
                staff.Position,
                staff.Department,
                staff.EmployeeNo,
                staff.EmergencyContactName,
                staff.EmergencyContactPhone,
                staff.Tin,
                staff.Sss,
                staff.PhilHealth,
                staff.PagIbig,
                staff.MustClock,
                staff.RestDays);
    }

    /// <summary>Writes the append-only HR trail for a staff create or edit.</summary>
    internal static class StaffAudit
    {
        private const int MaxValueLength = 200;

        public static void RecordChanges(
            HrChangeLogService hr,
            Guid? actorStaffId,
            Domain.Staff staff,
            StaffAuditSnapshot before)
        {
            var subject = staff.Id;
            hr.Record(actorStaffId, subject, HrChangeField.StaffRole, Text(before.Role), Text(staff.Role));
            hr.Record(actorStaffId, subject, HrChangeField.StaffActive, Text(before.Active), Text(staff.Active));
            hr.Record(
                actorStaffId, subject, HrChangeField.StaffPayBasis, Text(before.PayBasis), Text(staff.PayBasis));
            hr.RecordMoney(actorStaffId, subject, HrChangeField.StaffRate, before.RatePhp, staff.RatePhp);
            hr.RecordMoney(
                actorStaffId,
                subject,
                HrChangeField.StaffOtAfterHours,
                before.OtAfterHoursPerDay,
                staff.OtAfterHoursPerDay);
            hr.RecordMoney(
                actorStaffId,
                subject,
                HrChangeField.StaffOtMultiplier,
                before.OtMultiplier,
                staff.OtMultiplier);
            hr.Record(
                actorStaffId, subject, HrChangeField.StaffHireDate, Text(before.HireDate), Text(staff.HireDate));
            hr.Record(actorStaffId, subject, HrChangeField.StaffPosition, before.Position, staff.Position);
            hr.Record(actorStaffId, subject, HrChangeField.StaffDepartment, before.Department, staff.Department);
            hr.Record(actorStaffId, subject, HrChangeField.StaffEmployeeNo, before.EmployeeNo, staff.EmployeeNo);
            hr.Record(
                actorStaffId,
                subject,
                HrChangeField.StaffEmergencyContact,
                Emergency(before),
                Emergency(staff));
            hr.Record(actorStaffId, subject, HrChangeField.StaffTin, before.Tin, staff.Tin);
            hr.Record(actorStaffId, subject, HrChangeField.StaffSss, before.Sss, staff.Sss);
            hr.Record(actorStaffId, subject, HrChangeField.StaffPhilHealth, before.PhilHealth, staff.PhilHealth);
            hr.Record(actorStaffId, subject, HrChangeField.StaffPagIbig, before.PagIbig, staff.PagIbig);
            hr.Record(
                actorStaffId, subject, HrChangeField.StaffMustClock, Text(before.MustClock), Text(staff.MustClock));
            hr.Record(actorStaffId, subject, HrChangeField.StaffRestDays, before.RestDays, staff.RestDays);
        }

        /// <summary>
        /// Keeps the dated rate history in step with the current snapshot when an edit touches pay.
        /// The row for the effective date is corrected in place; otherwise one row is appended.
        /// A cleared rate leaves history alone, so past periods keep their price.
        /// </summary>
        public static async Task SyncRateHistoryAsync(
            IHrDb db,
            Domain.Staff staff,
            Guid? actorStaffId,
            bool payEdited,
            CancellationToken cancellationToken)
        {
            if (!payEdited || staff.PayBasis is not PayBasis basis || staff.RatePhp is not decimal rate)
            {
                return;
            }

            var effectiveFrom = staff.HireDate ?? ManilaTime.Today();
            var existing = await db.StaffPayRates
                .FirstOrDefaultAsync(
                    r => r.StaffId == staff.Id && r.EffectiveFrom == effectiveFrom,
                    cancellationToken);
            if (existing is null)
            {
                db.StaffPayRates.Add(new StaffPayRate
                {
                    Id = Guid.NewGuid(),
                    StaffId = staff.Id,
                    EffectiveFrom = effectiveFrom,
                    Basis = basis,
                    RatePhp = rate,
                    OtAfterHoursPerDay = staff.OtAfterHoursPerDay,
                    OtMultiplier = staff.OtMultiplier,
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedByStaffId = actorStaffId,
                    Reason = "Staff record edit"
                });
                return;
            }

            existing.Basis = basis;
            existing.RatePhp = rate;
            existing.OtAfterHoursPerDay = staff.OtAfterHoursPerDay;
            existing.OtMultiplier = staff.OtMultiplier;
        }

        private static string? Emergency(StaffAuditSnapshot snapshot) =>
            Emergency(snapshot.EmergencyContactName, snapshot.EmergencyContactPhone);

        private static string? Emergency(Domain.Staff staff) =>
            Emergency(staff.EmergencyContactName, staff.EmergencyContactPhone);

        private static string? Emergency(string? name, string? phone)
        {
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(phone))
            {
                return null;
            }

            return Truncate($"{name} / {phone}".Trim());
        }

        private static string? Text(object? value) => value switch
        {
            null => null,
            bool flag => flag ? "true" : "false",
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => Truncate(value.ToString())
        };

        private static string? Truncate(string? value) =>
            value is { Length: > MaxValueLength } ? value[..MaxValueLength] : value;
    }
}

public sealed record StaffAvailabilityDayResponse(DateOnly Date, List<StaffAvailabilityResponse> Staff);

/// <summary>
/// One active shop member on one day. <see cref="LeaveType"/> and <see cref="HalfDay"/> are set only when an
/// approved leave covers the day. <see cref="ClockedIn"/> is true when a punch that started on that Manila day is still open.
/// </summary>
public sealed record StaffAvailabilityResponse(
    Guid StaffId,
    string DisplayName,
    StaffRole Role,
    bool OnApprovedLeave,
    LeaveType? LeaveType,
    LeaveHalfDay? HalfDay,
    bool ClockedIn);
