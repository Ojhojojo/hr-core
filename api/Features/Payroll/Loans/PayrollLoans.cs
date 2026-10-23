using System.Globalization;

namespace HrCore.Features.Payroll;

/// <summary>
/// Loan and cash-advance instalments for one register period.
/// Skip a cancelled loan, a balance of zero or less, and a loan whose start is after the period end.
/// The instalment is the per-period amount, or the override for that loan, and never more than the balance.
/// Loans are ordered by start, then by when they were created. The running total stops at
/// max(0, net before loans). A shortfall is a warning and carries to the next run.
/// Money is rounded AwayFromZero to 2 decimals.
/// </summary>
public static class PayrollLoanSchedule
{
    public const string ItemKind = "loan";
    public const string OverrideKeyPrefix = "loan:";

    public static readonly IReadOnlyList<string> DefaultKinds =
    [
        "Cash advance",
        "SSS loan",
        "Pag-IBIG loan",
        "Company loan"
    ];

    public sealed record LoanInput(
        Guid LoanId,
        string Kind,
        decimal Balance,
        decimal PerPeriodAmount,
        DateOnly StartPeriod,
        DateTime CreatedAtUtc,
        bool Cancelled);

    public sealed record Item(
        Guid LoanId,
        string Kind,
        decimal Suggested,
        decimal Amount,
        decimal BalanceBefore,
        decimal BalanceAfter,
        bool Capped);

    public sealed record Result(IReadOnlyList<Item> Items, decimal Total, IReadOnlyList<string> Warnings)
    {
        public static Result Empty { get; } = new([], 0m, []);
    }

    public static string OverrideKey(Guid loanId) => OverrideKeyPrefix + loanId.ToString("D");

    public static bool TryParseOverrideKey(string key, out Guid loanId)
    {
        loanId = Guid.Empty;
        if (!key.StartsWith(OverrideKeyPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return Guid.TryParse(key.AsSpan(OverrideKeyPrefix.Length), out loanId);
    }

    public static Result Suggest(
        IReadOnlyList<LoanInput> loans,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal netBeforeLoans,
        IReadOnlyDictionary<Guid, decimal> overrides)
    {
        if (periodStart > periodEnd)
        {
            (periodStart, periodEnd) = (periodEnd, periodStart);
        }

        var remaining = Round(netBeforeLoans);
        if (remaining < 0m)
        {
            remaining = 0m;
        }

        var items = new List<Item>();
        var warnings = new List<string>();
        foreach (var loan in loans
            .OrderBy(row => row.StartPeriod)
            .ThenBy(row => row.CreatedAtUtc)
            .ThenBy(row => row.LoanId))
        {
            var balance = Round(loan.Balance);
            if (loan.Cancelled || balance <= 0m || loan.StartPeriod > periodEnd)
            {
                continue;
            }

            var instalment = overrides.TryGetValue(loan.LoanId, out var overrideAmount)
                ? Round(overrideAmount)
                : Round(loan.PerPeriodAmount);
            if (instalment < 0m)
            {
                instalment = 0m;
            }

            if (instalment > balance)
            {
                instalment = balance;
            }

            var capped = instalment > remaining;
            var amount = capped ? remaining : instalment;
            var carry = Round(instalment - amount);
            if (capped)
            {
                warnings.Add(
                    "Loan instalment above net pay: " + loan.Kind +
                    " capped at ₱" + Peso(amount) + "; ₱" + Peso(carry) + " carries to the next run");
            }

            items.Add(new Item(
                loan.LoanId,
                loan.Kind,
                instalment,
                amount,
                balance,
                Round(balance - amount),
                capped));
            remaining = Round(remaining - amount);
        }

        return items.Count == 0
            ? Result.Empty
            : new Result(items, Round(items.Sum(item => item.Amount)), warnings);
    }

    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string Peso(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);
}
