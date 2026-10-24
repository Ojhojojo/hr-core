using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Payroll;

public sealed partial class PayrollService
{
    public async Task<ThirteenthMonthResponse> GetThirteenthMonthAsync(
        IReadOnlyList<Branch> shops,
        int year,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        var staffList = await ScopedStaffQuery(shops, includeInactive: false, includeUnscopedOwners)
            .OrderBy(s => s.Profile.DisplayName)
            .ToListAsync(cancellationToken);
        var autos = await AutoBasicsAsync(staffList, year, cancellationToken);
        var staffIds = staffList.Select(s => s.Id).ToList();
        var overrides = staffIds.Count == 0
            ? new Dictionary<Guid, PayrollThirteenthOverride>()
            : await db.PayrollThirteenthOverrides.AsNoTracking()
                .Where(o => o.Year == year && staffIds.Contains(o.StaffId))
                .ToDictionaryAsync(o => o.StaffId, cancellationToken);

        var rows = staffList
            .Select(staff => ToThirteenthRow(
                staff,
                autos[staff.Id],
                overrides.GetValueOrDefault(staff.Id)))
            .ToList();
        return new ThirteenthMonthResponse(year, rows, rows.Sum(r => r.ThirteenthMonth));
    }

    public async Task<ThirteenthMonthRowDto?> SaveThirteenthOverrideAsync(
        IReadOnlyList<Branch> shops,
        Guid staffId,
        int year,
        decimal basicEarnedOverride,
        Guid? updatedByStaffId,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        var staff = await FindStaffForThirteenthAsync(staffId, cancellationToken);
        if (staff is null || !IsInRegisterScope(staff, shops, includeUnscopedOwners))
        {
            return null;
        }

        var amount = RoundMoney(basicEarnedOverride);
        var existing = await db.PayrollThirteenthOverrides
            .FirstOrDefaultAsync(o => o.StaffId == staff.Id && o.Year == year, cancellationToken);
        if (existing is null)
        {
            existing = new PayrollThirteenthOverride
            {
                Id = Guid.NewGuid(),
                StaffId = staff.Id,
                Year = year
            };
            db.PayrollThirteenthOverrides.Add(existing);
        }

        existing.BasicEarnedOverride = amount;
        existing.UpdatedAtUtc = DateTime.UtcNow;
        existing.UpdatedByStaffId = updatedByStaffId;
        await db.SaveChangesAsync(cancellationToken);

        var auto = await AutoBasicForAsync(staff, year, cancellationToken);
        return ToThirteenthRow(staff, auto, existing);
    }

    public async Task<ThirteenthMonthRowDto?> ClearThirteenthOverrideAsync(
        IReadOnlyList<Branch> shops,
        Guid staffId,
        int year,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        var staff = await FindStaffForThirteenthAsync(staffId, cancellationToken);
        if (staff is null || !IsInRegisterScope(staff, shops, includeUnscopedOwners))
        {
            return null;
        }

        var existing = await db.PayrollThirteenthOverrides
            .FirstOrDefaultAsync(o => o.StaffId == staff.Id && o.Year == year, cancellationToken);
        if (existing is not null)
        {
            db.PayrollThirteenthOverrides.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
        }

        var auto = await AutoBasicForAsync(staff, year, cancellationToken);
        return ToThirteenthRow(staff, auto, null);
    }

    public async Task<PayrollCsvFile> BuildThirteenthCsvAsync(
        IReadOnlyList<Branch> shops,
        int year,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        var report = await GetThirteenthMonthAsync(shops, year, includeUnscopedOwners, cancellationToken);
        var lines = new List<string>
        {
            JoinCsv(
                "Name", "Role", "Branch", "Hire Date", "Basis", "Rate",
                "Basic Earned Auto", "Override", "Basic Earned", "13th Month")
        };

        foreach (var row in report.Rows)
        {
            lines.Add(JoinCsv(
                row.StaffName,
                row.Role.ToString(),
                row.Branch,
                row.HireDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                row.PayBasis?.ToString(),
                row.RatePhp?.ToString("0.00", CultureInfo.InvariantCulture),
                Money(row.BasicEarnedAuto),
                row.UsedOverride && row.BasicEarnedOverride is decimal ov ? Money(ov) : "",
                Money(row.BasicEarned),
                Money(row.ThirteenthMonth)));
        }

        var bytes = Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n");
        return new PayrollCsvFile($"rakkii-13th-month-{report.Year}.csv", bytes);
    }

