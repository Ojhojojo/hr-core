namespace HrCore.Infrastructure;

public sealed class StaffAuthorizationFilter(IHrDb db, IHrStaffResolver resolver, HrCoreOptions options) : IEndpointFilter
{
    public const string StaffItemKey = "Staff";
    public const string TestAuthUserHeader = "X-Test-Auth-User-Id";

    /// <summary>
    /// #322: marks a request whose caller is authenticated (test auth header or JWT) but has no
    /// active staff profile. Role checks must treat this caller as having no privileges.
    /// </summary>
    public const string NoStaffItemKey = "AuthenticatedWithoutStaff";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        // The test header signs a request in with no token. Only a host that opts in (the hr-core
        // test host) honours it. The environment name is never enough, so a deployed app that
        // happens to run as "Testing" stays closed.
        if (options.AllowTestAuthHeader)
        {
            var testAuth = http.Request.Headers[TestAuthUserHeader].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(testAuth))
            {
                await BindStaffAsync(http, db, testAuth, http.RequestAborted);
                MarkIfAuthenticatedWithoutStaff(http);
                return await next(context);
            }
        }

        // No token and no honoured header: nobody is signed in, so there is nothing to check.
        if (string.IsNullOrWhiteSpace(http.User.AuthUserId()))
        {
            return Results.Unauthorized();
        }

        // The host decides which staff row this sign-in is (IHrStaffResolver). The default matches
        // by sign-in id only.
        var staff = await resolver.FindAsync(http, http.RequestAborted);
        if (staff is not null)
        {
            http.Items[StaffItemKey] = staff;
        }
        else
        {
            http.Items.Remove(StaffItemKey);
        }

        MarkIfAuthenticatedWithoutStaff(http);

        if (!http.Items.ContainsKey(StaffItemKey))
        {
            return HrProblems.Problem(StatusCodes.Status403Forbidden, "Staff access required. Your account is not linked to an active staff profile.");
        }

        return await next(context);
    }

    private static async Task BindStaffAsync(
        HttpContext http,
        IHrDb db,
        string authUserId,
        CancellationToken cancellationToken)
    {
        var staff = await DefaultHrStaffResolver.FindActiveByAuthUserIdAsync(db, authUserId, cancellationToken);
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
