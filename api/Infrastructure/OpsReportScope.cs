namespace HrCore.Infrastructure;

public enum OpsReportScopeKind
{
    Shop,
    Owned,
    Franchise,
    All
}

/// <summary>
/// Query param for owner dashboard / cash / AR / bookkeeper CSV:
/// <c>scope=shop|owned|franchise|all</c> (default shop) or <c>allShops=true</c> (same as all).
/// HQ owner: this shop, all live Owned, all live Franchise, or all live shops (excludes Demo).
/// Admin cannot request owned / franchise / all. Shop-scoped owners stay on HomeBranch
/// even when they send <c>scope=all</c> or <c>scope=franchise</c>.
/// </summary>
public static class OpsReportScope
{
    public const string QueryName = "scope";
    public const string AllShopsQueryName = "allShops";
    public const string Shop = "shop";
    public const string Owned = "owned";
    public const string Franchise = "franchise";
    public const string All = "all";
    public const string QueryDoc =
        "scope=shop|owned|franchise|all (default shop) or allShops=true. " +
        "HQ owner: this shop, all live Owned, all live Franchise, or all live shops (excludes Demo). " +
        "Shop-scoped owner stays on HomeBranch. Admin stays on the home / header branch.";

    public static OpsReportScopeKind Parse(IQueryCollection query)
    {
        var scope = query[QueryName].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(scope))
        {
            return scope.Trim().ToLowerInvariant() switch
            {
                All => OpsReportScopeKind.All,
                Owned => OpsReportScopeKind.Owned,
                Franchise => OpsReportScopeKind.Franchise,
                _ => OpsReportScopeKind.Shop
            };
        }

        var flag = query[AllShopsQueryName].FirstOrDefault();
        return string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(flag, "1", StringComparison.OrdinalIgnoreCase)
            ? OpsReportScopeKind.All
            : OpsReportScopeKind.Shop;
    }

    public static async Task<IResult> MapAsync(
        HttpContext http,
        BranchResolver branches,
        Func<IReadOnlyList<Domain.Branch>, Guid, CancellationToken, Task<IResult>> next,
        CancellationToken cancellationToken)
    {
        try
        {
            var shops = await branches.ResolveOpsReportScopeAsync(http, cancellationToken);
            var currentId = await branches.ResolveOpsAsync(http, cancellationToken);
            return await next((IReadOnlyList<Domain.Branch>)shops, currentId, cancellationToken);
        }
        catch (PublicBranchException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
        }
    }
}
