using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Payroll;

/// <summary>
/// Approved paid leave (Vacation, Sick, ServiceIncentive) as per-date pay factors for the register.
/// A working day counts 1 and a half-day leave counts 0.5. Rest days and shop holidays are never
/// paid as leave. Unpaid leave is handled separately by <see cref="PayrollService.IndexUnpaidLeaveDays"/>.
/// </summary>
internal static class PayrollPaidLeave
{
    /// <summary>Hours in one leave day for hourly staff. Same 8-hour day as <see cref="OvertimeMath.HourlyEquivalent"/>.</summary>
    public const decimal HoursPerLeaveDay = 8m;

    /// <summary>
    /// Pay factor per date for each staff member with paid leave in [start, end]. A date covered by
    /// two requests keeps the larger factor, so it is never paid twice.
    /// </summary>
    public static async Task<Dictionary<Guid, Dictionary<DateOnly, decimal>>> LoadByStaffAsync(
        IHrDb db,
        IReadOnlyList<Domain.Staff> staff,
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, Dictionary<DateOnly, decimal>>();
        if (staff.Count == 0)
        {
            return result;
        }

        var staffIds = staff.Select(s => s.Id).ToList();
        var leaves = await db.LeaveRequests.AsNoTracking()
            .Where(l => staffIds.Contains(l.StaffId)
                && l.Status == LeaveRequestStatus.Approved
                && (l.LeaveType == LeaveType.Vacation
                    || l.LeaveType == LeaveType.Sick
                    || l.LeaveType == LeaveType.ServiceIncentive)
                && l.StartDate <= end
                && l.EndDate >= start)
            .ToListAsync(cancellationToken);
        if (leaves.Count == 0)
        {
            return result;
        }

        var holidays = await db.ShopHolidays.AsNoTracking()
            .Where(h => h.Date >= start && h.Date <= end)
            .Select(h => new { h.Date, h.BranchId })
            .ToListAsync(cancellationToken);

        foreach (var member in staff)
        {
            var memberLeaves = leaves.Where(l => l.StaffId == member.Id).ToList();
            if (memberLeaves.Count == 0)
            {
                continue;
            }

            var restDays = RestDayList.Parse(member.RestDays);
            var memberHolidays = holidays
                .Where(h => h.BranchId is null || h.BranchId == member.BranchId)
                .Select(h => h.Date)
                .ToHashSet();

            var days = new Dictionary<DateOnly, decimal>();
            foreach (var leave in memberLeaves)
            {
                var from = leave.StartDate > start ? leave.StartDate : start;
                var to = leave.EndDate < end ? leave.EndDate : end;
                var factor = leave.HalfDay is null ? 1m : LeaveDays.HalfDayAmount;
                foreach (var date in LeaveDays.PaidDates(from, to, restDays, memberHolidays))
                {
                    days[date] = Math.Max(days.GetValueOrDefault(date), factor);
                }
            }

            if (days.Count > 0)
            {
                result[member.Id] = days;
            }
        }

        return result;
    }
}
