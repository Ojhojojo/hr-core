using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.HrChangeLog;

/// <summary>Response shape for the people change history.</summary>
public sealed record HrChangeLogListResponse(IReadOnlyList<HrChangeLogRow> Rows);

public static class HrChangeLogEndpoints
{
    public const int DefaultTake = 100;

    public static IEndpointRouteBuilder MapHrChangeLogApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/hr/change-log")
            .WithTags("HR")
            .AddEndpointFilter<StaffAuthorizationFilter>();

        api.MapGet("/", List)
            .WithName("ListHrChangeLog")
            .WithSummary("People change history")
            .WithDescription(
                "Query: staffId (optional), field (optional), from, to (YYYY-MM-DD, Asia/Manila), " +
                "take (default 100, max 500). " +
                OpsReportScope.QueryDoc +
                " Owner and admin read the rows for staff in the shop scope; a technician or sales " +
                "member reads only their own rows. Rows are append-only: there is no edit or delete.");

        return app;
    }

    private static Task<IResult> List(
        Guid? staffId,
        string? field,
        DateOnly? from,
        DateOnly? to,
        int? take,
        HttpContext http,
        BranchResolver branches,
        IHrDb db,
        HrChangeLogService hr,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Task.FromResult(Forbidden("Staff access required."));
        }

        var capped = take ?? DefaultTake;

        if (actor.Role is not (StaffRole.Owner or StaffRole.Admin))
        {
            // A worker reads their own rows only. Any staffId filter is ignored.
            return RespondAsync(hr, [actor.Id], field, from, to, capped, cancellationToken);
        }

        return OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var shopIds = shops.Select(shop => shop.Id).ToList();
                var includeUnscopedOwners = actor.IsHqOwner()
                    && OpsReportScope.Parse(http.Request.Query) == OpsReportScopeKind.All;

                var scopedStaffIds = await db.Staff.AsNoTracking()
                    .Where(person =>
                        (person.BranchId != null && shopIds.Contains(person.BranchId.Value)) ||
                        (includeUnscopedOwners && person.BranchId == null && person.Role == StaffRole.Owner))
                    .Select(person => person.Id)
                    .ToListAsync(ct);

                if (staffId is Guid one)
                {
                    return scopedStaffIds.Contains(one)
                        ? await RespondAsync(hr, [one], field, from, to, capped, ct)
                        : Results.NotFound(new { error = "That person is not in this shop scope." });
                }

                return scopedStaffIds.Count == 0
                    ? Results.Ok(new HrChangeLogListResponse([]))
                    : await RespondAsync(hr, scopedStaffIds, field, from, to, capped, ct);
            },
            cancellationToken);
    }

    private static async Task<IResult> RespondAsync(
        HrChangeLogService hr,
        IReadOnlyCollection<Guid> subjectStaffIds,
        string? field,
        DateOnly? from,
        DateOnly? to,
        int take,
        CancellationToken cancellationToken)
    {
        var rows = await hr.ListAsync(subjectStaffIds, field, from, to, take, cancellationToken);
        return Results.Ok(new HrChangeLogListResponse(rows));
    }

    private static IResult Forbidden(string error) =>
        Results.Json(new { error }, statusCode: StatusCodes.Status403Forbidden);
}
