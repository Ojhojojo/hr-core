using HrCore.Features.SettingsLog;

namespace HrCore.Features.Payroll;

public static class PayrollDeductionScheduleEndpoints
{
    public static IEndpointRouteBuilder MapDeductionScheduleApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/ops/payroll/deduction-schedule")
            .WithTags("Payroll")
            .AddEndpointFilter<StaffAuthorizationFilter>();

        api.MapGet("/", GetSchedule)
            .WithName("PayrollDeductionSchedule")
            .WithSummary("Which cut-off each deduction uses")
            .WithDescription(
                "Owner and admin. No rows means every deduction is prorated as today. " +
                "Rules name sss, philhealth, pagibig, wht, or loan: plus the loan kind. " +
                "cutoffOrdinal is the current list position only. It is not stored on deductions or runs. " +
                "403 problem details for any other staff role.");

        api.MapPut("/", SaveSchedule)
            .WithName("PayrollDeductionScheduleSave")
            .WithSummary("Save the deduction cut-off rules")
            .WithDescription(
                "Owner and admin. mode is prorate, cutoff, or split. " +
                "cutoff requires a cut-off number that exists. Referencing cut-off 3 when only 2 exist is " +
                "400 problem details with a field error. Prorate rows are not stored. " +
                "Logged on the settings trail. 403 problem details for any other staff role.");

        var catchUps = app.MapGroup("/api/ops/payroll/catch-ups")
            .WithTags("Payroll")
            .AddEndpointFilter<StaffAuthorizationFilter>();

        catchUps.MapPut("/", SaveCatchUp)
            .WithName("PayrollCatchUpSave")
            .WithSummary("Save a catch-up amount for one period")
            .WithDescription(
                "Owner and admin. Body: staffId, periodFrom, periodTo (yyyy-MM-dd), deductionKey, " +
                "amount (numeric, at most 2 decimal places), note. A positive amount requires a note " +
                "(400 problem details on note when it is empty). Amount 0 clears the row. " +
                "The period is the from and to dates. No cut-off number is stored. " +
                "403 problem details for any other staff role.");

        return app;
    }

    private static IResult? DenyUnlessOwnerOrAdmin(HttpContext http)
    {
        var staff = http.GetStaff();
        if (staff is { Role: StaffRole.Owner or StaffRole.Admin })
        {
            return null;
        }

        return Results.Problem(
            title: "Forbidden",
            detail: "This screen is not available for your role.",
            statusCode: StatusCodes.Status403Forbidden);
    }

    private static Task<IResult> GetSchedule(
        HttpContext http,
        BranchResolver branches,
        PayrollDeductionScheduleService schedules,
        CancellationToken cancellationToken)
    {
        var denied = DenyUnlessOwnerOrAdmin(http);
        if (denied is not null)
        {
            return Task.FromResult(denied);
        }

        return OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, currentId, ct) =>
            {
                var schedule = await schedules.GetAsync(shops, currentId, ct);
                return schedule is null
                    ? HrProblems.Problem(404, "That shop is not on your payroll scope.")
                    : Results.Ok(schedule);
            },
            cancellationToken);
    }

    private static Task<IResult> SaveSchedule(
        UpdateDeductionScheduleRequest body,
        HttpContext http,
        BranchResolver branches,
        PayrollDeductionScheduleService schedules,
        CancellationToken cancellationToken)
    {
        var denied = DenyUnlessOwnerOrAdmin(http);
        if (denied is not null)
        {
            return Task.FromResult(denied);
        }

        return OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, currentId, ct) =>
            {
                var (saved, errors) = await schedules.SaveAsync(
                    shops, currentId, body, http.ActorStaffId(), ct);
                if (errors is not null)
                {
                    return Results.ValidationProblem(errors);
                }

                return saved is null
                    ? HrProblems.Problem(404, "That shop is not on your payroll scope.")
                    : Results.Ok(saved);
            },
            cancellationToken);
    }

    private static async Task<IResult> SaveCatchUp(
        SavePayrollCatchUpRequest body,
        HttpContext http,
        BranchResolver branches,
        PayrollDeductionScheduleService schedules,
        CancellationToken cancellationToken)
    {
        var denied = DenyUnlessOwnerOrAdmin(http);
        if (denied is not null)
        {
            return denied;
        }

        var shops = await branches.ResolveOpsReportScopeAsync(http, cancellationToken);
        var (saved, errors, status) = await schedules.SaveCatchUpAsync(
            shops, body, http.ActorStaffId(), http.GetStaff().IsHqOwner(), cancellationToken);
        if (errors is not null)
        {
            return Results.ValidationProblem(errors);
        }

        if (status == StatusCodes.Status204NoContent)
        {
            return Results.NoContent();
        }

        return saved is null
            ? HrProblems.Problem(404, "That person is not on your payroll scope.")
            : Results.Ok(saved);
    }
}
