namespace HrCore.Infrastructure;

/// <summary>
/// Stock on-hand changes are applied in SQL at the moment they run, so they must share a database
/// transaction with the save that records the job or line change. This runs <paramref name="work"/>
/// inside one transaction, or joins the caller's transaction when one is already open. The work
/// must call <c>SaveChangesAsync</c> itself. A thrown exception rolls everything back.
/// </summary>
public static class StockTransaction
{
    public static async Task<T> RunAsync<T>(IHrDb db, Func<Task<T>> work, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await work();
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var result = await work();
        await tx.CommitAsync(cancellationToken);
        return result;
    }

    public static async Task RunAsync(IHrDb db, Func<Task> work, CancellationToken cancellationToken) =>
        await RunAsync(db, async () =>
        {
            await work();
            return true;
        }, cancellationToken);
}
