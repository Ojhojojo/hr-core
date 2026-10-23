using System.Text.Json.Serialization;
using HrCore.Features.Attendance;
using HrCore.Features.HrChangeLog;
using HrCore.Features.Staff;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.EmployeeDirectory;

public static class EmployeeDirectoryEndpoints
{
    public const int EmergencyNameMaxLength = 200;
    public const int EmergencyPhoneMaxLength = 40;
    public const int MobileMaxLength = 40;
    public const int PositionMaxLength = 100;
    public const int DepartmentMaxLength = 100;
    public const int EmployeeNoMaxLength = 100;
    public const int StatutoryIdMaxLength = 40;
    public const int RegisteredAddressMaxLength = 300;
    public const int ZipCodeMaxLength = 10;
    public const string HealthMissingHireDate = "missing_hire_date";
    public const string HealthMissingPayRate = "missing_pay_rate";
    public const string HealthMustClockNoRate = "must_clock_no_rate";
    public const string HealthBelowMinimumWage = "below_minimum_wage";
    public const string HealthMissingStatutoryIds = "missing_statutory_ids";
    public const string HealthSilMissingAllotment = "sil_missing_allotment";

    public static IEndpointRouteBuilder MapEmployeeDirectoryApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/employee-directory")
            .WithTags("Employee directory")
            .AddEndpointFilter<StaffAuthorizationFilter>();
        api.MapGet("/", List)
            .AddEndpointFilter(StaffRoleFilter.OwnerOrAdmin)
            .WithName("ListEmployeeDirectory");
        api.MapGet("/{id:guid}", GetById).WithName("GetEmployeeDirectoryPerson");
        api.MapPost("/", Create)
            .AddEndpointFilter(StaffRoleFilter.OwnerOnly)
            .WithName("CreateEmployeeDirectoryPerson");
        api.MapPatch("/{id:guid}", Update).WithName("UpdateEmployeeDirectoryPerson");
        return app;
    }

    private static async Task<IResult> List(
        HttpContext http,
        BranchResolver branches,
        IHrDb db,
        CancellationToken cancellationToken,
        bool includeInactive = false,
        string? search = null)
    {
        var branchId = await branches.ResolveOpsAsync(http, cancellationToken);
        var branch = await db.Branches.AsNoTracking()
            .FirstAsync(b => b.Id == branchId, cancellationToken);
        var scoped = db.Staff.AsNoTracking()
            .Include(s => s.Profile)
            .Include(s => s.Branch)
            .Where(s => s.BranchId == branchId);
        // Current ops shop only. When that shop is not Demo, also drop Demo people.
        if (!string.Equals(branch.Code, BranchCodes.Demo, StringComparison.OrdinalIgnoreCase))
        {
            scoped = scoped.Where(s => s.Branch!.Code != BranchCodes.Demo);
        }

        var headcount = await scoped.CountAsync(s => s.Active, cancellationToken);
        var query = scoped;
        if (!includeInactive)
        {
            query = query.Where(s => s.Active);
        }

        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim().ToLowerInvariant();
        if (term is not null)
        {
            query = query.Where(s =>
                s.Profile.DisplayName.ToLower().Contains(term)
                || (s.Profile.Mobile != null && s.Profile.Mobile.ToLower().Contains(term)));
        }

        var items = await query
            .OrderBy(s => s.Profile.DisplayName)
            .ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);
        var actor = http.GetStaff();
        var canManage = actor?.Role is StaffRole.Owner or StaffRole.Admin;
        var revealPayAndAccess = actor?.Role == StaffRole.Owner;
        var onLeave = await OnLeaveTodayIdsAsync(db, items.Select(s => s.Id).ToArray(), cancellationToken);
        var cards = items.Select(s => ToCard(
            s,
            onLeave.Contains(s.Id),
            revealStatutoryIds: canManage || (actor is not null && s.Id == actor.Id),
            revealPayAndAccess)).ToList();
        var today = ManilaTime.Today();
        var silAllotted = await db.LeaveAllotments.AsNoTracking()
            .Where(a => a.LeaveType == LeaveType.ServiceIncentive && a.Year == today.Year)
            .Select(a => a.StaffId)
            .ToListAsync(cancellationToken);
        var health = canManage
            ? BuildConfigHealth(items, branch.MinimumDailyWagePhp, silAllotted.ToHashSet(), today)
            : null;
        return Results.Ok(new EmployeeDirectoryListResponse(headcount, cards, health));
    }

    /// <summary>The caller's own finalised scorecards. Any staff role; never another person's, never a draft or reopened card.</summary>
    private static async Task<IResult> GetById(
        Guid id,
        HttpContext http,
        BranchResolver branches,
        IHrDb db,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        if (!HrRoles.IsDesk(actor.Role) && actor.Id != id)
        {
            return Forbidden("You cannot view this employee record.");
        }

        var branchId = await branches.ResolveOpsAsync(http, cancellationToken);
        var staff = await db.Staff.AsNoTracking()
            .Include(s => s.Profile)
            .Include(s => s.Branch)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (staff is null || staff.BranchId != branchId)
        {
            return Results.NotFound();
        }

        var branch = await db.Branches.AsNoTracking()
            .FirstAsync(b => b.Id == branchId, cancellationToken);
        if (!string.Equals(branch.Code, BranchCodes.Demo, StringComparison.OrdinalIgnoreCase)
            && string.Equals(staff.Branch?.Code, BranchCodes.Demo, StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }

        var onLeave = await IsOnLeaveTodayAsync(db, staff.Id, cancellationToken);
        var canManage = actor.Role is StaffRole.Owner or StaffRole.Admin;
        return Results.Ok(ToCard(
            staff,
            onLeave,
            revealStatutoryIds: canManage || staff.Id == actor.Id,
            revealPayAndAccess: actor.Role == StaffRole.Owner));
    }

    private static async Task<IResult> Create(
        CreateEmployeeDirectoryRequest request,
        HttpContext http,
        BranchResolver branches,
        IHrDb db,
        IStaffInvite invite,
        HrChangeLogService hr,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrWhiteSpace(request.DisplayName)
            || request.Role is null
            || !Enum.IsDefined(request.Role.Value)
            || request.HireDate is null)
        {
            return Results.BadRequest(new { error = "Email, name, role, and hire date are required." });
        }

        if (request.DisplayName.Trim().Length > 200)
        {
            return Results.BadRequest(new { error = "Name must be 200 characters or fewer." });
        }

        if (!IsValidEmail(request.Email))
        {
            return Results.BadRequest(new { error = "Enter a valid email address." });
        }

        var lengthError = LengthError(
            request.Mobile,
            request.EmergencyContactName,
            request.EmergencyContactPhone,
            request.Position,
            request.Department,
            request.EmployeeNo,
            request.Tin,
            request.Sss,
            request.PhilHealth,
            request.PagIbig);
        if (lengthError is not null)
        {
            return lengthError;
        }

        if (!IsValidTin(request.Tin))
        {
            return Results.BadRequest(new { error = "TIN must be 9 to 14 digits, for example 123-456-789-000." });
        }

        var actor = http.GetStaff();
        var home = await ResolveCreateHomeAsync(request, http, branches, db, actor, cancellationToken);
        if (home.Error is not null)
        {
            return home.Error;
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

        var persisted = await StaffEndpoints.PersistStaffAsync(
            db,
            invite,
            request.Email,
            request.DisplayName,
            request.Role.Value,
            home.BranchId,
            request.PayBasis,
            request.RatePhp,
            otAfterHours,
            otMultiplier,
            actor: actor,
            appFields: request.AppFields,
            mustClock: request.MustClock,
            cancellationToken: cancellationToken,
            mobile: request.Mobile,
            hireDate: request.HireDate,
            emergencyContactName: request.EmergencyContactName,
            emergencyContactPhone: request.EmergencyContactPhone,
            hr: hr);
        if (persisted.Error is not null)
        {
            return persisted.Error;
        }

        var staff = persisted.Staff!;
        staff.Position = BlankToNull(request.Position);
        staff.Department = BlankToNull(request.Department);
        staff.EmployeeNo = BlankToNull(request.EmployeeNo);
        staff.Tin = BlankToNull(request.Tin);
        staff.Sss = BlankToNull(request.Sss);
        staff.PhilHealth = BlankToNull(request.PhilHealth);
        staff.PagIbig = BlankToNull(request.PagIbig);

        var actorId = actor?.Id;
        hr.Record(actorId, staff.Id, HrChangeField.StaffRole, null, staff.Role.ToString(), refType: "directory");
        hr.Record(
            actorId, staff.Id, HrChangeField.StaffHireDate, null,
            staff.HireDate?.ToString("yyyy-MM-dd"), refType: "directory");
        hr.Record(actorId, staff.Id, HrChangeField.StaffPosition, null, staff.Position, refType: "directory");
        hr.Record(actorId, staff.Id, HrChangeField.StaffDepartment, null, staff.Department, refType: "directory");
        hr.Record(actorId, staff.Id, HrChangeField.StaffEmployeeNo, null, staff.EmployeeNo, refType: "directory");
        hr.Record(actorId, staff.Id, HrChangeField.StaffTin, null, staff.Tin, refType: "directory");
        hr.Record(actorId, staff.Id, HrChangeField.StaffSss, null, staff.Sss, refType: "directory");
        hr.Record(actorId, staff.Id, HrChangeField.StaffPhilHealth, null, staff.PhilHealth, refType: "directory");
        hr.Record(actorId, staff.Id, HrChangeField.StaffPagIbig, null, staff.PagIbig, refType: "directory");

        await db.SaveChangesAsync(cancellationToken);
        if (staff.BranchId is Guid createdHomeId)
        {
            staff.Branch ??= await db.Branches.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == createdHomeId, cancellationToken);
        }

        var onLeave = await IsOnLeaveTodayAsync(db, staff.Id, cancellationToken);
        return Results.Created(
            $"/api/employee-directory/{staff.Id}",
            ToCard(staff, onLeave, revealPayAndAccess: actor?.Role == StaffRole.Owner));
    }

    private static async Task<IResult> Update(
        Guid id,
        UpdateEmployeeDirectoryRequest request,
        HttpContext http,
        BranchResolver branches,
        IHrDb db,
        HrChangeLogService hr,
        AttendanceService attendance,
        IStaffWorkHooks work,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null || !HrRoles.IsDesk(actor.Role))
        {
            return Forbidden("You cannot change the directory.");
        }

        if (actor.Role != StaffRole.Owner && actor.Role != StaffRole.Admin)
        {
            return Forbidden("You cannot change the directory.");
        }

        var branchId = await branches.ResolveOpsAsync(http, cancellationToken);
        var staff = await db.Staff.Include(s => s.Profile).Include(s => s.Branch)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (staff is null || staff.BranchId != branchId)
        {
            return Results.NotFound();
        }

        var branch = await db.Branches.AsNoTracking()
            .FirstAsync(b => b.Id == branchId, cancellationToken);
        if (!string.Equals(branch.Code, BranchCodes.Demo, StringComparison.OrdinalIgnoreCase)
            && string.Equals(staff.Branch?.Code, BranchCodes.Demo, StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }

        if (actor.Role != StaffRole.Owner && staff.Role == StaffRole.Owner)
        {
            return Forbidden("You cannot edit an owner's record.");
        }

        if (actor.Role == StaffRole.Admin && (request.Role is not null || request.Active is not null))
        {
            return Forbidden("You cannot change role or active status.");
        }

        // Deactivation: open jobs block it (409). Locks, check and side effects share one transaction.
        var deactivating = request.Active is false && staff.Active;
        await using var deactivationTx = deactivating
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        if (deactivating)
        {
            var blocking = await work.LockAndListOpenWorkAsync(staff.Id, cancellationToken);
            if (blocking.Count > 0)
            {
                return Results.Conflict(new { error = work.BlockedDeactivationMessage, jobs = blocking });
            }
        }

        var before = DirectoryAuditSnapshot.From(staff);

        if (request.DisplayName is not null)
        {
            var name = request.DisplayName.Trim();
            if (name.Length == 0 || name.Length > 200)
            {
                return Results.BadRequest(new { error = "Name must be 200 characters or fewer." });
            }

            staff.Profile.DisplayName = name;
        }

        var lengthError = LengthError(
            request.Mobile,
            request.EmergencyContactName,
            request.EmergencyContactPhone,
            request.Position,
            request.Department,
            request.EmployeeNo,
            request.Tin,
            request.Sss,
            request.PhilHealth,
            request.PagIbig);
        if (lengthError is not null)
        {
            return lengthError;
        }

        if (!IsValidTin(request.Tin))
        {
            return Results.BadRequest(new { error = "TIN must be 9 to 14 digits, for example 123-456-789-000." });
        }

        if (request.RegisteredAddress is not null
            && request.RegisteredAddress.Trim().Length > RegisteredAddressMaxLength)
        {
            return Results.BadRequest(new { error = "Registered address must be 300 characters or fewer." });
        }

        if (request.ZipCode is not null && request.ZipCode.Trim().Length > ZipCodeMaxLength)
        {
            return Results.BadRequest(new { error = "ZIP code must be 10 characters or fewer." });
        }

        if (request.Mobile is not null)
        {
            var mobile = request.Mobile.Trim();
            staff.Profile.Mobile = mobile.Length == 0 ? null : mobile;
        }

        if (request.HireDate is not null)
        {
            staff.HireDate = request.HireDate;
        }

        if (request.EmergencyContactName is not null)
        {
            var name = request.EmergencyContactName.Trim();
            staff.EmergencyContactName = name.Length == 0 ? null : name;
        }

        if (request.EmergencyContactPhone is not null)
        {
            var phone = request.EmergencyContactPhone.Trim();
            staff.EmergencyContactPhone = phone.Length == 0 ? null : phone;
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

        if (request.RegisteredAddress is not null)
        {
            staff.RegisteredAddress = BlankToNull(request.RegisteredAddress);
        }

        if (request.ZipCode is not null)
        {
            staff.ZipCode = BlankToNull(request.ZipCode);
        }

        if (request.ClearBirthDate)
        {
            staff.BirthDate = null;
        }
        else if (request.BirthDate is DateOnly birthDate)
        {
            staff.BirthDate = birthDate;
        }

        if (actor.Role == StaffRole.Owner)
        {
            if (request.Role is StaffRole role)
            {
                if (!Enum.IsDefined(role))
                {
                    return Results.BadRequest(new { error = "Unknown role." });
                }

                staff.Role = role;
                if (role == StaffRole.Owner)
                {
                    staff.MustClock = false;
                }
            }

            if (request.Active is bool active)
            {
                staff.Active = active;
            }

            if (request.ClearSeparation)
            {
                staff.SeparationDate = null;
                staff.SeparationReason = null;
            }
            else
            {
                if (request.SeparationDate is DateOnly separation)
                {
                    staff.SeparationDate = separation;
                }

                if (request.SeparationReason is not null)
                {
                    if (request.SeparationReason.Trim().Length > 200)
                    {
                        return Results.BadRequest(
                            new { error = "Separation reason must be 200 characters or fewer." });
                    }

                    staff.SeparationReason = BlankToNull(request.SeparationReason);
                }
            }

            if (request.ClearHomeBranch)
            {
                if (staff.Role == StaffRole.Owner)
                {
                    staff.BranchId = null;
                }
            }
            else if (request.BranchId is Guid targetBranchId)
            {
                staff.BranchId = targetBranchId;
            }

            StaffAccessFlags.ApplyOnUpdate(staff, request.AppFields);

            if (request.MustClock is bool mustClock)
            {
                staff.MustClock = staff.Role == StaffRole.Owner ? false : mustClock;
            }

            var hasPayEdits = request.PayBasis != null
                || request.RatePhp != null
                || request.OtAfterHoursPerDay != null
                || request.OtMultiplier != null
                || request.ClearPayBasis
                || request.ClearRate;

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

            if (hasPayEdits)
            {
                await StaffEndpoints.StaffAudit.SyncRateHistoryAsync(db, staff, actor.Id, hasPayEdits, cancellationToken);
            }
        }

        var restDays = RestDayList.ParseUpdate(request.RestDays, request.ClearRestDays);
        if (restDays.Error is not null)
        {
            return Results.BadRequest(new { error = restDays.Error });
        }

        if (restDays.Changed)
        {
            staff.RestDays = restDays.Stored;
        }

        DeactivationSummary? deactivation = null;
        if (deactivating)
        {
            deactivation = await StaffDeactivation.ApplyAsync(
                db,
                staff,
                actor.Id,
                hr,
                attendance,
                DateTime.UtcNow,
                cancellationToken);
        }

        RecordDirectoryChanges(hr, actor.Id, staff, before);

        await db.SaveChangesAsync(cancellationToken);
        if (deactivationTx is not null)
        {
            await deactivationTx.CommitAsync(cancellationToken);
        }

        var onLeave = await IsOnLeaveTodayAsync(db, staff.Id, cancellationToken);
        var card = ToCard(
            staff,
            onLeave,
            revealPayAndAccess: actor.Role == StaffRole.Owner);
        return Results.Ok(card with { Deactivation = deactivation });
    }

    private static async Task<(Guid? BranchId, IResult? Error)> ResolveCreateHomeAsync(
        CreateEmployeeDirectoryRequest request,
        HttpContext http,
        BranchResolver branches,
        IHrDb db,
        Domain.Staff? actor,
        CancellationToken cancellationToken)
    {
        if (actor.IsShopScopedOwner())
        {
            return (actor!.BranchId, null);
        }

        if (request.AllShops == true)
        {
            if (request.Role != StaffRole.Owner)
            {
                return (null, Results.BadRequest(new { error = "Home branch is required." }));
            }

            return (null, null);
        }

        if (request.BranchId is Guid requested)
        {
            var branch = await db.Branches.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == requested, cancellationToken);
            if (request.Role == StaffRole.Owner)
            {
                var error = StaffEndpoints.ShopOwnerBranchError(branch);
                return error is null ? (requested, null) : (null, error);
            }

            if (branch is null
                || branch.OrganizationId != PlatformIds.OrganizationId
                || branch.Code == BranchCodes.Demo)
            {
                return (null, Results.BadRequest(new { error = "Home branch must be one of your shops." }));
            }

            return (requested, null);
        }

        var current = await branches.ResolveOpsAsync(http, cancellationToken);
        return (current, null);
    }

    private static async Task<HashSet<Guid>> OnLeaveTodayIdsAsync(
        IHrDb db,
        Guid[] staffIds,
        CancellationToken cancellationToken)
    {
        if (staffIds.Length == 0)
        {
            return [];
        }

        var today = ManilaTime.Today();
        var ids = await db.LeaveRequests.AsNoTracking()
            .Where(r => staffIds.Contains(r.StaffId)
                && r.Status == LeaveRequestStatus.Approved
                && r.StartDate <= today
                && r.EndDate >= today)
            .Select(r => r.StaffId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    private static async Task<bool> IsOnLeaveTodayAsync(
        IHrDb db,
        Guid staffId,
        CancellationToken cancellationToken)
    {
        var today = ManilaTime.Today();
        return await db.LeaveRequests.AsNoTracking().AnyAsync(
            r => r.StaffId == staffId
                && r.Status == LeaveRequestStatus.Approved
                && r.StartDate <= today
                && r.EndDate >= today,
            cancellationToken);
    }

    internal static IReadOnlyList<DirectoryConfigIssue> BuildConfigHealth(
        IEnumerable<Domain.Staff> staff,
        decimal? minimumDailyWage,
        IReadOnlySet<Guid> silAllotted,
        DateOnly today)
    {
        var issues = new List<DirectoryConfigIssue>();
        foreach (var person in staff)
        {
            if (!person.Active || person.Role == StaffRole.Owner)
            {
                continue;
            }

            var codes = new List<string>();
            if (person.HireDate is null)
            {
                codes.Add(HealthMissingHireDate);
            }

            var rateBlank = person.RatePhp is null;
            var rateExpected = person.MustClock
                || person.PayBasis is not null
                || HrRoles.ClocksByDefault(person.Role);
            if (rateBlank && rateExpected)
            {
                codes.Add(HealthMissingPayRate);
            }

            if (person.MustClock && rateBlank)
            {
                codes.Add(HealthMustClockNoRate);
            }

            // Below the shop's verified daily minimum. Hourly compares rate x standard 8 hours.
            if (minimumDailyWage is decimal minimum
                && person.RatePhp is decimal rate
                && person.PayBasis is { } basis)
            {
                var dailyEquivalent = basis == PayBasis.Hourly
                    ? rate * (person.OtAfterHoursPerDay ?? 8m)
                    : rate;
                if (dailyEquivalent < minimum)
                {
                    codes.Add(HealthBelowMinimumWage);
                }
            }

            // Paid workers should have their government numbers on file.
            if (rateExpected
                && (string.IsNullOrWhiteSpace(person.Sss)
                    || string.IsNullOrWhiteSpace(person.PhilHealth)
                    || string.IsNullOrWhiteSpace(person.PagIbig)))
            {
                codes.Add(HealthMissingStatutoryIds);
            }

            // Service incentive leave: five statutory days after one year of service.
            if (person.HireDate is DateOnly hired
                && hired.AddYears(1) <= today
                && !silAllotted.Contains(person.Id))
            {
                codes.Add(HealthSilMissingAllotment);
            }

            if (codes.Count > 0)
            {
                issues.Add(new DirectoryConfigIssue(person.Id, person.Profile.DisplayName, codes));
            }
        }

        return issues;
    }

    internal static bool IsValidEmail(string? email)
    {
        var trimmed = email?.Trim() ?? "";
        if (trimmed.Length == 0 || trimmed.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var at = trimmed.IndexOf('@');
        return at > 0
            && at == trimmed.LastIndexOf('@')
            && trimmed.IndexOf('.', at) > at + 1
            && !trimmed.EndsWith('.');
    }

    /// <summary>Blank is fine (TIN is optional). Otherwise digits with optional dashes or spaces.</summary>
    internal static bool IsValidTin(string? tin)
    {
        if (string.IsNullOrWhiteSpace(tin))
        {
            return true;
        }

        var trimmed = tin.Trim();
        if (trimmed.Any(c => !char.IsAsciiDigit(c) && c != '-' && c != ' '))
        {
            return false;
        }

        var digits = trimmed.Count(char.IsAsciiDigit);
        return digits is >= 9 and <= 14;
    }

    private static IResult? LengthError(
        string? mobile,
        string? emergencyName,
        string? emergencyPhone,
        string? position,
        string? department,
        string? employeeNo,
        string? tin,
        string? sss,
        string? philHealth,
        string? pagIbig)
    {
        if (mobile is not null && mobile.Trim().Length > MobileMaxLength)
        {
            return Results.BadRequest(new { error = "Mobile must be 40 characters or fewer." });
        }

        if (emergencyName is not null && emergencyName.Trim().Length > EmergencyNameMaxLength)
        {
            return Results.BadRequest(new { error = "Emergency contact name must be 200 characters or fewer." });
        }

        if (emergencyPhone is not null && emergencyPhone.Trim().Length > EmergencyPhoneMaxLength)
        {
            return Results.BadRequest(new { error = "Emergency contact phone must be 40 characters or fewer." });
        }

        if (position is not null && position.Trim().Length > PositionMaxLength)
        {
            return Results.BadRequest(new { error = "Position must be 100 characters or fewer." });
        }

        if (department is not null && department.Trim().Length > DepartmentMaxLength)
        {
            return Results.BadRequest(new { error = "Department must be 100 characters or fewer." });
        }

        if (employeeNo is not null && employeeNo.Trim().Length > EmployeeNoMaxLength)
        {
            return Results.BadRequest(new { error = "Employee number must be 100 characters or fewer." });
        }

        var statutory = StatutoryLength(tin, "TIN")
            ?? StatutoryLength(sss, "SSS")
            ?? StatutoryLength(philHealth, "PhilHealth")
            ?? StatutoryLength(pagIbig, "Pag-IBIG");
        return statutory;
    }

    private static IResult? StatutoryLength(string? value, string label)
    {
        if (value is not null && value.Trim().Length > StatutoryIdMaxLength)
        {
            return Results.BadRequest(new { error = $"{label} must be {StatutoryIdMaxLength} characters or fewer." });
        }

        return null;
    }

    private static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IResult Forbidden(string error) =>
        Results.Json(new { error }, statusCode: StatusCodes.Status403Forbidden);

    internal static EmployeeDirectoryCard ToCard(
        Domain.Staff staff,
        bool onLeaveToday = false,
        bool revealStatutoryIds = true,
        bool revealPayAndAccess = false) => new(
        staff.Id,
        staff.Role,
        staff.Profile.DisplayName,
        staff.Profile.Email,
        staff.Profile.Mobile,
        staff.HireDate,
        staff.Active,
        staff.EmergencyContactName,
        staff.EmergencyContactPhone,
        staff.Position,
        staff.Department,
        staff.EmployeeNo,
        staff.Active
            ? onLeaveToday ? DirectoryPresence.OnLeaveToday : DirectoryPresence.Active
            : DirectoryPresence.Inactive,
        revealStatutoryIds ? staff.Tin : null,
        revealStatutoryIds ? staff.Sss : null,
        revealStatutoryIds ? staff.PhilHealth : null,
        revealStatutoryIds ? staff.PagIbig : null,
        revealStatutoryIds ? staff.RegisteredAddress : null,
        revealStatutoryIds ? staff.ZipCode : null,
        revealStatutoryIds ? staff.BirthDate : null,
        staff.SeparationDate,
        staff.SeparationReason,
        RestDayList.ToArray(staff.RestDays),
        revealPayAndAccess ? staff.BranchId : null,
        revealPayAndAccess ? staff.Branch?.Name : null,
        revealPayAndAccess ? staff.MustClock : null,
        revealPayAndAccess ? staff.PayBasis : null,
        revealPayAndAccess ? staff.RatePhp : null,
        revealPayAndAccess ? staff.OtAfterHoursPerDay : null,
        revealPayAndAccess ? staff.OtMultiplier : null)
        {
            AppFields = revealPayAndAccess ? StaffAccessFlags.Describe(staff) : null
        };

    /// <summary>Audited directory fields, captured before an edit so only real changes are logged.</summary>
    private readonly record struct DirectoryAuditSnapshot(
        string Role,
        string Active,
        string? HireDate,
        string? Position,
        string? Department,
        string? EmployeeNo,
        string? EmergencyContactName,
        string? Tin,
        string? Sss,
        string? PhilHealth,
        string? PagIbig,
        string? RegisteredAddress,
        string? ZipCode,
        string? BirthDate,
        string MustClock,
        string? SeparationDate,
        string? SeparationReason,
        string? RestDays)
    {
        public static DirectoryAuditSnapshot From(Domain.Staff staff) => new(
            staff.Role.ToString(),
            staff.Active ? "true" : "false",
            staff.HireDate?.ToString("yyyy-MM-dd"),
            staff.Position,
            staff.Department,
            staff.EmployeeNo,
            staff.EmergencyContactName,
            staff.Tin,
            staff.Sss,
            staff.PhilHealth,
            staff.PagIbig,
            staff.RegisteredAddress,
            staff.ZipCode,
            staff.BirthDate?.ToString("yyyy-MM-dd"),
            staff.MustClock ? "true" : "false",
            staff.SeparationDate?.ToString("yyyy-MM-dd"),
            staff.SeparationReason,
            staff.RestDays);
    }

    private static void RecordDirectoryChanges(
        HrChangeLogService hr,
        Guid actorId,
        Domain.Staff staff,
        DirectoryAuditSnapshot before)
    {
        hr.Record(actorId, staff.Id, HrChangeField.StaffRole, before.Role, staff.Role.ToString());
        hr.Record(
            actorId, staff.Id, HrChangeField.StaffActive, before.Active,
            staff.Active ? "true" : "false");
        hr.Record(
            actorId, staff.Id, HrChangeField.StaffHireDate, before.HireDate,
            staff.HireDate?.ToString("yyyy-MM-dd"));
        hr.Record(actorId, staff.Id, HrChangeField.StaffPosition, before.Position, staff.Position);
        hr.Record(actorId, staff.Id, HrChangeField.StaffDepartment, before.Department, staff.Department);
        hr.Record(actorId, staff.Id, HrChangeField.StaffEmployeeNo, before.EmployeeNo, staff.EmployeeNo);
        hr.Record(
            actorId, staff.Id, HrChangeField.StaffEmergencyContact,
            before.EmergencyContactName, staff.EmergencyContactName);
        hr.Record(actorId, staff.Id, HrChangeField.StaffTin, before.Tin, staff.Tin);
        hr.Record(actorId, staff.Id, HrChangeField.StaffSss, before.Sss, staff.Sss);
        hr.Record(actorId, staff.Id, HrChangeField.StaffPhilHealth, before.PhilHealth, staff.PhilHealth);
        hr.Record(actorId, staff.Id, HrChangeField.StaffPagIbig, before.PagIbig, staff.PagIbig);
        hr.Record(
            actorId, staff.Id, HrChangeField.StaffRegisteredAddress,
            before.RegisteredAddress, staff.RegisteredAddress);
        hr.Record(actorId, staff.Id, HrChangeField.StaffZipCode, before.ZipCode, staff.ZipCode);
        hr.Record(
            actorId, staff.Id, HrChangeField.StaffBirthDate, before.BirthDate,
            staff.BirthDate?.ToString("yyyy-MM-dd"));
        hr.Record(
            actorId, staff.Id, HrChangeField.StaffMustClock, before.MustClock,
            staff.MustClock ? "true" : "false");
        hr.Record(
            actorId, staff.Id, HrChangeField.StaffSeparationDate, before.SeparationDate,
            staff.SeparationDate?.ToString("yyyy-MM-dd"));
        hr.Record(
            actorId, staff.Id, HrChangeField.StaffSeparationReason, before.SeparationReason,
            staff.SeparationReason);
        hr.Record(actorId, staff.Id, HrChangeField.StaffRestDays, before.RestDays, staff.RestDays);
    }
}

public enum DirectoryPresence
{
    Active,
    OnLeaveToday,
    Inactive
}

public sealed record DirectoryConfigIssue(
    Guid StaffId,
    string DisplayName,
    IReadOnlyList<string> Issues);

public sealed record EmployeeDirectoryListResponse(
    int Headcount,
    IReadOnlyList<EmployeeDirectoryCard> Items,
    IReadOnlyList<DirectoryConfigIssue>? ConfigHealth = null);

public sealed record EmployeeDirectoryCard(
    Guid Id,
    StaffRole Role,
    string DisplayName,
    string? Email,
    string? Mobile,
    DateOnly? HireDate,
    bool Active,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    string? Position = null,
    string? Department = null,
    string? EmployeeNo = null,
    DirectoryPresence Status = DirectoryPresence.Active,
    string? Tin = null,
    string? Sss = null,
    string? PhilHealth = null,
    string? PagIbig = null,
    string? RegisteredAddress = null,
    string? ZipCode = null,
    DateOnly? BirthDate = null,
    DateOnly? SeparationDate = null,
    string? SeparationReason = null,
    int[]? RestDays = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? BranchId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BranchName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? MustClock = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    PayBasis? PayBasis = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? RatePhp = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? OtAfterHoursPerDay = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? OtMultiplier = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DeactivationSummary? Deactivation = null)
{
    /// <summary>
    /// The app's own access flags (see <see cref="StaffAccessFlags"/>), written as top-level fields.
    /// Null (nothing written) unless pay and access are revealed.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, object?>? AppFields { get; init; }
}

public sealed class CreateEmployeeDirectoryRequest
{
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public StaffRole? Role { get; set; }
    public DateOnly? HireDate { get; set; }
    public string? Mobile { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? Position { get; set; }
    public string? Department { get; set; }
    public string? EmployeeNo { get; set; }
    public string? Tin { get; set; }
    public string? Sss { get; set; }
    public string? PhilHealth { get; set; }
    public string? PagIbig { get; set; }
    public Guid? BranchId { get; set; }
    public bool? AllShops { get; set; }
    public bool? MustClock { get; set; }
    public PayBasis? PayBasis { get; set; }
    public decimal? RatePhp { get; set; }
    public decimal? OtAfterHoursPerDay { get; set; }
    public decimal? OtMultiplier { get; set; }

    /// <summary>The app's own access flags (see <see cref="StaffAccessFlags"/>), sent as top-level fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AppFields { get; set; }
}

public sealed class UpdateEmployeeDirectoryRequest
{
    public string? DisplayName { get; set; }
    public string? Mobile { get; set; }
    public DateOnly? HireDate { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? Position { get; set; }
    public string? Department { get; set; }
    public string? EmployeeNo { get; set; }
    public string? Tin { get; set; }
    public string? Sss { get; set; }
    public string? PhilHealth { get; set; }
    public string? PagIbig { get; set; }
    public string? RegisteredAddress { get; set; }
    public string? ZipCode { get; set; }
    public DateOnly? BirthDate { get; set; }
    public bool ClearBirthDate { get; set; }
    public StaffRole? Role { get; set; }
    public bool? Active { get; set; }

    /// <summary>Owner-only. When the worker left. Null in the body means "not changed".</summary>
    public DateOnly? SeparationDate { get; set; }
    public string? SeparationReason { get; set; }
    public bool ClearSeparation { get; set; }
    public int[]? RestDays { get; set; }
    public bool ClearRestDays { get; set; }

    // Owner-only Access and Pay fields
    public Guid? BranchId { get; set; }
    public bool ClearHomeBranch { get; set; }
    public bool? MustClock { get; set; }
    public PayBasis? PayBasis { get; set; }
    public decimal? RatePhp { get; set; }
    public decimal? OtAfterHoursPerDay { get; set; }
    public decimal? OtMultiplier { get; set; }
    public bool ClearPayBasis { get; set; }
    public bool ClearRate { get; set; }

    /// <summary>The app's own access flags (see <see cref="StaffAccessFlags"/>), sent as top-level fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AppFields { get; set; }
}
