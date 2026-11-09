using System.Globalization;
using HrCore.Features.SettingsLog;

namespace HrCore.Features.Payroll;

public static class PayrollRemittanceEndpoints
{
    public static IEndpointRouteBuilder MapRemittanceApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/ops/payroll/remittance")
            .WithTags("Payroll")
            .AddEndpointFilter<StaffAuthorizationFilter>();

        api.MapGet("/", GetReport)
            .WithName("PayrollRemittance")
            .WithSummary("Monthly remittance report for the current shop")
            .WithDescription(
                "The host's remittance capability. The default is the owner-or-admin policy. " +
                "Query: month, strictly yyyy-MM. Omitted month is the current Asia/Manila month. " +
                "enabled is false until the shop turns the report on. When it is on, agencies list the employee share, " +
                "employer share, and total for SSS, PhilHealth, and Pag-IBIG across locked runs whose period ends in " +
                "that month. Voided runs are excluded. No locked run returns the empty message. " +
                "403 problem details when the capability refuses the caller.");

        api.MapGet("/export", ExportReport)
            .WithName("PayrollRemittanceExport")
            .WithSummary("Monthly remittance CSV")
            .WithDescription(
                "Same month and shop as the remittance report. CSV totals match the report. " +
                "409 when a locked run in that month is not reviewed, with those runs listed by period and name. " +
                "A later export of an exported month returns the same figures. " +
                "403 problem details when the host capability refuses the caller.");

        api.MapGet("/runs/{id:guid}", GetReview)
            .WithName("PayrollEmployerShareReview")
            .WithSummary("Suggested employer shares for one locked run")
            .WithDescription(
                "The host's review capability. Suggested shares come from the single shares calculation. " +
                "Stored shares are null until confirm. readOnly is true after the run's month is exported. " +
                "403 when the capability refuses the caller. 404 when the run is outside the shop or is not locked.");

        api.MapPut("/runs/{id:guid}", ConfirmReview)
            .WithName("PayrollEmployerShareConfirm")
            .WithSummary("Confirm employer shares for one locked run")
            .WithDescription(
                "The host's review capability. Body: lines, one per person on the run, each with employerSss, " +
                "employerPhilHealth, and employerPagIbig of zero or more. Stores those figures and sets " +
                "EmployerSharesReviewedAtUtc and EmployerSharesReviewedBy. A later confirm before export " +
                "replaces the figures and updates both review fields. 409 when the month has been exported. " +
                "403 when the capability refuses the caller. 404 when the run is outside the shop or is not locked.");

        api.MapPut("/", SaveSetting)
            .WithName("PayrollRemittanceSave")
            .WithSummary("Turn the remittance report on or off for the current shop")
            .WithDescription(
                "The host's remittance capability. Body: enabled (true or false). Missing enabled is 400 problem details. " +
                "Off is the default. Turning it on does not rewrite a run already locked. " +
                "Logged on the settings trail. 403 problem details when the capability refuses the caller.");

