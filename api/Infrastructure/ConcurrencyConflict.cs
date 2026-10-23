using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Infrastructure;

/// <summary>
/// Optimistic concurrency. Tracked entities use the Postgres <c>xmin</c> system column as a
/// concurrency token (see <see cref="IHrDb"/>), so an UPDATE only applies when the row is
/// still the version this request loaded. A stale save surfaces as
/// <see cref="DbUpdateConcurrencyException"/> or <see cref="StaleVersionException"/>, and
/// <see cref="ConcurrencyConflictHandler"/> maps both to HTTP 409.
/// </summary>
public static class ConcurrencyConflict
{
    public const string Message = "Someone else changed this record. Reload and try again.";
}

/// <summary>
/// A request sent an <c>expectedVersion</c> that no longer matches the stored row version.
/// </summary>
public sealed class StaleVersionException() : Exception(ConcurrencyConflict.Message);

/// <summary>
/// Reads and refreshes the <c>xmin</c> row version of tracked entities.
/// </summary>
public static class RowVersion
{
    public const string PropertyName = "xmin";

    /// <summary>The row version this entity was loaded (or last saved) with.</summary>
    public static uint Of<T>(IHrDb db, T entity) where T : class =>
        db.Entry(entity).Property<uint>(PropertyName).CurrentValue;

    /// <summary>
    /// Sets the tracked entity's concurrency token to a row version read from the database, without
    /// marking anything modified. Used after a raw SQL statement in the same transaction bumped xmin,
    /// so the EF save that follows does not conflict with the request's own write.
    /// </summary>
    public static void Refresh<T>(IHrDb db, T entity, uint xmin) where T : class
    {
        var property = db.Entry(entity).Property<uint>(PropertyName);
        property.OriginalValue = xmin;
        property.CurrentValue = xmin;
        property.IsModified = false;
    }

    /// <summary>
    /// Reads and locks a row (SELECT ... FOR UPDATE) and returns its xmin. Holding the lock means no
    /// other transaction can change the row until this one ends.
    /// </summary>
    public static async Task<uint> LockAndReadAsync(
        IHrDb db,
        string table,
        Guid id,
        CancellationToken cancellationToken)
    {
        // The table name is a constant chosen by the caller, never request input. The id is a parameter.
        var sql = $"SELECT (xmin::text)::bigint AS \"Value\" FROM \"{table}\" WHERE \"Id\" = {{0}} FOR UPDATE";
        var rows = await db.Database.SqlQueryRaw<long>(sql, id).ToListAsync(cancellationToken);
        return rows.Count == 0 ? 0u : (uint)rows[0];
    }
}

/// <summary>
/// Maps concurrency failures to 409 with the shared message. Other exceptions are left to the
/// default handling.
/// </summary>
public sealed class ConcurrencyConflictHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not (DbUpdateConcurrencyException or StaleVersionException))
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        await httpContext.Response.WriteAsJsonAsync(new { error = ConcurrencyConflict.Message }, cancellationToken);
        return true;
    }
}
