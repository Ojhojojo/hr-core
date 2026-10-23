using HrCore.Features.Attendance;
using HrCore.Features.HrChangeLog;
using HrCore.Features.Leave;
using HrCore.Features.Payroll;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Staff;

/// <summary>
/// Deactivation guard (5.4). A person with open work (see <see cref="IStaffWorkHooks"/>) cannot be deactivated.
/// When they can, deactivation also cancels their pending leave and closes an open punch, in the same
/// transaction as the flag change. Callers open the transaction and save.
/// </summary>
public static class StaffDeactivation
{
    public const string CloseReason = "Staff deactivated";

    /// <summary>
    /// Runs after the Active flag is set to false, before the save. Cancels pending leave (skipping any in
    /// a locked payroll period), closes an open punch at <paramref name="nowUtc"/> (skipping a punch whose
    /// days fall in a locked period), and reports upcoming approved leave so the desk can see it.
    /// </summary>
    public static async Task<DeactivationSummary> ApplyAsync(
        IHrDb db,
        Domain.Staff staff,
        Guid actorStaffId,
        HrChangeLogService hr,
        AttendanceService attendance,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var notes = new List<string>();

        var pending = await db.LeaveRequests
            .Where(r => r.StaffId == staff.Id && r.Status == LeaveRequestStatus.Pending)
            .OrderBy(r => r.StartDate)
            .ToListAsync(cancellationToken);
        var cancelled = 0;
        foreach (var row in pending)
        {
            if (await PayrollRunService.IsPeriodLockedAsync(db, staff.Id, row.StartDate, row.EndDate, cancellationToken))
            {
                notes.Add($"Pending {row.LeaveType} leave {row.StartDate:yyyy-MM-dd} to {row.EndDate:yyyy-MM-dd} is in a locked payroll period and was left pending.");
                continue;
            }

            LeaveEndpoints.ApplyCancellation(row, actorStaffId, CloseReason, hr, nowUtc);
            cancelled++;
        }

        var today = ManilaTime.Today();
        var upcoming = await db.LeaveRequests.AsNoTracking()
            .Where(r => r.StaffId == staff.Id
                && r.Status == LeaveRequestStatus.Approved
                && r.EndDate >= today)
            .OrderBy(r => r.StartDate)
            .Select(r => new DeactivationUpcomingLeave(r.Id, r.LeaveType, r.StartDate, r.EndDate, r.HalfDay))
            .ToListAsync(cancellationToken);

        var punch = await db.AttendancePunches
            .FirstOrDefaultAsync(p => p.StaffId == staff.Id && p.OutAtUtc == null, cancellationToken);
        var closedPunch = false;
        if (punch is not null)
        {
            var from = ManilaTime.ToManilaDate(punch.InAtUtc);
            var to = ManilaTime.ToManilaDate(nowUtc);
            if (await PayrollRunService.IsPeriodLockedAsync(db, staff.Id, from, to, cancellationToken))
            {
                notes.Add($"The open punch from {from:yyyy-MM-dd} is in a locked payroll period and was left open.");
            }
            else
            {
                await attendance.ApplyCloseAsync(punch, nowUtc, CloseReason, cancellationToken);
                closedPunch = true;
            }
        }

        return new DeactivationSummary(cancelled, closedPunch, upcoming, notes);
    }
}

public sealed record DeactivationSummary(
    int CancelledPendingLeave,
    bool ClosedOpenPunch,
    IReadOnlyList<DeactivationUpcomingLeave> UpcomingApprovedLeave,
    IReadOnlyList<string> Notes);

public sealed record DeactivationUpcomingLeave(
    Guid Id,
    LeaveType LeaveType,
    DateOnly StartDate,
    DateOnly EndDate,
    LeaveHalfDay? HalfDay);
