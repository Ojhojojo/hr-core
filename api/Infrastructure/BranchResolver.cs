using Microsoft.EntityFrameworkCore;

namespace HrCore.Infrastructure;

public static class BranchCodes
{
    public const string Roosevelt = "main-roosevelt";
    public const string Demo = "demo";
}

public sealed class PublicBranchException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class BranchResolver(IHrDb db, DemoBranchOptions demoOptions)
{
    public const string HeaderName = "X-Branch-Code";

    public async Task<Organization> OrganizationAsync(CancellationToken cancellationToken) =>
        await db.Organizations.AsNoTracking()
            .FirstAsync(o => o.Id == PlatformIds.OrganizationId, cancellationToken);

    public async Task<Branch> DefaultLiveAsync(CancellationToken cancellationToken) =>
        await db.Branches
            .Where(b => b.IsLive)
            .OrderBy(b => b.CreatedAtUtc)
            .FirstAsync(cancellationToken);

    public async Task<Branch> ResolvePublicAsync(string? branchCode, CancellationToken cancellationToken)
    {
        var org = await OrganizationAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(branchCode))
        {
            if (org.DemoPublicEnabled && demoOptions.Enabled)
            {
                throw new PublicBranchException(
                    StatusCodes.Status400BadRequest,
                    "branchCode is required when the company page is on.");
            }

            return await DefaultLiveAsync(cancellationToken);
        }

        var code = branchCode.Trim().ToLowerInvariant();
        if (code == BranchCodes.Demo && (!demoOptions.Enabled || !org.DemoPublicEnabled))
        {
            throw new PublicBranchException(StatusCodes.Status404NotFound, "Demo branch is not available.");
        }

        var branch = await db.Branches.FirstOrDefaultAsync(b => b.Code == code, cancellationToken);
        if (branch is null)
        {
            throw new PublicBranchException(StatusCodes.Status404NotFound, "Unknown branch.");
        }

        if (!branch.IsLive && (!demoOptions.Enabled || !org.DemoPublicEnabled))
        {
            throw new PublicBranchException(StatusCodes.Status404NotFound, "Demo branch is not available.");
        }

        return branch;
    }

    public async Task<Guid> ResolveOpsAsync(HttpContext http, CancellationToken cancellationToken)
    {
        var staff = http.GetStaff();
        if (staff.IsShopScopedOwner() && staff!.BranchId is Guid scopedHome)
        {
            var pinned = await db.Branches.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == scopedHome, cancellationToken);
            if (pinned is null)
            {
                throw new PublicBranchException(StatusCodes.Status404NotFound, "Unknown branch.");
            }

            return pinned.Id;
        }

        var allowed = await AllowedOpsAsync(staff, cancellationToken);
        var requested = http.Request.Headers[HeaderName].FirstOrDefault()
                        ?? http.Request.Query["branch"].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(requested))
        {
            var code = requested.Trim().ToLowerInvariant();
            var match = allowed.FirstOrDefault(b => b.Code == code);
            if (match is not null)
            {
                return match.Id;
            }
        }

        if (staff?.BranchId is Guid home)
        {
            var homeBranch = allowed.FirstOrDefault(b => b.Id == home);
            if (homeBranch is not null)
            {
                return homeBranch.Id;
            }
        }

        if (allowed.Count > 0)
        {
            return allowed[0].Id;
        }

        return (await DefaultLiveAsync(cancellationToken)).Id;
    }

    public Task<IReadOnlyList<Branch>> ListOpsAsync(HttpContext http, CancellationToken cancellationToken) =>
        AllowedOpsAsync(http.GetStaff(), cancellationToken);

    /// <summary>
    /// Dashboard / cash / AR / export shop list.
    /// <c>scope=owned|franchise|all</c> or <c>allShops=true</c> is HQ-owner-only (excludes Demo).
    /// Shop-scoped owners stay on HomeBranch even if they send a shop header or a network scope.
    /// Admin (and <c>scope=shop</c>) stay on the resolved home / header branch.
    /// </summary>
    public async Task<IReadOnlyList<Branch>> ResolveOpsReportScopeAsync(
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var staff = http.GetStaff();
        if (staff.IsShopScopedOwner())
        {
            var homeId = await ResolveOpsAsync(http, cancellationToken);
            var home = await db.Branches.AsNoTracking()
                .FirstAsync(b => b.Id == homeId, cancellationToken);
            return [home];
        }

        var scope = OpsReportScope.Parse(http.Request.Query);
        if (scope != OpsReportScopeKind.Shop)
        {
            if (staff is not null && staff.Role != StaffRole.Owner)
            {
                throw new PublicBranchException(
                    StatusCodes.Status403Forbidden,
                    "All shops is owner-only.");
            }

            var allowed = await AllowedOpsAsync(staff, cancellationToken);
            return scope switch
            {
                OpsReportScopeKind.Owned =>
                    allowed.Where(b => b.Kind == BranchKind.Owned).ToList(),
                OpsReportScopeKind.Franchise =>
                    allowed.Where(b => b.Kind == BranchKind.Franchise).ToList(),
                _ => allowed
            };
        }

        var id = await ResolveOpsAsync(http, cancellationToken);
        var current = await db.Branches.AsNoTracking()
            .FirstAsync(b => b.Id == id, cancellationToken);
        return [current];
    }

    /// <summary>Live shops a staff member can work in (same rule the ops endpoints use). Null = no staff.</summary>
    public async Task<IReadOnlyList<Branch>> AllowedOpsAsync(Staff? staff, CancellationToken cancellationToken)
    {
        if (staff.IsShopScopedOwner() && staff!.BranchId is Guid scopedHome)
        {
            return await db.Branches.AsNoTracking()
                .Where(b => b.Id == scopedHome)
                .ToListAsync(cancellationToken);
        }

        if (staff is not null && staff.Role != StaffRole.Owner)
        {
            if (staff.BranchId is Guid home)
            {
                return await db.Branches.AsNoTracking()
                    .Where(b => b.Id == home)
                    .ToListAsync(cancellationToken);
            }

            return [await DefaultLiveAsync(cancellationToken)];
        }

        if (staff?.BranchId is Guid ownerHome)
        {
            var home = await db.Branches.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == ownerHome, cancellationToken);
            if (home is not null && home.Code == BranchCodes.Demo && demoOptions.Enabled)
            {
                return [home];
            }
        }

        return await db.Branches.AsNoTracking()
            .Where(b => b.OrganizationId == PlatformIds.OrganizationId && b.IsLive && b.Code != BranchCodes.Demo)
            .OrderBy(b => b.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }
}
