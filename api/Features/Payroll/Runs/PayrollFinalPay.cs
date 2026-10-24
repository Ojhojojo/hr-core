using HrCore.Features.Leave;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Payroll;

/// <summary>
/// Final pay estimate for a separated worker: wages since the last locked period, a pro-rated
/// 13th month, and cash-convertible service incentive leave. Estimate only — the owner verifies
/// and pays outside the app, the same honest pattern as 13th-month lite.
/// </summary>
public sealed partial class PayrollService
{
    public async Task<(PayrollFinalPayDto? Result, string? Error)> GetFinalPayAsync(
        IReadOnlyList<Branch> shops,
        Guid staffId,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        var staff = await db.Staff.AsNoTracking()
            .Include(s => s.Profile)
            .Include(s => s.Branch)
            .FirstOrDefaultAsync(s => s.Id == staffId, cancellationToken);
        if (staff is null || !IsInRegisterScope(staff, shops, includeUnscopedOwners))
        {
            return (null, null);
        }

        if (staff.SeparationDate is not DateOnly separation)
        {
            return (null, "Set the separation date in the directory first.");
        }

        var payslipShops = staff.Branch is { } branch ? [branch] : shops;

        // Unpaid wages: the day after the last locked period on that shop (or the later of the
        // hire date and the first of the separation month) to the separation date.
        var from = await FinalPayWagesFromAsync(staff, separation, cancellationToken);
        if (from > separation)
        {
            from = separation;
        }

        var wagesRegister = await GetRegisterAsync(
            payslipShops, from, separation, true, includeUnscopedOwners, cancellationToken);
        var wagesRow = wagesRegister.Rows.FirstOrDefault(r => r.StaffId == staff.Id);

        // 13th month: the separation year's basic earned through the separation date, the same
        // basic pay the 13th-month page counts (overtime and premiums excluded). An owner override
        // for that year wins, as it does on the 13th-month page.
        var thirteenthOverride = await db.PayrollThirteenthOverrides.AsNoTracking()
            .FirstOrDefaultAsync(o => o.StaffId == staff.Id && o.Year == separation.Year, cancellationToken);
        var basicEarned = thirteenthOverride?.BasicEarnedOverride
            ?? (await AutoBasicsAsync([staff], separation.Year, cancellationToken, separation))[staff.Id];

        var thirteenthMonth = ThirteenthAmount(basicEarned);
        var sil = await FinalPaySilAsync(staff, separation, cancellationToken);
        var wagesGross = wagesRow?.Amount ?? 0m;
        var grossTotal = RoundMoney(wagesGross + thirteenthMonth + sil.Amount);

        return (new PayrollFinalPayDto(
            staff.Id,
            staff.Profile.DisplayName,
            separation,
            staff.SeparationReason,
            new PayrollFinalPayWages(from, separation, wagesGross),
            new PayrollFinalPayThirteenth(separation.Year, RoundMoney(basicEarned), thirteenthMonth),
            sil,
            grossTotal,
            [
                "Estimate — owner verify. Deductions are typed by the owner when paying.",
                "Wages start the day after the last locked payroll period on this shop. With nothing locked, they start at the later of the hire date and the first of the separation month.",
                thirteenthOverride is null
                    ? "13th month is the separation year's basic pay earned through the separation date (overtime excluded), divided by 12."
                    : "13th month uses the owner's basic earned override for this year, divided by 12.",
                "SIL encashes at the daily rate equivalent: daily as stored, hourly x 8, monthly x 12 / 313."
            ]), null);
    }

    private async Task<DateOnly> FinalPayWagesFromAsync(
        Domain.Staff staff,
        DateOnly separation,
        CancellationToken cancellationToken)
    {
        if (staff.BranchId is Guid branchId)
        {
            var lastLockedTo = await db.PayrollRuns.AsNoTracking()
                .Where(run => run.BranchId == branchId
                    && run.Status == PayrollRunStatus.Locked
                    && run.PeriodTo < separation)
                .OrderByDescending(run => run.PeriodTo)
                .Select(run => (DateOnly?)run.PeriodTo)
                .FirstOrDefaultAsync(cancellationToken);
            if (lastLockedTo is DateOnly lockedTo)
            {
                return lockedTo.AddDays(1);
            }
        }

        var monthStart = new DateOnly(separation.Year, separation.Month, 1);
        return staff.HireDate is DateOnly hired && hired > monthStart ? hired : monthStart;
    }

    private async Task<PayrollFinalPaySil> FinalPaySilAsync(
        Domain.Staff staff,
        DateOnly separation,
        CancellationToken cancellationToken)
    {
        // SIL is a legal entitlement after one year of service; before that there is nothing to encash.
        if (staff.HireDate is not DateOnly hired || hired.AddYears(1) > separation)
        {
            return new PayrollFinalPaySil(false, 0m, 0m, 0m, 0m, 0m, 0m);
        }

        var allotted = await db.LeaveAllotments.AsNoTracking()
            .Where(a => a.StaffId == staff.Id
                && a.Year == separation.Year
                && a.LeaveType == LeaveType.ServiceIncentive)
            .Select(a => (decimal?)a.Days)
            .FirstOrDefaultAsync(cancellationToken) ?? 0m;

        var requests = await db.LeaveRequests.AsNoTracking()
            .Where(l => l.StaffId == staff.Id
                && l.LeaveType == LeaveType.ServiceIncentive
                && l.StartDate.Year == separation.Year)
            .ToListAsync(cancellationToken);
        var calendarFrom = requests.Count == 0 ? separation : requests.Min(r => r.StartDate);
        var calendarTo = requests.Count == 0 ? separation : requests.Max(r => r.EndDate);
        var calendar = await LeaveDayContext.LoadAsync(db, staff.Id, calendarFrom, calendarTo, cancellationToken);
        var used = WorkingDays(calendar, requests.Where(l => l.Status == LeaveRequestStatus.Approved));
        var pending = WorkingDays(calendar, requests.Where(l => l.Status == LeaveRequestStatus.Pending));
        var remaining = Math.Max(0m, allotted - used - pending);

        var dailyRate = staff.PayBasis switch
        {
            PayBasis.Daily => staff.RatePhp ?? 0m,
            PayBasis.Hourly => (staff.RatePhp ?? 0m) * 8m,
            PayBasis.Monthly => staff.RatePhp is { } rate ? RoundMoney(rate * 12m / 313m) : 0m,
            _ => 0m
        };

        return new PayrollFinalPaySil(
            true, allotted, used, pending, remaining, dailyRate, RoundMoney(remaining * dailyRate));
    }

    /// <summary>Leave days counted as working days: rest days and shop holidays do not use up SIL.</summary>
    private static decimal WorkingDays(LeaveDayContext calendar, IEnumerable<LeaveRequest> requests) =>
        requests.Sum(request => calendar.Count(request.StartDate, request.EndDate, request.HalfDay));
}
