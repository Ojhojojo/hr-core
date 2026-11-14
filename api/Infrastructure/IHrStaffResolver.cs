using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Infrastructure;

/// <summary>
/// Finds the active staff row for the signed-in caller. The host app may replace the default
/// (register its own before <c>AddHrCore</c>), for example to link a person by verified email on
/// first sign-in. Return null when the caller has no staff row; hr-core then answers 403.
/// </summary>
public interface IHrStaffResolver
{
    Task<Staff?> FindAsync(HttpContext http, CancellationToken cancellationToken);
}

/// <summary>Matches the active staff row whose profile has the caller's sign-in id. This is the default.</summary>
public sealed class DefaultHrStaffResolver(IHrDb db) : IHrStaffResolver
{
    public Task<Staff?> FindAsync(HttpContext http, CancellationToken cancellationToken)
    {
        var authUserId = http.User.AuthUserId();
        return string.IsNullOrWhiteSpace(authUserId)
            ? Task.FromResult<Staff?>(null)
            : FindActiveByAuthUserIdAsync(db, authUserId, cancellationToken);
    }

    internal static Task<Staff?> FindActiveByAuthUserIdAsync(
        IHrDb db,
        string authUserId,
        CancellationToken cancellationToken) =>
        db.Staff.AsNoTracking()
            .Include(s => s.Profile)
            .FirstOrDefaultAsync(s => s.Profile!.AuthUserId == authUserId && s.Active, cancellationToken);
}

internal static class HrCallerClaims
{
    /// <summary>The sign-in id from the token: the name identifier, else the <c>sub</c> claim.</summary>
    public static string? AuthUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
}
