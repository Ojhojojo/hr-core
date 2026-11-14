using HrCore.Features.SettingsLog;

namespace HrCore.Features.Payroll;

public static class PayrollStatutoryRateEndpoints
{
    public static IEndpointRouteBuilder MapStatutoryRateApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/ops/payroll/statutory-rates")
            .WithTags("Payroll")
            .AddEndpointFilter<StaffAuthorizationFilter>()
            .AddEndpointFilter(StaffRoleFilter.OwnerOnly);

        api.MapGet("/", ListRates)
            .WithName("PayrollStatutoryRates")
            .WithSummary("Effective-dated statutory rates")
            .WithDescription(
                "Owner only. Each row is one government table (sss, philhealth, pagibig, or wht) " +
                "with an effectiveFrom date. Payroll uses the latest row of each kind on or before " +
                "the month. 403 problem details for any other staff role.");

        api.MapPost("/", CreateRate)
            .WithName("PayrollStatutoryRateCreate")
            .WithSummary("Add a dated statutory rate row")
            .WithDescription(
                "Owner only. Body: kind, effectiveFrom (yyyy-MM-dd), floors, ceilings, rates, and " +
                "for withholding, brackets. Overlapping kind+date or invalid numbers are 400 " +
                "problem details. 403 problem details for any other staff role.");

        api.MapPut("/{id:guid}", UpdateRate)
            .WithName("PayrollStatutoryRateUpdate")
            .WithSummary("Update a dated statutory rate row")
            .WithDescription(
                "Owner only. Same body as create. Withholding brackets are replaced. " +
                "Overlapping kind+date or invalid numbers are 400 problem details. 404 when missing.");

        api.MapDelete("/{id:guid}", DeleteRate)
            .WithName("PayrollStatutoryRateDelete")
            .WithSummary("Delete a dated statutory rate row")
            .WithDescription("Owner only. 404 when the row does not exist.");

        return app;
    }

    private static async Task<IResult> ListRates(
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        Results.Ok(await payroll.ListStatutoryRatesAsync(cancellationToken));

    private static async Task<IResult> CreateRate(
        SaveStatutoryRateRequest body,
        HttpContext http,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        ToWriteResult(await payroll.CreateStatutoryRateAsync(body, http.ActorStaffId(), cancellationToken));

    private static async Task<IResult> UpdateRate(
        Guid id,
        SaveStatutoryRateRequest body,
        HttpContext http,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        ToWriteResult(await payroll.UpdateStatutoryRateAsync(id, body, http.ActorStaffId(), cancellationToken));

    private static async Task<IResult> DeleteRate(
        Guid id,
        HttpContext http,
        PayrollService payroll,
        CancellationToken cancellationToken)
    {
        var result = await payroll.DeleteStatutoryRateAsync(id, http.ActorStaffId(), cancellationToken);
        return result.Deleted
            ? Results.NoContent()
            : HrProblems.Problem(404, "Statutory rate row not found.");
    }

    private static IResult ToWriteResult(StatutoryRateWriteResult result)
    {
        if (result.Errors is not null)
        {
            return Results.ValidationProblem(result.Errors);
        }

        return result.Saved is null
            ? HrProblems.Problem(404, "Statutory rate row not found.")
            : Results.Ok(result.Saved);
    }
}
