using HrCore.Features.SettingsLog;

namespace HrCore.Features.Payroll;

public static class PayrollLoanKindEndpoints
{
    public static IEndpointRouteBuilder MapLoanKindApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/ops/payroll/loan-kinds")
            .WithTags("Payroll")
            .AddEndpointFilter<StaffAuthorizationFilter>();

        api.MapGet("/", ListKinds)
            .WithName("PayrollLoanKinds")
            .WithSummary("Loan kinds")
            .WithDescription(
                "Owner and admin. Each row is a name, whether it is active, and a cut-off rule. " +
                "cutoffOrdinal null means every period. A number means that cut-off only. " +
                "403 problem details for any other staff role.");

        api.MapPost("/", CreateKind)
            .WithName("PayrollLoanKindCreate")
            .WithSummary("Add a loan kind")
            .WithDescription(
                "Owner and admin. Body: name (required, 80), active (default true), " +
                "cutoffOrdinal (null, or 1 or more). A duplicate name is 409 problem details. " +
                "403 problem details for any other staff role.");

        api.MapPut("/{id:guid}", UpdateKind)
            .WithName("PayrollLoanKindUpdate")
            .WithSummary("Update a loan kind")
            .WithDescription(
                "Owner and admin. Same body as create. Deactivating a kind that loans already use is allowed. " +
                "A duplicate name is 409 problem details. 404 when the kind does not exist.");

        api.MapDelete("/{id:guid}", DeleteKind)
            .WithName("PayrollLoanKindDelete")
            .WithSummary("Delete a loan kind that no loan uses")
            .WithDescription(
                "Owner and admin. 409 problem details when any loan points at the kind. Deactivate it instead. " +
                "404 when the kind does not exist.");

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

    private static async Task<IResult> ListKinds(
        HttpContext http,
        PayrollService payroll,
        CancellationToken cancellationToken)
    {
        var denied = DenyUnlessOwnerOrAdmin(http);
        if (denied is not null)
        {
            return denied;
        }

        return Results.Ok(await payroll.ListLoanKindsAsync(cancellationToken));
    }

    private static async Task<IResult> CreateKind(
        SaveLoanKindRequest body,
        HttpContext http,
        PayrollService payroll,
        CancellationToken cancellationToken)
    {
        var denied = DenyUnlessOwnerOrAdmin(http);
        if (denied is not null)
        {
            return denied;
        }

        return ToWriteResult(await payroll.CreateLoanKindAsync(body, http.ActorStaffId(), cancellationToken));
    }

    private static async Task<IResult> UpdateKind(
        Guid id,
        SaveLoanKindRequest body,
        HttpContext http,
        PayrollService payroll,
        CancellationToken cancellationToken)
    {
        var denied = DenyUnlessOwnerOrAdmin(http);
        if (denied is not null)
        {
            return denied;
        }

        return ToWriteResult(await payroll.UpdateLoanKindAsync(id, body, http.ActorStaffId(), cancellationToken));
    }

    private static async Task<IResult> DeleteKind(
        Guid id,
        HttpContext http,
        PayrollService payroll,
        CancellationToken cancellationToken)
    {
        var denied = DenyUnlessOwnerOrAdmin(http);
        if (denied is not null)
        {
            return denied;
        }

        var result = await payroll.DeleteLoanKindAsync(id, http.ActorStaffId(), cancellationToken);
        if (result.Detail is not null)
        {
            return Conflict(result.Detail);
        }

        return result.Deleted
            ? Results.NoContent()
            : Results.NotFound(new { error = "Loan kind not found." });
    }

    private static IResult ToWriteResult(LoanKindWriteResult result)
    {
        if (result.Errors is not null)
        {
            return Results.ValidationProblem(result.Errors);
        }

        if (result.Detail is not null)
        {
            return Conflict(result.Detail);
        }

        return result.Saved is null
            ? Results.NotFound(new { error = "Loan kind not found." })
            : Results.Ok(result.Saved);
    }

    private static IResult Conflict(string detail) =>
        Results.Problem(
            title: "Conflict",
            detail: detail,
            statusCode: StatusCodes.Status409Conflict);
}
