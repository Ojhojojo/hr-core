namespace HrCore.Features.Payroll;

public static class PayrollDepositEndpoints
{
    public static IEndpointRouteBuilder MapPayrollDepositApi(this IEndpointRouteBuilder app)
    {
        // Own group so a non owner/admin gets problem details. The main payroll group answers
        // that case with a different JSON body, and that body stays as it is.
        var api = app.MapGroup("/api/ops/payroll/runs/{runId:guid}/deposit")
            .WithTags("Payroll")
            .AddEndpointFilter<StaffAuthorizationFilter>();

        api.MapPut("/", RecordDeposit)
            .WithName("PayrollRunDeposit")
            .WithSummary("Record what was deposited on a locked payroll line")
            .WithDescription(
                "Owner and admin. Body: staffId, depositedAmount (numeric(14,2), zero or more, at most 2 decimal places). " +
                "Discrepancy is net minus that amount. A negative amount or more than 2 decimal places is " +
                "400 problem details with a field error on depositedAmount. " +
                "A draft or void run is 409. The locked gross, deductions, and net stay as they are. " +
                "The entry is written to the HR change log as payroll.deposit. " +
                "403 problem details for any other staff role. " +
                "404 when the run is outside the shop scope or the person is not on the run.");

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

    private static Task<IResult> RecordDeposit(
        Guid runId,
        RecordPayrollDepositRequest body,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken)
    {
        var denied = DenyUnlessOwnerOrAdmin(http);
        if (denied is not null)
        {
            return Task.FromResult(denied);
        }

        if (body.StaffId == Guid.Empty)
        {
            return Task.FromResult<IResult>(Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["staffId"] = ["Pick a person."]
            }));
        }

        return OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var result = await payroll.RecordDepositAsync(
                    shops,
                    runId,
                    body.StaffId,
                    body.DepositedAmount,
                    http.GetStaff()!.Id,
                    ct);
                return ToResult(result);
            },
            cancellationToken);
    }

    private static IResult ToResult(PayrollDepositWriteResult result)
    {
        if (result.Errors is not null)
        {
            return Results.ValidationProblem(result.Errors);
        }

        if (result.Status == StatusCodes.Status409Conflict)
        {
            return Results.Problem(
                title: "Conflict",
                detail: result.Error,
                statusCode: StatusCodes.Status409Conflict);
        }

        return result.Saved is null
            ? HrProblems.Problem(404, result.Error ?? "That payroll run is not on this shop scope.")
            : Results.Ok(result.Saved);
    }
}

public sealed class RecordPayrollDepositRequest
{
    public Guid StaffId { get; set; }

    public decimal? DepositedAmount { get; set; }
}
