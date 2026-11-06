namespace HrCore.Features.Payroll;

/// <summary>
/// How one monthly statutory share is placed on the cut-offs of a month.
/// <see cref="Prorate"/> is today's <see cref="PayrollStatutoryProration"/> result.
/// Cut-off numbers are positional and are not stored on the suggestion.
/// </summary>
public static class PayrollStatutoryAllocation
{
    public const string Prorate = "prorate";
    public const string Cutoff = "cutoff";
    public const string Split = "split";

    public static readonly IReadOnlyList<string> StatutoryKeys =
    [
        "sss",
        "philhealth",
        "pagibig",
        "wht"
    ];

    public static string LoanKey(string kind) => "loan:" + kind.Trim();

    /// <summary>
    /// <paramref name="prorated"/> is the amount <see cref="PayrollStatutoryProration"/> already produced.
    /// Cut-off only pays the full <paramref name="monthly"/> share on the matching period and zero on the others.
    /// Split divides the monthly share across the month's periods; the last period keeps the rounding remainder.
    /// A register range that is not a generated period keeps <paramref name="prorated"/>.
    /// </summary>
    public static decimal Amount(
        string? mode,
        decimal monthly,
        decimal prorated,
        int? cutoffOrdinal,
        int? matchedOrdinal,
        int periodCount)
    {
        var normalized = string.IsNullOrWhiteSpace(mode) ? Prorate : mode.Trim().ToLowerInvariant();
        if (normalized == Prorate)
        {
            return Round(prorated);
        }

        if (matchedOrdinal is null)
        {
            return Round(prorated);
        }

        if (normalized == Cutoff)
        {
            return cutoffOrdinal == matchedOrdinal ? Round(monthly) : 0m;
        }

        if (normalized == Split)
        {
            return SplitShare(monthly, periodCount, matchedOrdinal.Value);
        }

        return Round(prorated);
    }

    /// <summary>
    /// Loan instalment for one register period. Prorate keeps today's per-period amount on any range.
    /// Cut-off pays it only on that positional period. Split pays it on each generated period.
    /// The amount is identified by the period dates the caller matched, not by a stored ordinal.
    /// </summary>
    public static decimal LoanInstalment(
        string? mode,
        int? cutoffOrdinal,
        int? matchedOrdinal,
        decimal perPeriod)
    {
        var normalized = string.IsNullOrWhiteSpace(mode) ? Prorate : mode.Trim().ToLowerInvariant();
        if (normalized == Prorate)
        {
            return Round(perPeriod);
        }

        if (matchedOrdinal is null)
        {
            return 0m;
        }

        if (normalized == Cutoff)
        {
            return cutoffOrdinal == matchedOrdinal ? Round(perPeriod) : 0m;
        }

        if (normalized == Split)
        {
            return Round(perPeriod);
        }

        return Round(perPeriod);
    }

    public static decimal SplitShare(decimal monthly, int periodCount, int ordinal)
    {
        if (periodCount < 1 || ordinal < 1 || ordinal > periodCount)
        {
            return 0m;
        }

        var each = Round(monthly / periodCount);
        if (ordinal < periodCount)
        {
            return each;
        }

        return Round(monthly - (each * (periodCount - 1)));
    }

    public static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