    internal static bool TryResolveThirteenthYear(int? year, out int resolved, out string? error)
    {
        resolved = year ?? ManilaTime.Today().Year;
        if (resolved is < PayrollThirteenthOverride.MinYear or > PayrollThirteenthOverride.MaxYear)
        {
            error = $"Year must be from {PayrollThirteenthOverride.MinYear} through {PayrollThirteenthOverride.MaxYear}.";
            resolved = 0;
            return false;
        }

        error = null;
        return true;
    }

    internal static bool TryNormalizeOverride(decimal amount, out decimal rounded, out string? error)
    {
        if (amount < 0)
        {
            rounded = 0m;
            error = "Override amount cannot be negative.";
            return false;
        }

        rounded = RoundMoney(amount);
        if (rounded > PayrollThirteenthOverride.MaxAmount)
        {
            error = "Override amount is too large.";
            return false;
        }

        error = null;
        return true;
    }

    internal static decimal ThirteenthAmount(decimal basicEarned) =>
        RoundMoney(basicEarned / 12m);

    /// <summary>
    /// End of the employment window for a calendar year. Past years run through 31 Dec.
    /// The current year stops today in Asia/Manila. A future year has not started.
    /// </summary>
    internal static bool TryYearCap(int year, DateOnly today, out DateOnly yearStart, out DateOnly yearEndCap)
    {
        yearStart = new DateOnly(year, 1, 1);
        if (year < today.Year)
        {
            yearEndCap = new DateOnly(year, 12, 31);
            return true;
        }

        if (year == today.Year)
        {
            yearEndCap = today;
            return true;
        }

        yearEndCap = yearStart.AddDays(-1);
        return false;
    }

    internal static IEnumerable<(DateOnly Start, DateOnly End)> EnumerateMonthSlices(DateOnly start, DateOnly end)
    {
        if (start > end)
        {
            yield break;
        }

        var monthStart = new DateOnly(start.Year, start.Month, 1);
        while (monthStart <= end)
        {
            var days = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
            var monthEnd = new DateOnly(monthStart.Year, monthStart.Month, days);
            var sliceStart = start > monthStart ? start : monthStart;
            var sliceEnd = end < monthEnd ? end : monthEnd;
            yield return (sliceStart, sliceEnd);
            monthStart = monthEnd.AddDays(1);
        }
    }

    private async Task<decimal> AutoBasicForAsync(Domain.Staff staff, int year, CancellationToken cancellationToken)
    {
        var autos = await AutoBasicsAsync([staff], year, cancellationToken);
        return autos[staff.Id];
    }

    private async Task<Domain.Staff?> FindStaffForThirteenthAsync(Guid staffId, CancellationToken cancellationToken) =>
        await db.Staff.AsNoTracking()
            .Include(s => s.Profile)
            .Include(s => s.Branch)
            .FirstOrDefaultAsync(s => s.Id == staffId, cancellationToken);

    /// <param name="windowEnd">
    /// Last day to count instead of the usual year cap (final pay counts through the separation date).
    /// </param>
    private async Task<Dictionary<Guid, decimal>> AutoBasicsAsync(
        IReadOnlyList<Domain.Staff> staffList,
        int year,
        CancellationToken cancellationToken,
        DateOnly? windowEnd = null)
    {
        var totals = staffList.ToDictionary(s => s.Id, _ => 0m);
        DateOnly yearStart;
        DateOnly yearEndCap;
        if (windowEnd is DateOnly end)
        {
            yearStart = new DateOnly(year, 1, 1);
            yearEndCap = end;
        }
        else if (!TryYearCap(year, ManilaTime.Today(), out yearStart, out yearEndCap))
        {
            return totals;
        }

        if (staffList.Count == 0)
        {
            return totals;
        }

        var windows = new Dictionary<Guid, DateOnly>();
        DateOnly? earliest = null;
        foreach (var staff in staffList)
        {
            var start = staff.HireDate is DateOnly hired && hired > yearStart ? hired : yearStart;
            if (start > yearEndCap)
            {
                continue;
            }

            windows[staff.Id] = start;
            if (earliest is null || start < earliest)
            {
                earliest = start;
            }
        }

        if (windows.Count == 0 || earliest is null)
        {
            return totals;
        }

        var fromUtc = ManilaTime.StartOfDayUtc(earliest.Value);
        var toExclusiveUtc = ManilaTime.StartOfDayUtc(yearEndCap.AddDays(1));
        var staffIds = windows.Keys.ToList();
        var punches = await db.AttendancePunches.AsNoTracking()
            .Where(p => staffIds.Contains(p.StaffId) &&
                        p.OutAtUtc != null &&
                        p.ApprovalStatus == PunchApprovalStatus.Approved &&
                        p.InAtUtc >= fromUtc &&
                        p.InAtUtc < toExclusiveUtc)
            .ToListAsync(cancellationToken);
        var punchesByStaff = punches
            .GroupBy(p => p.StaffId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AttendancePunch>)g.ToList());

