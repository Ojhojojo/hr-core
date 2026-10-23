using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Leave;

/// <summary>
/// One staff member's rest days and the shop holidays that apply to them (their branch's, plus
/// the all-shop ones), loaded for a date range. Turns leave dates into a working-day count.
/// </summary>
public sealed class LeaveDayContext
{
    public required IReadOnlySet<DayOfWeek> RestDays { get; init; }
    public required IReadOnlySet<DateOnly> Holidays { get; init; }

    public decimal Count(DateOnly start, DateOnly end, LeaveHalfDay? halfDay = null) =>
        LeaveDays.Count(start, end, halfDay, RestDays, Holidays);

    /// <summary>
    /// Loads the staff member's RestDays and ShopHolidays where BranchId is their branch or null,
    /// for dates in [from, to]. An unknown staff id gives an empty context (no rest days, no holidays).
    /// </summary>
    public static async Task<LeaveDayContext> LoadAsync(
        IHrDb db,
        Guid staffId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var staff = await db.Staff.AsNoTracking()
            .Where(s => s.Id == staffId)
            .Select(s => new { s.BranchId, s.RestDays })
            .FirstOrDefaultAsync(cancellationToken);

        if (staff is null)
        {
            return new LeaveDayContext
            {
                RestDays = RestDayList.Parse(null),
                Holidays = new HashSet<DateOnly>()
            };
        }

        var branchId = staff.BranchId;
        var holidays = await db.ShopHolidays.AsNoTracking()
            .Where(h => h.Date >= from && h.Date <= to
                && (h.BranchId == null || h.BranchId == branchId))
            .Select(h => h.Date)
            .ToListAsync(cancellationToken);

        return new LeaveDayContext
        {
            RestDays = RestDayList.Parse(staff.RestDays),
            Holidays = holidays.ToHashSet()
        };
    }
}
