namespace HrCore.Infrastructure;

/// <summary>Allows only the listed roles. The host app adds its own role sets in another part of this class.</summary>
public sealed partial class StaffRoleFilter(params StaffRole[] allowed) : IEndpointFilter
{
    public static StaffRoleFilter OwnerOnly { get; } = new(StaffRole.Owner);

    public static StaffRoleFilter OwnerOrAdmin { get; } =
        new(StaffRole.Owner, StaffRole.Admin);

    /// <summary>Any active staff role. Used where the handler scopes rows to the caller (commission).</summary>
    public static StaffRoleFilter AnyStaff { get; } =
        new(Enum.GetValues<StaffRole>());

    public bool Allows(StaffRole role) => allowed.Contains(role);

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var staff = context.HttpContext.GetStaff();
        if (staff is null)
        {
            // #322: an authenticated caller with no staff profile has no role — deny.
            // The unauthenticated (legacy test) caller keeps the previous pass-through.
            if (context.HttpContext.IsAuthenticatedWithoutStaff())
            {
                return Results.Json(
                    new { error = "This screen is not available for your role." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            return await next(context);
        }

        if (!allowed.Contains(staff.Role))
        {
            return Results.Json(
                new { error = "This screen is not available for your role." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }
}

/// <summary>
/// Settings branch create/edit/delete and Demo flag: unscoped company owner only.
/// Shop-scoped owners stay <see cref="StaffRole.Owner"/> but cannot manage shops.
/// </summary>
public sealed class HqOwnerFilter : IEndpointFilter
{
    public static HqOwnerFilter Instance { get; } = new();

    public const string Error = "Only the company owner can add or edit shops.";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var staff = context.HttpContext.GetStaff();
        if (staff.IsShopScopedOwner())
        {
            return Results.Json(new { error = Error }, statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }
}
