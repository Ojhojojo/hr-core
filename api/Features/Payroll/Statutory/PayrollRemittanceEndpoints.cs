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
                "403 problem details when the host capability refuses the caller.");

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
                var report = await remittance.GetAsync(shops, currentId, year, monthNumber, ct);
                if (report is null)
                {
                    return Results.NotFound(new { error = "That shop is not on your payroll scope." });
                }

                var agencies = report.Agencies
                    .Select(agency => new PayrollRemittance.Agency(
                        agency.Key,
                        agency.Label,
                        agency.EmployeeShare,
                        agency.EmployerShare))
                    .ToList();
                var file = PayrollRemittance.File(year, monthNumber, agencies);
                return Results.File(file.Bytes, "text/csv; charset=utf-8", file.FileName);
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
