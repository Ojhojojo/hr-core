using Microsoft.EntityFrameworkCore;

namespace HrCore.Infrastructure;

/// <summary>
/// One page of a list. Paged endpoints return this only when the request has a <c>page</c> query
/// parameter; without it they keep their old response shape.
/// </summary>
public sealed record HrPage<T>(IReadOnlyList<T> Items, int Total, int PageNumber, int PageSize);

/// <summary>
/// Paged mode for the HR lists. <c>page</c> is 1-based and a value below 1 reads as 1.
/// <c>pageSize</c> defaults to 50, a value below 1 reads as 50, and a value above 200 reads as 200
/// (the same rules as <see cref="ListPaging"/>). The caller orders the list by a unique key first.
/// </summary>
public static class HrPaging
{
    public const int DefaultPageSize = ListPaging.DefaultTake;
    public const int MaxPageSize = ListPaging.MaxTake;

    /// <summary>Paged mode is on when the request carried a <c>page</c> parameter.</summary>
    public static bool Requested(int? page) => page is not null;

    public static (int PageNumber, int PageSize) Normalize(int? page, int? pageSize)
    {
        var number = page is int requested && requested >= 1 ? requested : 1;
        var size = pageSize ?? DefaultPageSize;
        if (size < 1)
        {
            size = DefaultPageSize;
        }

        if (size > MaxPageSize)
        {
            size = MaxPageSize;
        }

        return (number, size);
    }

    /// <summary>Pages an in-memory list that is already ordered.</summary>
    public static HrPage<T> ToPage<T>(IReadOnlyList<T> ordered, int? page, int? pageSize)
    {
        var (number, size) = Normalize(page, pageSize);
        var skip = SkipFor(number, size, ordered.Count);
        var items = ordered.Skip(skip).Take(size).ToList();
        return new HrPage<T>(items, ordered.Count, number, size);
    }

    /// <summary>Counts, then reads one page. The query must already be ordered, with a unique tie-breaker.</summary>
    public static async Task<HrPage<T>> ToPageAsync<T>(
        this IQueryable<T> ordered, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var (number, size) = Normalize(page, pageSize);
        var total = await ordered.CountAsync(cancellationToken);
        var skip = SkipFor(number, size, total);
        var items = new List<T>();
        if (skip < total)
        {
            items = await ordered.Skip(skip).Take(size).ToListAsync(cancellationToken);
        }

        return new HrPage<T>(items, total, number, size);
    }

    /// <summary>The offset of a page. Past the end it is the total, so the page is empty and no overflow occurs.</summary>
    private static int SkipFor(int pageNumber, int pageSize, int total) =>
        (int)Math.Min((long)(pageNumber - 1) * pageSize, total);
}