        return app;
    }

    private static IResult? DenyUnlessCanView(HttpContext http, IHrRemittanceAccess access)
    {
        var staff = http.GetStaff();
        if (staff is not null && access.CanView(staff.Role))
        {
            return null;
        }

        return Results.Problem(
            title: "Forbidden",
            detail: "This screen is not available for your role.",
            statusCode: StatusCodes.Status403Forbidden);
    }

    private static Task<IResult> GetReport(
        string? month,
        HttpContext http,
        BranchResolver branches,
        PayrollRemittanceService remittance,
        IHrRemittanceAccess access,
        CancellationToken cancellationToken)
    {
        var denied = DenyUnlessCanView(http, access);
        if (denied is not null)
        {
            return Task.FromResult(denied);
        }

        if (!TryParseMonth(month, out var year, out var monthNumber))
        {
            return Task.FromResult<IResult>(MonthProblem());
        }

        return OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, currentId, ct) =>
            {
                var report = await remittance.GetAsync(shops, currentId, year, monthNumber, ct);
                return report is null
                    ? Results.NotFound(new { error = "That shop is not on your payroll scope." })
                    : Results.Ok(report);
            },
            cancellationToken);
    }

    private static Task<IResult> ExportReport(
        string? month,
        HttpContext http,
        BranchResolver branches,
        PayrollRemittanceService remittance,
        IHrRemittanceAccess access,
        CancellationToken cancellationToken)
    {
        var denied = DenyUnlessCanView(http, access);
        if (denied is not null)
        {
            return Task.FromResult(denied);
        }

        if (!TryParseMonth(month, out var year, out var monthNumber))
        {
            return Task.FromResult<IResult>(MonthProblem());
        }

        return OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, currentId, ct) =>
            {
                var (file, blocking, error) = await remittance.ExportAsync(
                    shops, currentId, year, monthNumber, http.ActorStaffId(), ct);
                if (error is not null)
                {
                    return Results.Json(
                        new { error, blockingRuns = blocking },
                        statusCode: StatusCodes.Status409Conflict);
                }

                return file is null
                    ? Results.NotFound(new { error = "That shop is not on your payroll scope." })
                    : Results.File(file.Bytes, "text/csv; charset=utf-8", file.FileName);
            },
            cancellationToken);
    }

    private static Task<IResult> SaveSetting(
        UpdateRemittanceSettingRequest? body,
        HttpContext http,
        BranchResolver branches,
        PayrollRemittanceService remittance,
        IHrRemittanceAccess access,
        CancellationToken cancellationToken)
    {
        var denied = DenyUnlessCanView(http, access);
        if (denied is not null)
        {
            return Task.FromResult(denied);
        }

        return OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, currentId, ct) =>
            {
                var (saved, errors) = await remittance.SetEnabledAsync(
                    shops, currentId, body, http.ActorStaffId(), ct);
                if (errors is not null)
                {
                    return Results.ValidationProblem(errors);
                }

                return saved is null
                    ? Results.NotFound(new { error = "That shop is not on your payroll scope." })
                    : Results.Ok(saved);
            },
            cancellationToken);
    }

    private static Task<IResult> GetReview(
        Guid id,
        HttpContext http,
        BranchResolver branches,
        PayrollRemittanceService remittance,
        IHrEmployerShareReview review,
        CancellationToken cancellationToken)
    {
        var denied = DenyUnlessCanReview(http, review);
        if (denied is not null)
        {
            return Task.FromResult(denied);
        }

        return OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var body = await remittance.GetReviewAsync(shops, id, IncludeUnscopedOwners(http), ct);
                return body is null
                    ? Results.NotFound(new { error = "That payroll run is not on this shop scope." })
                    : Results.Ok(body);
            },
            cancellationToken);
    }

    private static Task<IResult> ConfirmReview(
        Guid id,
        ConfirmEmployerSharesRequest? body,
        HttpContext http,
        BranchResolver branches,
        PayrollRemittanceService remittance,
        IHrEmployerShareReview review,
        CancellationToken cancellationToken)
    {
        var denied = DenyUnlessCanReview(http, review);
        if (denied is not null)
        {
            return Task.FromResult(denied);
        }

        return OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var (saved, errors, conflict) = await remittance.ConfirmAsync(
                    shops, id, body, http.ActorStaffId(), IncludeUnscopedOwners(http), ct);
                if (conflict is not null)
                {
                    return Results.Json(new { error = conflict }, statusCode: StatusCodes.Status409Conflict);
                }

                if (errors is not null)
                {
                    return Results.ValidationProblem(errors);
                }

                return saved is null
                    ? Results.NotFound(new { error = "That payroll run is not on this shop scope." })
                    : Results.Ok(saved);
            },
            cancellationToken);
    }

    private static IResult? DenyUnlessCanReview(HttpContext http, IHrEmployerShareReview review)
    {
        var staff = http.GetStaff();
        if (staff is not null && review.CanReview(staff.Role))
        {
            return null;
        }

        return Results.Problem(
            title: "Forbidden",
            detail: "This screen is not available for your role.",
            statusCode: StatusCodes.Status403Forbidden);
    }

    private static bool IncludeUnscopedOwners(HttpContext http) =>
        http.GetStaff().IsHqOwner();

    internal static bool TryParseMonth(string? month, out int year, out int monthNumber)
    {
        if (string.IsNullOrWhiteSpace(month))
        {
            var today = ManilaTime.Today();
            year = today.Year;
            monthNumber = today.Month;
            return true;
        }

        var text = month.Trim();
        if (text.Length != 7 || text[4] != '-')
        {
            year = 0;
            monthNumber = 0;
            return false;
        }

        if (!int.TryParse(text.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out year)
            || !int.TryParse(text.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out monthNumber))
        {
            year = 0;
            monthNumber = 0;
            return false;
        }

        return year is >= 2000 and <= 2100 && monthNumber is >= 1 and <= 12;
    }

    private static IResult MonthProblem() =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["month"] = ["Month must be yyyy-MM."]
        });
}
