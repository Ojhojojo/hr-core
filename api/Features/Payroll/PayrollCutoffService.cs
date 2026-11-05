using HrCore.Features.SettingsLog;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Payroll;

/// <summary>
/// Reads and replaces one branch's cut-off schedule. Saving never touches payroll runs:
/// a locked run keeps the dates and amounts it was frozen with.
/// </summary>
public sealed class PayrollCutoffService(IHrDb db)
{
    public async Task<PayrollCutoffScheduleDto?> GetAsync(
        IReadOnlyList<Branch> shops,
        Guid branchId,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        if (shops.All(shop => shop.Id != branchId))
        {
            return null;
        }

        var rows = await db.BranchPayrollCutoffs().AsNoTracking()
            .Where(row => row.BranchId == branchId)
            .OrderBy(row => row.Ordinal)
            .ToListAsync(cancellationToken);
        return ToDto(branchId, rows.Select(ToRule).ToList(), year, month);
    }

    /// <summary>
    /// Register payload. <paramref name="from"/> and <paramref name="to"/> in the same month
    /// return that month's periods. A range that crosses months returns each end month, deduplicated,
    /// earliest first. No rows means free dates and an empty list.
    /// </summary>
    public async Task<(string Mode, IReadOnlyList<PayrollCutoffPeriodDto> Periods)?> DescribeRegisterAsync(
        IReadOnlyList<Branch> shops,
        Guid branchId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        if (shops.All(shop => shop.Id != branchId))
        {
            return null;
        }

        var rows = await db.BranchPayrollCutoffs().AsNoTracking()
            .Where(row => row.BranchId == branchId)
            .OrderBy(row => row.Ordinal)
            .ToListAsync(cancellationToken);
        var rules = rows.Select(ToRule).ToList();
        if (rules.Count == 0)
        {
            return (PayrollCutoffMode.Free, []);
        }

        var periods = PayrollCutoffCalendar.PeriodsCovering(from, to, rules)
            .Select(period => new PayrollCutoffPeriodDto(period.Ordinal, period.From, period.To))
            .ToList();
        return (PayrollCutoffMode.Schedule, periods);
    }

    public async Task<(PayrollCutoffScheduleDto? Saved, Dictionary<string, string[]>? Errors)> SaveAsync(
        IReadOnlyList<Branch> shops,
        Guid branchId,
        UpdatePayrollCutoffScheduleRequest request,
        int year,
        int month,
        Guid? actorStaffId,
        CancellationToken cancellationToken)
    {
        if (shops.All(shop => shop.Id != branchId))
        {
            return (null, null);
        }

        var mode = (request.Mode ?? "").Trim().ToLowerInvariant();
        if (mode.Length == 0)
        {
            mode = PayrollCutoffMode.Free;
        }

        if (mode is not (PayrollCutoffMode.Free or PayrollCutoffMode.Schedule))
        {
            return (null, new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["mode"] = ["Mode must be free or schedule."]
            });
        }

        var rules = new List<PayrollCutoffRule>();
        if (mode == PayrollCutoffMode.Schedule)
        {
            var raw = (request.Cutoffs ?? [])
                .Select(row => new PayrollCutoffRule(row.Ordinal, row.StartDay, row.EndDay))
                .ToList();
            rules = PayrollCutoffCalendar.WithOrdinals(raw).ToList();
            if (!PayrollCutoffCalendar.TryValidate(rules, out var errors))
            {
                return (null, errors);
            }
        }

        var existing = await db.BranchPayrollCutoffs()
            .Where(row => row.BranchId == branchId)
            .OrderBy(row => row.Ordinal)
            .ToListAsync(cancellationToken);
        var before = PayrollCutoffCalendar.Describe(existing.Select(ToRule).ToList());
        var after = PayrollCutoffCalendar.Describe(rules);

        // Delete first, then insert. A single save can trip the (branch, ordinal) unique index
        // on providers that insert before they delete. A relational transaction keeps that pair atomic.
        async Task WriteAsync()
        {
            db.BranchPayrollCutoffs().RemoveRange(existing);
            if (existing.Count > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            foreach (var rule in rules)
            {
                db.BranchPayrollCutoffs().Add(new BranchPayrollCutoff
                {
                    Id = Guid.NewGuid(),
                    BranchId = branchId,
                    Ordinal = rule.Ordinal,
                    StartDay = rule.StartDay,
                    EndDay = rule.EndDay
                });
            }

            new SettingsChangeLogService(db).Record(
                actorStaffId,
                branchId,
                SettingsArea.PayrollCutoff,
                "schedule",
                before,
                after);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (db.Database.IsRelational())
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await WriteAsync();
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await WriteAsync();
        }

        return (ToDto(branchId, rules, year, month), null);
    }

    private static PayrollCutoffRule ToRule(BranchPayrollCutoff row) =>
        new(row.Ordinal, row.StartDay, row.EndDay);

    private static PayrollCutoffScheduleDto ToDto(
        Guid branchId,
        IReadOnlyList<PayrollCutoffRule> rules,
        int year,
        int month)
    {
        var mode = rules.Count == 0 ? PayrollCutoffMode.Free : PayrollCutoffMode.Schedule;
        var periods = mode == PayrollCutoffMode.Schedule
            ? PayrollCutoffCalendar.PeriodsForMonth(year, month, rules)
                .Select(period => new PayrollCutoffPeriodDto(period.Ordinal, period.From, period.To))
                .ToList()
            : [];
        return new PayrollCutoffScheduleDto(
            branchId,
            mode,
            rules.OrderBy(rule => rule.Ordinal)
                .Select(rule => new PayrollCutoffDto(rule.Ordinal, rule.StartDay, rule.EndDay))
                .ToList(),
            $"{year:0000}-{month:00}",
            periods);
    }
}
