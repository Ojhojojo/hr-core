namespace HrCore.Features.Payroll;

/// <summary>
/// Allowance math for one worker and one register period.
/// Inactive rows are skipped. A row applies when its effective range overlaps the period
/// (a blank end is open). Period pays <see cref="StaffAllowance.Amount"/> once, with no proration.
/// Day pays Amount times the count of days worked that fall inside both the period and the
/// effective range. Each item is rounded AwayFromZero to 2 decimals.
/// </summary>
public static class PayrollAllowanceCalculator
{
    public const string ItemKind = "allowance";
    public const string OverrideKey = "allowances";

    /// <summary>A payroll run line item label holds at most 120 characters.</summary>
    public const int LabelMaxLength = 120;

    /// <summary>
    /// The earnings line a scheduled sales incentive bonus shows on a register, payslip and locked run,
    /// e.g. "Sales incentive Q4 2026: Top closer". The reason is cut so the label fits <see cref="LabelMaxLength"/>.
    /// </summary>
    public static string IncentiveLabel(int year, int quarter, string reason)
    {
        var prefix = $"Sales incentive Q{quarter} {year}: ";
        var room = LabelMaxLength - prefix.Length;
        var text = reason.Trim();
        return prefix + (text.Length <= room ? text : text[..(room - 3)] + "...");
    }

    public sealed record Item(
        Guid AllowanceId,
        string Name,
        AllowancePer Per,
        bool Taxable,
        decimal Quantity,
        decimal Amount);

    public sealed record Result(IReadOnlyList<Item> Items, decimal Total)
    {
        public static Result Empty { get; } = new([], 0m);
    }

    public static Result Compute(
        IReadOnlyList<StaffAllowance> allowances,
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlySet<DateOnly> daysWorked)
    {
        if (periodStart > periodEnd)
        {
            (periodStart, periodEnd) = (periodEnd, periodStart);
        }

        var items = new List<Item>();
        foreach (var allowance in allowances
            .OrderBy(row => row.Name, StringComparer.Ordinal)
            .ThenBy(row => row.Id))
        {
            if (!allowance.Active)
            {
                continue;
            }

            var from = allowance.EffectiveFrom ?? DateOnly.MinValue;
            var to = allowance.EffectiveTo ?? DateOnly.MaxValue;
            if (from > to || from > periodEnd || to < periodStart)
            {
                continue;
            }

            decimal quantity;
            decimal raw;
            if (allowance.Per == AllowancePer.Day)
            {
                var count = 0;
                foreach (var day in daysWorked)
                {
                    if (day >= periodStart && day <= periodEnd && day >= from && day <= to)
                    {
                        count++;
                    }
                }

                quantity = count;
                raw = allowance.Amount * quantity;
            }
            else
            {
                quantity = 1m;
                raw = allowance.Amount;
            }

            items.Add(new Item(
                allowance.Id,
                allowance.Name,
                allowance.Per,
                allowance.Taxable,
                quantity,
                Math.Round(raw, 2, MidpointRounding.AwayFromZero)));
        }

        return new Result(items, items.Sum(item => item.Amount));
    }
}
