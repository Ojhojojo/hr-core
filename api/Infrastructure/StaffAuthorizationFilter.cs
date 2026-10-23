using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Infrastructure;

public sealed class StaffAuthorizationFilter(IHrDb db, IWebHostEnvironment environment) : IEndpointFilter
{
    public const string StaffItemKey = "Staff";
    public const string TestAuthUserHeader = "X-Test-Auth-User-Id";

    /// <summary>
    /// #322: marks a request whose caller is authenticated (test auth header or JWT) but has no
    /// active staff profile. Role checks must treat this caller as having no privileges instead
    /// of falling back to the unauthenticated (legacy test) path.
    /// </summary>
    public const string NoStaffItemKey = "AuthenticatedWithoutStaff";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (environment.IsEnvironment("Testing"))
        {
            var testAuth = context.HttpContext.Request.Headers[TestAuthUserHeader].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(testAuth))
            {
                await BindStaffAsync(context.HttpContext, db, testAuth, context.HttpContext.RequestAborted);
                MarkIfAuthenticatedWithoutStaff(context.HttpContext);
                return await next(context);
            }
        }

        var authUserId = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? context.HttpContext.User.FindFirstValue("sub");

        if (!string.IsNullOrWhiteSpace(authUserId))
        {
            await BindStaffAsync(context.HttpContext, db, authUserId, context.HttpContext.RequestAborted);
            MarkIfAuthenticatedWithoutStaff(context.HttpContext);
        }

        if (environment.IsEnvironment("Testing"))
        {
            return await next(context);
        }

        if (string.IsNullOrWhiteSpace(authUserId))
        {
            return Results.Unauthorized();
        }

        if (!context.HttpContext.Items.ContainsKey(StaffItemKey))
        {
            return Results.Json(
                new { error = "Staff access required. Your account is not linked to an active staff profile." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }

    private static async Task BindStaffAsync(
        HttpContext http,
        IHrDb db,
        string authUserId,
        CancellationToken cancellationToken)
    {
        var staff = await db.Staff.AsNoTracking()
            .Include(s => s.Profile)
            .FirstOrDefaultAsync(s => s.Profile!.AuthUserId == authUserId && s.Active, cancellationToken);
        if (staff is not null)
        {
            http.Items[StaffItemKey] = staff;
        }
        else
        {
            http.Items.Remove(StaffItemKey);
        }
    }

    private static void MarkIfAuthenticatedWithoutStaff(HttpContext http)
    {
        if (!http.Items.ContainsKey(StaffItemKey))
        {
            http.Items[NoStaffItemKey] = true;
        }
    }
}

public static class StaffHttpContextExtensions
{
    public static Staff? GetStaff(this HttpContext context) =>
        context.Items.TryGetValue(StaffAuthorizationFilter.StaffItemKey, out var value)
            ? value as Staff
            : null;

    /// <summary>
    /// #322: true when the caller is authenticated but not linked to an active staff profile
    /// (no staff role). Such a caller must never pass a role-gated or owner-only check.
    /// </summary>
    public static bool IsAuthenticatedWithoutStaff(this HttpContext context) =>
        context.Items.ContainsKey(StaffAuthorizationFilter.NoStaffItemKey);
}