        var monthlyIds = staffList
            .Where(s => windows.ContainsKey(s.Id) && s.PayBasis == PayBasis.Monthly && s.RatePhp is not null)
            .Select(s => s.Id)
            .ToList();
        var leaves = monthlyIds.Count == 0
            ? []
            : await db.LeaveRequests.AsNoTracking()
                .Where(l => monthlyIds.Contains(l.StaffId) &&
                            l.Status == LeaveRequestStatus.Approved &&
                            l.LeaveType == LeaveType.Unpaid &&
                            l.StartDate <= yearEndCap &&
                            l.EndDate >= earliest.Value)
                .ToListAsync(cancellationToken);
        var leavesByStaff = leaves
            .GroupBy(l => l.StaffId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<LeaveRequest>)g.ToList());
        var windowedStaff = staffList.Where(s => windows.ContainsKey(s.Id)).ToList();
        var slicesByStaff = await RateSlicesByStaffAsync(
            windowedStaff,
            earliest.Value,
            yearEndCap,
            cancellationToken);
        // Paid leave is loaded once for the whole window. ComputePeriodGross only reads days inside each slice.
        var paidLeaveByStaff = await PayrollPaidLeave.LoadByStaffAsync(
            db,
            windowedStaff,
            earliest.Value,
            yearEndCap,
            cancellationToken);

        foreach (var staff in staffList)
        {
            if (!windows.TryGetValue(staff.Id, out var windowStart))
            {
                continue;
            }

            totals[staff.Id] = SumMonthSlices(
                staff,
                slicesByStaff.GetValueOrDefault(staff.Id, []),
                punchesByStaff.GetValueOrDefault(staff.Id, []),
                leavesByStaff.GetValueOrDefault(staff.Id, []),
                paidLeaveByStaff.GetValueOrDefault(staff.Id),
                windowStart,
                yearEndCap);
        }

        return totals;
    }

    private static decimal SumMonthSlices(
        Domain.Staff staff,
        IReadOnlyList<RateSlice> rateSlices,
        IReadOnlyList<AttendancePunch> punches,
        IReadOnlyList<LeaveRequest> leaves,
        IReadOnlyDictionary<DateOnly, decimal>? paidLeaveDays,
        DateOnly windowStart,
        DateOnly windowEnd)
    {
        decimal total = 0m;
        foreach (var (sliceStart, sliceEnd) in EnumerateMonthSlices(windowStart, windowEnd))
        {
            var fromUtc = ManilaTime.StartOfDayUtc(sliceStart);
            var toExclusiveUtc = ManilaTime.StartOfDayUtc(sliceEnd.AddDays(1));
            var slicePunches = new List<AttendancePunch>();
            foreach (var punch in punches)
            {
                if (punch.InAtUtc >= fromUtc && punch.InAtUtc < toExclusiveUtc)
                {
                    slicePunches.Add(punch);
                }
            }

            IReadOnlySet<DateOnly>? unpaid = null;
            if (staff.PayBasis == PayBasis.Monthly && leaves.Count > 0)
            {
                var indexed = IndexUnpaidLeaveDays(leaves, sliceStart, sliceEnd);
                if (indexed.TryGetValue(staff.Id, out var days))
                {
                    unpaid = days;
                }
            }

            total += ComputePeriodGross(
                rateSlices,
                slicePunches,
                unpaid,
                sliceStart,
                sliceEnd,
                paidLeaveDays: paidLeaveDays).Amount;
        }

        return total;
    }

    private static ThirteenthMonthRowDto ToThirteenthRow(
        Domain.Staff staff,
        decimal auto,
        PayrollThirteenthOverride? stored)
    {
        var used = stored is not null;
        var basic = used ? stored!.BasicEarnedOverride : auto;
        return new ThirteenthMonthRowDto(
            staff.Id,
            staff.Profile.DisplayName,
            staff.Role,
            staff.Branch?.Name,
            staff.HireDate,
            staff.PayBasis,
            staff.RatePhp,
            auto,
            used ? stored!.BasicEarnedOverride : null,
            basic,
            ThirteenthAmount(basic),
            used);
    }
}
