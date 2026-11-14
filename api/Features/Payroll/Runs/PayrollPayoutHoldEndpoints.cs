namespace HrCore.Features.Payroll;

public static class PayrollPayoutHoldEndpoints
{
    public static IEndpointRouteBuilder MapPayoutHoldApi(this IEndpointRouteBuilder app)
    {
        // Own group so a non owner/admin gets problem details. The main payroll group answers
        // that case with a different JSON body, and that body stays as it is.
        var api = app.MapGroup("/api/ops/payroll/payout-holds")
            .WithTags("Payroll")
            .AddEndpointFilter<StaffAuthorizationFilter>();

        api.MapGet("/", ListHolds)
            .WithName("PayrollPayoutHolds")
            .WithSummary("Payout holds")
            .WithDescription(
                "Owner and admin. Active holds for people on the shop scope. " +
                "Each row is the person, the reason, and who put the payout on hold. " +
                "403 problem details for any other staff role.");

        api.MapPost("/", SetHold)
            .WithName("PayrollPayoutHoldSet")
            .WithSummary("Hold a payout until clearance")
            .WithDescription(
                "Owner and admin. Body: staffId, reason (required). " +
                "A blank reason is 400 problem details with a field error on reason. " +
                "The next lock still computes the line and marks it held. " +
                "403 problem details for any other staff role. " +
                "404 when the person is outside the shop scope.");

        api.MapPost("/{staffId:guid}/release", ReleaseHold)
            .WithName("PayrollPayoutHoldRelease")
            .WithSummary("Release a payout hold")
            .WithDescription(
                "Owner and admin. Clearance is approved. Held lines for this person show " +
                "released, the Manila date (yyyy-MM-dd), and who released it. " +
                "Logged on the HR change log. 404 when the person is not on hold. " +
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

    private static bool IncludeUnscopedOwners(HttpContext http) =>
        http.GetStaff().IsHqOwner()
        && OpsReportScope.Parse(http.Request.Query) == OpsReportScopeKind.All;

    private static Task<IResult> ListHolds(
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

        return OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
                Results.Ok(await payroll.ListPayoutHoldsAsync(shops, IncludeUnscopedOwners(http), ct)),
            cancellationToken);
    }

    private static Task<IResult> SetHold(
        SetPayoutHoldRequest body,
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
                var result = await payroll.SetPayoutHoldAsync(
                    shops,
                    body.StaffId,
                    body.Reason,
                    IncludeUnscopedOwners(http),
                    http.GetStaff()!.Id,
                    ct);
                return ToResult(result);
            },
            cancellationToken);
    }

    private static Task<IResult> ReleaseHold(
        Guid staffId,
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

        return OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var result = await payroll.ReleasePayoutHoldAsync(
                    shops,
                    staffId,
                    IncludeUnscopedOwners(http),
                    http.GetStaff()!.Id,
                    ct);
                return ToResult(result);
            },
            cancellationToken);
    }

    private static IResult ToResult(PayoutHoldWriteResult result)
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
            ? HrProblems.Problem(404, result.Error ?? "That person is not on this shop scope.")
            : Results.Ok(result.Saved);
    }
}

public sealed class SetPayoutHoldRequest
{
    public Guid StaffId { get; set; }

    public string? Reason { get; set; }
}
