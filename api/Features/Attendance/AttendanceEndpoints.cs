using System.Globalization;
using System.Text.Json;
using HrCore.Features.HrChangeLog;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Attendance;

public static class AttendanceEndpoints
{
    public static IEndpointRouteBuilder MapAttendanceApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/ops/attendance")
            .WithTags("Attendance")
            .AddEndpointFilter<StaffAuthorizationFilter>();
        api.MapPost("/clock-in", ClockIn).DisableAntiforgery().WithName("ClockIn");
        api.MapPost("/clock-out", ClockOut).DisableAntiforgery().WithName("ClockOut");
        api.MapGet("/me", Me).WithName("AttendanceMe");
        api.MapGet("/kiosk", KioskList).WithName("AttendanceKiosk");
        api.MapPost("/kiosk/{staffId:guid}/clock-in", KioskClockIn).DisableAntiforgery().WithName("KioskClockIn");
        api.MapPost("/kiosk/{staffId:guid}/clock-out", KioskClockOut).DisableAntiforgery().WithName("KioskClockOut");
        api.MapPost("/punch-for", PunchFor).DisableAntiforgery().WithName("AttendancePunchFor");
        api.MapPost("/punches/{id:guid}/close", ClosePunch).DisableAntiforgery().WithName("AttendanceClosePunch");
        api.MapPost("/punches/{id:guid}/approve", ApprovePunch).DisableAntiforgery().WithName("AttendanceApprovePunch");
        api.MapPost("/punches/{id:guid}/reject", RejectPunch).DisableAntiforgery().WithName("AttendanceRejectPunch");
        api.MapGet("/punches/{id:guid}/photos/{side}", DownloadPhoto)
            .WithName("DownloadAttendancePhoto")
            .WithSummary("Open a clock photo")
            .WithDescription(
                "Side is in or out. Owner and admin can open a photo for a shift on a shop they can see. " +
                "Staff can open their own photo. Another staff member cannot.");
        api.MapGet("/overtime", ListOvertime)
            .WithName("ListOvertime")
            .WithSummary("List overtime")
            .WithDescription(
                "Punch time past shift end, after the grace, is claimed and is not paid until approved. " +
                "Staff can also file a request ahead of time. Technician and sales see only their own; " +
                "owner and admin see shop scope; mine=true returns the caller's own claims (empty for the owner, who has no OT).");
        api.MapPost("/overtime", FileOvertime)
            .WithName("FileOvertime")
            .WithSummary("File an overtime request");
        api.MapPost("/overtime/{id:guid}/approve", ApproveOvertime)
            .WithName("ApproveOvertime")
            .WithSummary("Approve overtime");
        api.MapPost("/overtime/{id:guid}/reject", RejectOvertime)
            .WithName("RejectOvertime")
            .WithSummary("Reject overtime");
        api.MapPost("/overtime/backfill", BackfillOvertime)
            .AddEndpointFilter(StaffRoleFilter.OwnerOnly)
            .WithName("BackfillOvertime")
            .WithSummary("Backfill punch overtime claims");
        api.MapGet("/register", Register)
            .WithName("AttendanceRegister")
            .WithSummary("Attendance register")
            .WithDescription(
                "Query: from, to (YYYY-MM-DD, Asia/Manila; default this week Sunday–Saturday). " +
                "Optional staffId, branchId. " +
                "Owner/admin: shop-scoped via the ops branch header; HQ owner may pass " +
                "scope=shop|owned|franchise|all. " +
                "Technician/sales: own punches only (staffId/branch filters ignored). " +
                "Open punches keep Out and Hours null.");
        api.MapGet("/open-punches", OpenPunches)
            .AddEndpointFilter(StaffRoleFilter.OwnerOrAdmin)
            .WithName("AttendanceOpenPunches")
            .WithSummary("Open punches in a range")
            .WithDescription(
                "Query: from, to (YYYY-MM-DD, Asia/Manila; default this week). " +
                "Lists punches with no clock-out whose clock-in falls in the range, on the shop scope. " +
                "Owner and admin only.");
        api.MapGet("/register.csv", RegisterCsv)
            .WithName("AttendanceRegisterCsv")
            .WithSummary("Attendance register CSV")
            .WithDescription(
                "Same filters as /register. Columns: Name, Branch, Kind, In, Out, Hours. " +
                "Open punches leave Out and Hours empty.");
        return app;
    }

    private static async Task<IResult> ClockIn(
        HttpContext http,
        BranchResolver branches,
        AttendanceService attendance,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        // #384: the owner has no personal time clock, same as leave. Kiosk punch-for is unchanged.
        if (actor.Role == StaffRole.Owner)
        {
            return Results.Json(new { error = "Owners do not clock in." }, statusCode: StatusCodes.Status403Forbidden);
        }

        var branchId = await branches.ResolveOpsAsync(http, cancellationToken);
        var body = await ReadPunchAsync(http.Request, cancellationToken);
        if (body.Error is not null)
        {
            return Results.BadRequest(new { error = body.Error });
        }

        try
        {
            var punch = await attendance.ClockInAsync(
                actor,
                branchId,
                new ClockInRequest
                {
                    Latitude = body.Latitude,
                    Longitude = body.Longitude,
                    OffsiteReason = body.OffsiteReason
                },
                body.Photo,
                cancellationToken);
            return Results.Ok(punch);
        }
        catch (Exception ex) when (ex is ClockPhotoException or AttendanceBadRequestException or InvalidOperationException)
        {
            return MapError(ex);
        }
    }

    private static async Task<IResult> ClockOut(
        HttpContext http,
        AttendanceService attendance,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        // #384: the owner has no personal time clock, same as leave. Kiosk punch-for is unchanged.
        if (actor.Role == StaffRole.Owner)
        {
            return Results.Json(new { error = "Owners do not clock in." }, statusCode: StatusCodes.Status403Forbidden);
        }

        var body = await ReadPunchAsync(http.Request, cancellationToken);
        if (body.Error is not null)
        {
            return Results.BadRequest(new { error = body.Error });
        }

        try
        {
            var punch = await attendance.ClockOutAsync(
                actor,
                new ClockInRequest
                {
                    Latitude = body.Latitude,
                    Longitude = body.Longitude,
                    OffsiteReason = body.OffsiteReason
                },
                body.Photo,
                cancellationToken);
            return Results.Ok(punch);
        }
        catch (Exception ex) when (ex is ClockPhotoException or AttendanceBadRequestException or InvalidOperationException)
        {
            return MapError(ex);
        }
    }

    private static async Task<IResult> KioskList(
        HttpContext http,
        BranchResolver branches,
        AttendanceService attendance,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var branchId = await branches.ResolveOpsAsync(http, cancellationToken);
        try
        {
            return Results.Ok(await attendance.ListKioskAsync(actor, branchId, cancellationToken));
        }
        catch (Exception ex) when (ex is AttendanceForbiddenException or AttendanceBadRequestException or KeyNotFoundException or InvalidOperationException)
        {
            return MapError(ex);
        }
    }

    private static async Task<IResult> KioskClockIn(
        Guid staffId,
        HttpContext http,
        BranchResolver branches,
        AttendanceService attendance,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var branchId = await branches.ResolveOpsAsync(http, cancellationToken);
        var body = await ReadPunchAsync(http.Request, cancellationToken);
        if (body.Error is not null)
        {
            return Results.BadRequest(new { error = body.Error });
        }

        try
        {
            var punch = await attendance.KioskClockInAsync(actor, branchId, staffId, body.Photo, cancellationToken);
            return Results.Ok(punch);
        }
        catch (Exception ex) when (ex is AttendanceForbiddenException or AttendanceBadRequestException or ClockPhotoException or KeyNotFoundException or InvalidOperationException)
        {
            return MapError(ex);
        }
    }

    private static async Task<IResult> KioskClockOut(
        Guid staffId,
        HttpContext http,
        BranchResolver branches,
        AttendanceService attendance,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var branchId = await branches.ResolveOpsAsync(http, cancellationToken);
        var body = await ReadPunchAsync(http.Request, cancellationToken);
        if (body.Error is not null)
        {
            return Results.BadRequest(new { error = body.Error });
        }

        try
        {
            var punch = await attendance.KioskClockOutAsync(actor, branchId, staffId, body.Photo, cancellationToken);
            return Results.Ok(punch);
        }
        catch (Exception ex) when (ex is AttendanceForbiddenException or AttendanceBadRequestException or ClockPhotoException or KeyNotFoundException or InvalidOperationException)
        {
            return MapError(ex);
        }
    }

    private static async Task<IResult> PunchFor(
        HttpContext http,
        BranchResolver branches,
        AttendanceService attendance,
        IHrDb db,
        HrChangeLogService hr,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        PunchForRequest? request;
        try
        {
            request = await http.Request.ReadFromJsonAsync<PunchForRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "Could not read the punch." });
        }

        if (request is null || request.StaffId == Guid.Empty)
        {
            return Results.BadRequest(new { error = "Choose a staff member." });
        }

        var branchId = await branches.ResolveOpsAsync(http, cancellationToken);
        try
        {
            var punch = await attendance.PunchForAsync(actor, branchId, request, cancellationToken);
            hr.Record(
                actor.Id,
                request.StaffId,
                HrChangeField.AttendancePunchFor,
                null,
                "recorded",
                refType: "attendance_punch");
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(punch);
        }
        catch (Exception ex) when (ex is AttendanceForbiddenException or AttendanceBadRequestException or KeyNotFoundException or InvalidOperationException)
        {
            return MapError(ex);
        }
    }

    private static async Task<IResult> ClosePunch(
        Guid id,
        HttpContext http,
        BranchResolver branches,
        AttendanceService attendance,
        IHrDb db,
        HrChangeLogService hr,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        ClosePunchRequest? request;
        try
        {
            request = await http.Request.ReadFromJsonAsync<ClosePunchRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "Could not read the close request." });
        }

        request ??= new ClosePunchRequest();
        var branchId = await branches.ResolveOpsAsync(http, cancellationToken);
        try
        {
            var punch = await attendance.CloseAsync(actor, branchId, id, request, cancellationToken);
            hr.Record(
                actor.Id,
                punch.StaffId,
                HrChangeField.AttendancePunchClose,
                null,
                "closed",
                refType: "attendance_punch",
                refId: id);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(punch);
        }
        catch (Exception ex) when (ex is AttendanceForbiddenException or AttendanceBadRequestException or KeyNotFoundException or InvalidOperationException)
        {
            return MapError(ex);
        }
    }

    private static Task<IResult> ApprovePunch(
        Guid id,
        HttpContext http,
        BranchResolver branches,
        AttendanceService attendance,
        CancellationToken cancellationToken) =>
        ReviewPunchAsync(id, Domain.PunchApprovalStatus.Approved, http, branches, attendance, cancellationToken);

    private static Task<IResult> RejectPunch(
        Guid id,
        HttpContext http,
        BranchResolver branches,
        AttendanceService attendance,
        CancellationToken cancellationToken) =>
        ReviewPunchAsync(id, Domain.PunchApprovalStatus.Rejected, http, branches, attendance, cancellationToken);

    private static async Task<IResult> ReviewPunchAsync(
        Guid id,
        Domain.PunchApprovalStatus decision,
        HttpContext http,
        BranchResolver branches,
        AttendanceService attendance,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        ReviewPunchRequest? request;
        try
        {
            request = await http.Request.ReadFromJsonAsync<ReviewPunchRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "Could not read the review." });
        }

        var branchId = await branches.ResolveOpsAsync(http, cancellationToken);
        try
        {
            var punch = await attendance.ReviewAsync(
                actor, branchId, id, decision, request?.Note, cancellationToken);
            return Results.Ok(punch);
        }
        catch (Exception ex) when (ex is AttendanceForbiddenException or AttendanceBadRequestException or KeyNotFoundException or InvalidOperationException)
        {
            return MapError(ex);
        }
    }

    private static IResult MapError(Exception ex) => ex switch
    {
        AttendanceForbiddenException => Results.Json(
            new { error = ex.Message },
            statusCode: StatusCodes.Status403Forbidden),
        AttendanceBadRequestException => Results.BadRequest(new { error = ex.Message }),
        ClockPhotoException => Results.BadRequest(new { error = ex.Message }),
        KeyNotFoundException => Results.NotFound(new { error = ex.Message }),
        InvalidOperationException => Results.Conflict(new { error = ex.Message }),
        _ => throw ex
    };

    private static async Task<IResult> Me(
        HttpContext http,
        BranchResolver branches,
        AttendanceService attendance,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var branchId = await branches.ResolveOpsAsync(http, cancellationToken);
        var me = await attendance.GetMeAsync(actor, branchId, cancellationToken);
        return Results.Ok(me);
    }

    private static async Task<IResult> DownloadPhoto(
        Guid id,
        string side,
        HttpContext http,
        BranchResolver branches,
        IHrDb db,
        IFileStore files,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var normalized = side.Trim().ToLowerInvariant();
        if (normalized is not "in" and not "out")
        {
            return Results.BadRequest(new { error = "Photo side must be in or out." });
        }

        var punch = await db.AttendancePunches.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (punch is null)
        {
            return Results.NotFound();
        }

        if (actor.Role is Domain.StaffRole.Owner or Domain.StaffRole.Admin)
        {
            return await OpsReportScope.MapAsync(
                http,
                branches,
                (shops, _, ct) =>
                {
                    if (!shops.Any(s => s.Id == punch.BranchId))
                    {
                        return Task.FromResult(Results.NotFound());
                    }

                    return OpenPhotoAsync(punch, normalized, files, ct);
                },
                cancellationToken);
        }

        if (punch.StaffId != actor.Id)
        {
            return Results.Json(
                new { error = "You cannot open another staff member's photo." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await OpenPhotoAsync(punch, normalized, files, cancellationToken);
    }

    private static async Task<IResult> OpenPhotoAsync(
        Domain.AttendancePunch punch,
        string side,
        IFileStore files,
        CancellationToken cancellationToken)
    {
        var storagePath = side == "out" ? punch.OutPhotoStoragePath : punch.InPhotoStoragePath;
        var contentType = side == "out" ? punch.OutPhotoContentType : punch.InPhotoContentType;
        var fileName = side == "out" ? punch.OutPhotoFileName : punch.InPhotoFileName;
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            return Results.NotFound(new { error = "No clock photo on this punch." });
        }

        var stream = await files.OpenAsync(storagePath, cancellationToken);
        return stream is null
            ? Results.NotFound(new { error = "No clock photo on this punch." })
            : Results.File(
                stream,
                string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType,
                string.IsNullOrWhiteSpace(fileName) ? "clock-photo.jpg" : fileName);
    }

    private static async Task<PunchBody> ReadPunchAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(cancellationToken);
            var photo = form.Files.GetFile("photo");
            if (photo is { Length: 0 })
            {
                photo = null;
            }

            var latitude = ParseCoordinate(form["latitude"]);
            if (latitude.Error is not null)
            {
                return latitude.ErrorBody;
            }

            var longitude = ParseCoordinate(form["longitude"]);
            if (longitude.Error is not null)
            {
                return longitude.ErrorBody;
            }

            return new PunchBody(latitude.Value, longitude.Value, photo, null, FormReason(form["offsiteReason"]));
        }

        if (request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                var gps = await request.ReadFromJsonAsync<ClockInRequest>(cancellationToken);
                return new PunchBody(gps?.Latitude, gps?.Longitude, null, null, gps?.OffsiteReason);
            }
            catch (JsonException)
            {
                return new PunchBody(null, null, null, "Could not read the clock request.");
            }
        }

        return new PunchBody(null, null, null, null);
    }

    private static (double? Value, string? Error, PunchBody ErrorBody) ParseCoordinate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (null, null, new PunchBody(null, null, null, null));
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return (null, "That location is not valid.", new PunchBody(null, null, null, "That location is not valid."));
        }

        return (value, null, new PunchBody(null, null, null, null));
    }

    private static string? FormReason(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

    private sealed record PunchBody(
        double? Latitude,
        double? Longitude,
        IFormFile? Photo,
        string? Error,
        string? OffsiteReason = null);

    private static async Task<IResult> ListOvertime(
        DateOnly? from,
        DateOnly? to,
        bool? mine,
        HttpContext http,
        BranchResolver branches,
        OvertimeService overtime,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        IReadOnlyList<Domain.Branch>? shops = null;
        if (actor.Role is Domain.StaffRole.Owner or Domain.StaffRole.Admin)
        {
            shops = await branches.ResolveOpsReportScopeAsync(http, cancellationToken);
        }

        var list = await overtime.ListAsync(actor, shops, from, to, mine ?? false, cancellationToken);
        return Results.Ok(list);
    }

    private static async Task<IResult> FileOvertime(
        HttpContext http,
        OvertimeService overtime,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        FileOvertimeRequest? request;
        try
        {
            request = await http.Request.ReadFromJsonAsync<FileOvertimeRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "Could not read the overtime request." });
        }

        if (request is null)
        {
            return Results.BadRequest(new { error = "Could not read the overtime request." });
        }

        try
        {
            return Results.Ok(await overtime.FileAsync(actor, request, cancellationToken));
        }
        catch (Exception ex) when (ex is AttendanceBadRequestException or InvalidOperationException)
        {
            return MapError(ex);
        }
    }

    private static Task<IResult> ApproveOvertime(
        Guid id,
        HttpContext http,
        BranchResolver branches,
        OvertimeService overtime,
        CancellationToken cancellationToken) =>
        DecideOvertime(id, Domain.OvertimeStatus.Approved, http, branches, overtime, cancellationToken);

    private static Task<IResult> RejectOvertime(
        Guid id,
        HttpContext http,
        BranchResolver branches,
        OvertimeService overtime,
        CancellationToken cancellationToken) =>
        DecideOvertime(id, Domain.OvertimeStatus.Rejected, http, branches, overtime, cancellationToken);

    private static async Task<IResult> DecideOvertime(
        Guid id,
        Domain.OvertimeStatus decision,
        HttpContext http,
        BranchResolver branches,
        OvertimeService overtime,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        DecideOvertimeRequest? request = null;
        if (http.Request.ContentLength is > 0)
        {
            try
            {
                request = await http.Request.ReadFromJsonAsync<DecideOvertimeRequest>(cancellationToken);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "Could not read the decision." });
            }
        }

        IReadOnlyList<Domain.Branch>? shops = null;
        if (actor.Role is Domain.StaffRole.Owner or Domain.StaffRole.Admin)
        {
            shops = await branches.ResolveOpsReportScopeAsync(http, cancellationToken);
        }

        try
        {
            return Results.Ok(await overtime.DecideAsync(actor, shops, id, decision, request, cancellationToken));
        }
        catch (Exception ex) when (ex is AttendanceForbiddenException or AttendanceBadRequestException or KeyNotFoundException or InvalidOperationException)
        {
            return MapError(ex);
        }
    }

    private static async Task<IResult> BackfillOvertime(
        HttpContext http,
        OvertimeService overtime,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await overtime.BackfillPunchClaimsAsync(actor, cancellationToken);
            return Results.Ok(result);
        }
        catch (Exception ex) when (ex is AttendanceForbiddenException or InvalidOperationException)
        {
            return MapError(ex);
        }
    }

    private static Task<IResult> Register(
        DateOnly? from,
        DateOnly? to,
        Guid? staffId,
        Guid? branchId,
        string? approval,
        HttpContext http,
        BranchResolver branches,
        AttendanceService attendance,
        CancellationToken cancellationToken) =>
        MapRegisterAsync(
            http,
            branches,
            async (actor, shops, ct) =>
            {
                try
                {
                    return Results.Ok(await attendance.GetRegisterAsync(
                        actor, (IReadOnlyList<Branch>?)shops, from, to, staffId, branchId, approval, ct));
                }
                catch (AttendanceBadRequestException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            },
            cancellationToken);

    private static Task<IResult> OpenPunches(
        DateOnly? from,
        DateOnly? to,
        HttpContext http,
        BranchResolver branches,
        AttendanceService attendance,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) => Results.Ok(await attendance.ListOpenPunchesAsync(shops, from, to, ct)),
            cancellationToken);

    private static Task<IResult> RegisterCsv(
        DateOnly? from,
        DateOnly? to,
        Guid? staffId,
        Guid? branchId,
        string? approval,
        HttpContext http,
        BranchResolver branches,
        AttendanceService attendance,
        CancellationToken cancellationToken) =>
        MapRegisterAsync(
            http,
            branches,
            async (actor, shops, ct) =>
            {
                try
                {
                    var file = await attendance.BuildRegisterCsvAsync(
                        actor, (IReadOnlyList<Branch>?)shops, from, to, staffId, branchId, approval, ct);
                    return Results.File(file.Bytes, "text/csv; charset=utf-8", file.FileName);
                }
                catch (AttendanceBadRequestException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            },
            cancellationToken);

    private static Task<IResult> MapRegisterAsync(
        HttpContext http,
        BranchResolver branches,
        Func<Domain.Staff?, IReadOnlyList<Domain.Branch>?, CancellationToken, Task<IResult>> next,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is not null && actor.Role is not Domain.StaffRole.Owner and not Domain.StaffRole.Admin)
        {
            return next(actor, null, cancellationToken);
        }

        return OpsReportScope.MapAsync(
            http,
            branches,
            (shops, _, ct) => next(actor, shops, ct),
            cancellationToken);
    }
}
