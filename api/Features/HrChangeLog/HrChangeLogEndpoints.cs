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
                "take (default 100, max 500). Add page (1-based) and pageSize (default 50, max 200) to get " +
                "{ items, total, pageNumber, pageSize } instead of { rows }. " +
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
        int? page,
        int? pageSize,
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
            return RespondAsync(hr, [actor.Id], field, from, to, capped, page, pageSize, cancellationToken);
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
                        ? await RespondAsync(hr, [one], field, from, to, capped, page, pageSize, ct)
                        : HrProblems.Problem(404, "That person is not in this shop scope.");
                }

                return scopedStaffIds.Count == 0
                    ? RespondEmpty(page, pageSize)
                    : await RespondAsync(hr, scopedStaffIds, field, from, to, capped, page, pageSize, ct);
            },
            cancellationToken);
    }

    /// <summary>Without <c>page</c> the old <c>{ rows }</c> shape; with it, one page as <c>{ items, total, pageNumber, pageSize }</c>.</summary>
    private static async Task<IResult> RespondAsync(
        HrChangeLogService hr,
        IReadOnlyCollection<Guid> subjectStaffIds,
        string? field,
        DateOnly? from,
        DateOnly? to,
        int take,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        if (HrPaging.Requested(page))
        {
            return Results.Ok(await hr.ListPageAsync(subjectStaffIds, field, from, to, page, pageSize, cancellationToken));
        }

        var rows = await hr.ListAsync(subjectStaffIds, field, from, to, take, cancellationToken);
        return Results.Ok(new HrChangeLogListResponse(rows));
    }

    /// <summary>No person in scope: the same empty answer as a query would give, in the requested shape.</summary>
    private static IResult RespondEmpty(int? page, int? pageSize)
    {
        if (!HrPaging.Requested(page))
        {
            return Results.Ok(new HrChangeLogListResponse([]));
        }

        var (pageNumber, size) = HrPaging.Normalize(page, pageSize);
        return Results.Ok(new HrPage<HrChangeLogRow>([], 0, pageNumber, size));
    }

    private static IResult Forbidden(string error) =>
        HrProblems.Problem(StatusCodes.Status403Forbidden, error);
}
