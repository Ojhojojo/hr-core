using System.Globalization;
using HrCore.Features.SettingsLog;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Payroll;

public sealed partial class PayrollService
{
    public const string PayFromRequiredMessage =
        "Set the first day this system should pay before this shop's first payroll run.";

    /// <summary>
    /// The period a new run uses when the operator leaves the dates alone.
    /// To stays on the usual week. From moves up to <paramref name="payFrom"/> when the
    /// week would start earlier. A pay-from date after this week uses that date's week.
    /// </summary>
    public static (DateOnly From, DateOnly To) DefaultPeriod(
        DateOnly today,
        DateOnly? payFrom,
        DayOfWeek weekStart = DayOfWeek.Monday)
    {
        var from = ManilaTime.StartOfWeek(today, weekStart);
        var to = ManilaTime.EndOfWeek(today, weekStart);
        if (payFrom is not DateOnly first || from >= first)
        {
            return (from, to);
        }

        from = first;
        if (from > to)
        {
            to = ManilaTime.EndOfWeek(first, weekStart);
        }

        return (from, to);
    }

    public async Task<PayFromDateDto?> GetPayFromAsync(
        IReadOnlyList<Branch> allowed,
        Guid branchId,
        CancellationToken cancellationToken)
    {
        var branch = allowed.FirstOrDefault(shop => shop.Id == branchId);
        if (branch is null)
        {
            return null;
        }

        return await ToPayFromDtoAsync(branch.Id, branch.PayFromDate, cancellationToken);
    }

    public async Task<(PayFromDateDto? Saved, string? Error)> SetPayFromAsync(
        IReadOnlyList<Branch> allowed,
        Guid branchId,
        DateOnly payFromDate,
        Guid? actorStaffId,
        CancellationToken cancellationToken)
    {
        if (allowed.All(shop => shop.Id != branchId))
        {
            return (null, null);
        }

        if (payFromDate.Year is < 2000 or > 2100)
        {
            return (null, "Enter the first day this system should pay.");
        }

        var branch = await db.Branches.FirstOrDefaultAsync(row => row.Id == branchId, cancellationToken);
        if (branch is null)
        {
            return (null, null);
        }

        var before = branch.PayFromDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        branch.PayFromDate = payFromDate;
        var after = payFromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        new SettingsChangeLogService(db).Record(
            actorStaffId,
            branch.Id,
            SettingsArea.ShopDesk,
            "pay_from_date",
            before,
            after);
        await db.SaveChangesAsync(cancellationToken);
        return (await ToPayFromDtoAsync(branch.Id, branch.PayFromDate, cancellationToken), null);
    }

    /// <summary>
    /// The first lock on a shop needs a pay-from date. Later locks, and any shop that already
    /// has a run, are unchanged. Returns the message to show, or null when the lock may proceed.
    /// </summary>
    public async Task<string?> FirstRunPayFromBlockAsync(Guid branchId, CancellationToken cancellationToken)
    {
        var payFrom = await db.Branches.AsNoTracking()
            .Where(row => row.Id == branchId)
            .Select(row => row.PayFromDate)
            .FirstOrDefaultAsync(cancellationToken);
        if (payFrom is not null)
        {
            return null;
        }

        var hasRun = await db.PayrollRuns.AsNoTracking()
            .AnyAsync(run => run.BranchId == branchId, cancellationToken);
        return hasRun ? null : PayFromRequiredMessage;
    }

    private async Task<PayFromDateDto> ToPayFromDtoAsync(
        Guid branchId,
        DateOnly? payFromDate,
        CancellationToken cancellationToken)
    {
        var hasRun = await db.PayrollRuns.AsNoTracking()
            .AnyAsync(run => run.BranchId == branchId, cancellationToken);
        return new PayFromDateDto(payFromDate, hasRun);
    }
}
