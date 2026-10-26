using System.Globalization;
using System.Text;
using HrCore.Features.HrChangeLog;
using HrCore.Features.Staff;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Payroll;

public sealed partial class PayrollService(IHrDb db)
{
    public async Task<PayrollRegisterResponse> GetRegisterAsync(
        IReadOnlyList<Branch> shops,
        DateOnly? from,
        DateOnly? to,
        bool includeInactive,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        var (start, end, fromUtc, toExclusiveUtc) = ResolvePeriod(from, to);

        // A locked run for exactly this shop and period wins: show the frozen snapshot.
        var lockedRun = await PayrollRunService.FindExactLockedRunAsync(
            db, shops, start, end, cancellationToken);
        if (lockedRun is not null)
        {
            return await BuildLockedRegisterAsync((PayrollRun)lockedRun, start, end, cancellationToken);
        }

        var staffList = await ScopedStaffQuery(shops, includeInactive, includeUnscopedOwners)
            .OrderBy(s => s.Profile.DisplayName)
            .ToListAsync(cancellationToken);
        var staffIds = staffList.Select(s => s.Id).ToList();

        var closedPunches = staffIds.Count == 0
            ? []
            : await db.AttendancePunches.AsNoTracking()
                .Where(p => staffIds.Contains(p.StaffId) &&
                            p.OutAtUtc != null &&
                            p.ApprovalStatus == PunchApprovalStatus.Approved &&
                            p.InAtUtc >= fromUtc &&
                            p.InAtUtc < toExclusiveUtc)
                .ToListAsync(cancellationToken);

        var punchesByStaff = closedPunches
            .GroupBy(p => p.StaffId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var overtimeByStaff = staffIds.Count == 0
            ? new Dictionary<Guid, List<OvertimeEntry>>()
            : (await db.OvertimeEntries.AsNoTracking()
                .Where(entry => staffIds.Contains(entry.StaffId)
                    && entry.Status == OvertimeStatus.Approved
                    && entry.WorkDate >= start
                    && entry.WorkDate <= end)
                .ToListAsync(cancellationToken))
                .GroupBy(entry => entry.StaffId)
                .ToDictionary(group => group.Key, group => group.ToList());

        var pendingOvertimeClaimsCount = staffIds.Count == 0
            ? 0
            : await db.OvertimeEntries.AsNoTracking()
                .Where(entry => staffIds.Contains(entry.StaffId)
                    && entry.Status == OvertimeStatus.Pending
                    && entry.WorkDate >= start
                    && entry.WorkDate <= end)
                .CountAsync(cancellationToken);

        var deductionsByStaff = staffIds.Count == 0
            ? new Dictionary<Guid, PayrollDeduction>()
            : await db.PayrollDeductions.AsNoTracking()
                .Where(d => staffIds.Contains(d.StaffId) && d.PeriodFrom == start && d.PeriodTo == end)
                .ToDictionaryAsync(d => d.StaffId, cancellationToken);

        var monthlyStaffIds = staffList
            .Where(s => s.PayBasis == PayBasis.Monthly)
            .Select(s => s.Id)
            .ToList();
        var unpaidLeaves = monthlyStaffIds.Count == 0
            ? []
            : await db.LeaveRequests.AsNoTracking()
                .Where(l => monthlyStaffIds.Contains(l.StaffId) &&
                            l.Status == LeaveRequestStatus.Approved &&
                            l.LeaveType == LeaveType.Unpaid &&
                            l.StartDate <= end &&
                            l.EndDate >= start)
                .ToListAsync(cancellationToken);
        var unpaidDaysByStaff = IndexUnpaidLeaveDays(unpaidLeaves, start, end);
        var paidLeaveByStaff = await PayrollPaidLeave.LoadByStaffAsync(db, staffList, start, end, cancellationToken);
        var rateSlicesByStaff = await RateSlicesByStaffAsync(staffList, start, end, cancellationToken);
        var premiums = await LoadPremiumContextAsync(shops, staffList, start, end, cancellationToken);
        var allowanceContext = await LoadAllowanceContextAsync(staffIds, start, end, cancellationToken);
        var loanContext = await LoadLoanContextAsync(staffIds, start, end, cancellationToken);
        var dayPayOverridesByStaff = await LoadDayPayOverridesByStaffAsync(staffIds, start, end, cancellationToken);

        var rows = new List<PayrollStaffRowDto>();

        foreach (var staff in staffList)
        {
            var staffPunches = punchesByStaff.GetValueOrDefault(staff.Id, []);
            unpaidDaysByStaff.TryGetValue(staff.Id, out var unpaidDays);
            var slices = rateSlicesByStaff.GetValueOrDefault(staff.Id, []);
            var shiftEnd = staff.Branch?.ShiftEnd ?? Branch.DefaultShiftEnd;
            var graceMinutes = staff.Branch?.OtGraceMinutes ?? Branch.DefaultOtGraceMinutes;
            paidLeaveByStaff.TryGetValue(staff.Id, out var paidLeaveDays);
            dayPayOverridesByStaff.TryGetValue(staff.Id, out var staffDayOverrides);
            var dayOverrideSpecs = ToDayPayOverrideSpecs(staffDayOverrides);
            var gross = ComputePeriodGross(
                slices,
                staffPunches,
                unpaidDays,
                start,
                end,
                shiftEnd,
                graceMinutes,
                paidLeaveDays,
                dayOverrideSpecs);
            var overtime = PriceStaffOvertime(staff, staffPunches, slices, overtimeByStaff, shiftEnd, graceMinutes);
            var premium = PriceStaffPremium(staff, staffPunches, slices, premiums);
            var allowance = PriceStaffAllowances(
                staff,
                AllowanceDaysWorked(staff, staffPunches, start, end, unpaidDays, paidLeaveDays, staffDayOverrides),
                start,
                end,
                allowanceContext);
            var amount = gross.Amount + overtime.Pay + premium.AddedToGross + allowance.AddedToGross;
            var notClockingWarning = NotClockingPaidWarning(
                staff,
                staffPunches,
                paidLeaveDays is { Count: > 0 },
                staffDayOverrides is { Count: > 0 });

            var hasRate = staff.RatePhp is not null && staff.PayBasis is not null;
            string? notes = hasRate ? null : "No rate";

            var deduction = deductionsByStaff.GetValueOrDefault(staff.Id);
            var capped = PayrollDeductionCap.Cap(
                new PayrollDeductionCap.Shares(
                    deduction?.Sss ?? 0m,
                    deduction?.PhilHealth ?? 0m,
                    deduction?.PagIbig ?? 0m,
                    deduction?.Wht ?? 0m,
                    deduction?.Other ?? 0m),
                amount);
            var sss = capped.Shares.Sss;
            var philHealth = capped.Shares.PhilHealth;
            var pagIbig = capped.Shares.PagIbig;
            var wht = capped.Shares.Wht;
            var other = capped.Shares.Other;
            var statutory = capped.Shares.Total;
            var loan = PriceStaffLoans(staff.Id, amount - statutory, start, end, loanContext);
            var deductionSum = statutory + (loan.LoanDeductions ?? 0m);
            var net = amount - deductionSum;
            if (net < 0m)
            {
                net = 0m;
            }

            rows.Add(new PayrollStaffRowDto(
                StaffId: staff.Id,
                StaffName: staff.Profile.DisplayName,
                Role: staff.Role,
                BranchId: staff.BranchId,
                BranchName: staff.Branch?.Name,
                BranchKind: staff.Branch?.Kind,
                Basis: staff.PayBasis,
                RatePhp: staff.RatePhp,
                OtAfterHoursPerDay: staff.OtAfterHoursPerDay,
                OtMultiplier: staff.OtMultiplier,
                RegularHours: gross.RegularHours,
                OtHours: overtime.Hours,
                OtPay: overtime.Pay,
                DaysPaid: gross.DaysPaid,
                Amount: amount,
                LeaveDaysPaid: gross.LeaveDaysPaid,
                Notes: notes,
                Active: staff.Active,
                Sss: sss,
                PhilHealth: philHealth,
                PagIbig: pagIbig,
                Wht: wht,
                Other: other,
                Deductions: deductionSum,
                Net: net,
                RateSlices: ToRateSliceDtos(slices),
                HolidayHours: premium.HolidayHours,
                RestDayHours: premium.RestDayHours,
                NightHours: premium.NightHours,
                PremiumPay: premium.PremiumPay,
                PremiumOverridden: premium.PremiumOverridden,
                Warnings: MergeWarnings(
                    premium.Warnings,
                    allowance.Warnings,
                    loan.Warnings,
                    capped.Warning is null ? [] : [capped.Warning],
                    notClockingWarning is null ? [] : [notClockingWarning]),
                Allowances: allowance.Allowances,
                AllowanceItems: allowance.Items,
                AllowancesOverridden: allowance.Overridden,
                LoanDeductions: loan.LoanDeductions,
                LoanItems: loan.Items,
                DayPayOverrides: staffDayOverrides?.Select(ToDayPayOverrideDto).ToList()));
        }

        var grandTotal = rows.Sum(r => r.Amount);
        var totalNet = rows.Sum(r => r.Net);
        return new PayrollRegisterResponse(
            start,
            end,
            grandTotal,
            rows,
            totalNet,
            PendingOvertimeClaimsCount: pendingOvertimeClaimsCount,
            UnpaidBonuses: await LoadUnpaidBonusesAsync(staffIds, cancellationToken));
    }

    /// <summary>
    /// Frozen register for a locked run. Money and hours come from the PayrollRunLine snapshot;
    /// role and branch labels are current. Rate slices are not stored on a locked run.
    /// </summary>
    private async Task<PayrollRegisterResponse> BuildLockedRegisterAsync(
        PayrollRun run,
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken)
    {
        var lines = await db.PayrollRunLines.AsNoTracking()
            .Where(line => line.RunId == run.Id)
            .ToListAsync(cancellationToken);
        var staffIds = lines.Select(line => line.StaffId).ToList();
        var lineIds = lines.Select(line => line.Id).ToList();
        var allowanceItems = await LoadLockedAllowanceItemsAsync(db, lineIds, cancellationToken);
        var loanItems = await LoadLockedLoanItemsAsync(db, lineIds, cancellationToken);
        var overrideRows = staffIds.Count == 0
            ? []
            : await db.PayrollAmountOverrides.AsNoTracking()
                .Where(row => staffIds.Contains(row.StaffId)
                    && row.PeriodFrom == start
                    && row.PeriodTo == end
                    && (row.Key == PayrollPremiumCalculator.OverrideKey
                        || row.Key == PayrollAllowanceCalculator.OverrideKey))
                .Select(row => new { row.StaffId, row.Key })
                .ToListAsync(cancellationToken);
        var overridden = overrideRows
            .Where(row => row.Key == PayrollPremiumCalculator.OverrideKey)
            .Select(row => row.StaffId)
            .ToHashSet();
        var allowancesOverridden = overrideRows
            .Where(row => row.Key == PayrollAllowanceCalculator.OverrideKey)
            .Select(row => row.StaffId)
            .ToHashSet();
        var staffById = staffIds.Count == 0
            ? new Dictionary<Guid, Domain.Staff>()
            : await db.Staff.AsNoTracking()
                .Include(s => s.Profile)
                .Include(s => s.Branch)
                .Where(s => staffIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, cancellationToken);
        var enteredDeductions = staffIds.Count == 0
            ? new Dictionary<Guid, PayrollDeduction>()
            : await db.PayrollDeductions.AsNoTracking()
                .Where(d => staffIds.Contains(d.StaffId) && d.PeriodFrom == start && d.PeriodTo == end)
                .ToDictionaryAsync(d => d.StaffId, cancellationToken);

        var rows = lines
            .OrderBy(line => line.StaffName)
            .Select(line =>
            {
                staffById.TryGetValue(line.StaffId, out var staff);
                allowanceItems.TryGetValue(line.Id, out var items);
                loanItems.TryGetValue(line.Id, out var loans);
                IReadOnlyList<string>? warnings = null;
                if (enteredDeductions.TryGetValue(line.StaffId, out var entered))
                {
                    var enteredSum = entered.Sss + entered.PhilHealth + entered.PagIbig + entered.Wht + entered.Other;
                    var shown = line.Sss + line.PhilHealth + line.PagIbig + line.Wht + line.Other;
                    if (enteredSum > line.Gross && shown <= line.Gross)
                    {
                        warnings = [PayrollDeductionCap.Warning(shown)];
                    }
                }

                return new PayrollStaffRowDto(
                    StaffId: line.StaffId,
                    StaffName: line.StaffName,
                    Role: staff?.Role ?? HrRoles.UnknownPersonRole(),
                    BranchId: line.BranchId,
                    BranchName: staff?.Branch?.Name ?? run.Branch?.Name,
                    BranchKind: staff?.Branch?.Kind,
                    Basis: line.Basis,
                    RatePhp: line.RatePhp,
                    OtAfterHoursPerDay: null,
                    OtMultiplier: null,
                    RegularHours: line.RegularHours,
                    OtHours: line.OtHours,
                    DaysPaid: line.DaysPaid,
                    Amount: line.Gross,
                    Notes: line.Notes,
                    Active: staff?.Active ?? true,
                    Sss: line.Sss,
                    PhilHealth: line.PhilHealth,
                    PagIbig: line.PagIbig,
                    Wht: line.Wht,
                    Other: line.Other,
                    Deductions: line.Deductions,
                    Net: line.Net,
                    HolidayHours: line.HolidayHours,
                    RestDayHours: line.RestDayHours,
                    NightHours: line.NightHours,
                    PremiumPay: line.PremiumPay,
                    PremiumOverridden: overridden.Contains(line.StaffId),
                    Warnings: warnings,
                    Allowances: line.Allowances,
                    AllowanceItems: items is { Count: > 0 } ? items : null,
                    AllowancesOverridden: allowancesOverridden.Contains(line.StaffId),
                    LoanDeductions: line.LoanDeductions,
                    LoanItems: loans is { Count: > 0 } ? loans : null,
                    OtPay: line.OtPay ?? 0m);
            })
            .ToList();

        var lockedBy = run.LockedByStaff?.Profile.DisplayName;
        return new PayrollRegisterResponse(
            start,
            end,
            rows.Sum(row => row.Amount),
            rows,
            rows.Sum(row => row.Net),
            "locked",
            run.Id,
            run.LockedAtUtc,
            lockedBy,
            UnpaidBonuses: await LoadUnpaidBonusesAsync(staffIds, cancellationToken));
    }

    public async Task<PayrollDeductionSaveResult> SaveDeductionsAsync(
        IReadOnlyList<Branch> shops,
        SavePayrollDeductionsRequest request,
        bool includeUnscopedOwners,
        Guid? actorStaffId,
        CancellationToken cancellationToken)
    {
        var (start, end, _, _) = ResolvePeriod(request.From, request.To);
        var sss = RoundMoney(request.Sss);
        var philHealth = RoundMoney(request.PhilHealth);
        var pagIbig = RoundMoney(request.PagIbig);
        var wht = RoundMoney(request.Wht);
        var other = RoundMoney(request.Other);

        var staff = await db.Staff.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.StaffId, cancellationToken);
        if (staff is null || !IsInRegisterScope(staff, shops, includeUnscopedOwners))
        {
            return PayrollDeductionSaveResult.NotFound();
        }

        if (await PayrollRunService.IsPeriodLockedAsync(db, staff.Id, start, end, cancellationToken))
        {
            return PayrollDeductionSaveResult.Locked(PayrollRunService.LockedPeriodMessage);
        }

        var existing = await db.PayrollDeductions
            .FirstOrDefaultAsync(
                d => d.StaffId == staff.Id && d.PeriodFrom == start && d.PeriodTo == end,
                cancellationToken);
        var beforeSss = existing?.Sss ?? 0m;
        var beforePhilHealth = existing?.PhilHealth ?? 0m;
        var beforePagIbig = existing?.PagIbig ?? 0m;
        var beforeWht = existing?.Wht ?? 0m;
        var beforeOther = existing?.Other ?? 0m;
        if (existing is null)
        {
            existing = new PayrollDeduction
            {
                Id = Guid.NewGuid(),
                StaffId = staff.Id,
                PeriodFrom = start,
                PeriodTo = end
            };
            db.PayrollDeductions.Add(existing);
        }

        existing.Sss = sss;
        existing.PhilHealth = philHealth;
        existing.PagIbig = pagIbig;
        existing.Wht = wht;
        existing.Other = other;

        var hr = new HrChangeLogService(db);
        hr.RecordMoney(actorStaffId, staff.Id, HrChangeField.PayrollDeductionSss, beforeSss, sss, start, end);
        hr.RecordMoney(
            actorStaffId, staff.Id, HrChangeField.PayrollDeductionPhilHealth, beforePhilHealth, philHealth, start, end);
        hr.RecordMoney(
            actorStaffId, staff.Id, HrChangeField.PayrollDeductionPagIbig, beforePagIbig, pagIbig, start, end);
        hr.RecordMoney(actorStaffId, staff.Id, HrChangeField.PayrollDeductionWht, beforeWht, wht, start, end);
        hr.RecordMoney(actorStaffId, staff.Id, HrChangeField.PayrollDeductionOther, beforeOther, other, start, end);
        await db.SaveChangesAsync(cancellationToken);

        var sum = sss + philHealth + pagIbig + wht + other;
        return PayrollDeductionSaveResult.Ok(
            new PayrollDeductionDto(staff.Id, start, end, sss, philHealth, pagIbig, wht, other, sum));
    }

    public async Task<PayrollCsvFile> BuildRegisterCsvAsync(
        IReadOnlyList<Branch> shops,
        DateOnly? from,
        DateOnly? to,
        bool includeInactive,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        var register = await GetRegisterAsync(
            shops, from, to, includeInactive, includeUnscopedOwners, cancellationToken);

        var lines = new List<string>
        {
            JoinCsv(
                "Name", "Role", "Branch", "Kind", "Basis", "Rate", "Regular Hours", "OT Hours", "OT Pay", "Days Paid",
                "Amount", "Notes", "SSS", "PhilHealth", "Pag-IBIG", "WHT", "Other", "Deductions", "Net",
                "Holiday Hours", "Rest Day Hours", "Night Hours", "Premium Pay", "Allowances", "Loan Deductions")
        };

        foreach (var row in register.Rows)
        {
            lines.Add(JoinCsv(
                row.StaffName,
                row.Role.ToString(),
                row.BranchName,
                row.BranchKind == BranchKind.Franchise ? "Franchise" : (row.BranchKind == BranchKind.Owned ? "Owned" : ""),
                row.Basis?.ToString(),
                row.RatePhp?.ToString("0.00", CultureInfo.InvariantCulture),
                row.RegularHours.ToString("0.00", CultureInfo.InvariantCulture),
                row.OtHours.ToString("0.00", CultureInfo.InvariantCulture),
                row.OtPay.ToString("0.00", CultureInfo.InvariantCulture),
                row.DaysPaid.ToString(CultureInfo.InvariantCulture),
                row.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                row.Notes,
                Money(row.Sss),
                Money(row.PhilHealth),
                Money(row.PagIbig),
                Money(row.Wht),
                Money(row.Other),
                Money(row.Deductions),
                Money(row.Net),
                CsvOptionalMoney(row.HolidayHours),
                CsvOptionalMoney(row.RestDayHours),
                CsvOptionalMoney(row.NightHours),
                CsvOptionalMoney(row.PremiumPay),
                CsvOptionalMoney(row.Allowances),
                CsvOptionalMoney(row.LoanDeductions)));
        }

        var bytes = Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n");
        var fileName = $"rakkii-payroll-{register.From:yyyy-MM-dd}-{register.To:yyyy-MM-dd}.csv";
        return new PayrollCsvFile(fileName, bytes);
    }

    /// <summary>
    /// <c>LeaveDaysPaid</c> is the paid-leave days inside the gross (0.5 steps). It is kept apart from
    /// <c>DaysPaid</c>, which stays a whole-day count of punched days.
    /// </summary>
    internal readonly record struct PeriodGross(
        decimal RegularHours,
        decimal OtHours,
        decimal DaysPaid,
        decimal Amount,
        decimal LeaveDaysPaid = 0m);

    private static (decimal Hours, decimal Pay) PriceStaffOvertime(
        Domain.Staff staff,
        IReadOnlyList<AttendancePunch> punches,
        IReadOnlyList<RateSlice> slices,
        IReadOnlyDictionary<Guid, List<OvertimeEntry>> overtimeByStaff,
        TimeOnly shiftEnd,
        int graceMinutes)
    {
        var approved = overtimeByStaff.GetValueOrDefault(staff.Id) ?? [];
        var punchedByDay = new Dictionary<DateOnly, decimal>();
        foreach (var punch in punches)
        {
            if (punch.OutAtUtc is not DateTime outAt || punch.ApprovalStatus != PunchApprovalStatus.Approved)
            {
                continue;
            }

            var day = ManilaTime.ToManilaDate(punch.InAtUtc);
            var claimed = OvertimeMath.SplitPunch(punch.InAtUtc, outAt, shiftEnd, graceMinutes).ClaimedHours;
            punchedByDay[day] = punchedByDay.GetValueOrDefault(day) + claimed;
        }

        var approvedByDay = new Dictionary<DateOnly, decimal>();
        foreach (var entry in approved)
        {
            approvedByDay[entry.WorkDate] = approvedByDay.GetValueOrDefault(entry.WorkDate)
                + (entry.ApprovedHours ?? entry.ClaimedHours);
        }

        var days = new HashSet<DateOnly>(punchedByDay.Keys);
        foreach (var day in approvedByDay.Keys)
        {
            days.Add(day);
        }

        var divisor = staff.Branch is { OtMonthlyDayDivisor: > 0m } branch
            ? branch.OtMonthlyDayDivisor
            : Branch.DefaultOtMonthlyDayDivisor;
        var premium = staff.Branch?.OtPremiumPercent ?? Branch.DefaultOtPremiumPercent;
        var inputs = days.Select(day =>
        {
            var slice = SliceFor(slices, day);
            return new OvertimeMath.DayPayInput(
                punchedByDay.GetValueOrDefault(day),
                approvedByDay.GetValueOrDefault(day),
                slice?.Basis,
                slice?.RatePhp);
        });
        return OvertimeMath.SumDays(inputs, divisor, premium);
    }

    /// <summary>
    /// Warns when a daily or hourly person is not set to clock and the period has no closed punches,
    /// paid leave or day overrides, so their pay is zero. The pay rule itself is unchanged.
    /// </summary>
    internal static string? NotClockingPaidWarning(
        Domain.Staff staff,
        IReadOnlyCollection<AttendancePunch> closedPunches,
        bool hasPaidLeave,
        bool hasDayOverrides)
    {
        if (staff.MustClock || staff.PayBasis is not (PayBasis.Daily or PayBasis.Hourly))
        {
            return null;
        }

        return closedPunches.Count == 0 && !hasPaidLeave && !hasDayOverrides
            ? "Not set to clock: paid only for day overrides"
            : null;
    }

    /// <summary>
    /// Gross for one register period from a single basis/rate snapshot. Kept for the tests and for
    /// callers that have no dated rate history; the register uses the rate-slice overload below.
    /// </summary>
    internal static PeriodGross ComputePeriodGross(
        PayBasis? basis,
        decimal? rate,
        decimal? otAfterHoursPerDay,
        decimal? otMultiplier,
        IReadOnlyList<AttendancePunch> staffPunches,
        IReadOnlySet<DateOnly>? unpaidDays,
        DateOnly start,
        DateOnly end,
        TimeOnly? shiftEnd = null,
        int? graceMinutes = null) =>
        ComputePeriodGross(
            [new RateSlice(start, end, basis, rate, otAfterHoursPerDay, otMultiplier)],
            staffPunches,
            unpaidDays,
            start,
            end,
            shiftEnd,
            graceMinutes);

    /// <summary>#390: one day's pay override applied inside period gross.</summary>
    internal readonly record struct DayPayOverrideSpec(PayrollDayPayMode Mode, decimal? CustomAmount);

    /// <summary>
    /// Gross for one register period priced by dated rate slices. Hourly and daily days are priced
    /// with the slice effective that day; monthly days split each month slice by days at each rate
    /// and round every piece AwayFromZero. Slices must cover [start, end].
    /// </summary>
    internal static PeriodGross ComputePeriodGross(
        IReadOnlyList<RateSlice> slices,
        IReadOnlyList<AttendancePunch> staffPunches,
        IReadOnlySet<DateOnly>? unpaidDays,
        DateOnly start,
        DateOnly end,
        TimeOnly? shiftEnd = null,
        int? graceMinutes = null,
        IReadOnlyDictionary<DateOnly, decimal>? paidLeaveDays = null,
        IReadOnlyDictionary<DateOnly, DayPayOverrideSpec>? dayPayOverrides = null)
    {
        if (start > end)
        {
            (start, end) = (end, start);
        }

        var shift = shiftEnd ?? Branch.DefaultShiftEnd;
        var grace = graceMinutes ?? Branch.DefaultOtGraceMinutes;

        var punchesByDay = staffPunches
            .Where(p => p.ApprovalStatus == PunchApprovalStatus.Approved)
            .GroupBy(p => ManilaTime.ToManilaDate(p.InAtUtc))
            .OrderBy(g => g.Key);

        decimal regularHoursTotal = 0m;
        var daysPaid = 0m;
        decimal totalAmount = 0m;
        decimal leaveDaysPaid = 0m;
        var closedDays = new HashSet<DateOnly>();
        var overriddenDays = new HashSet<DateOnly>();

        foreach (var dayGroup in punchesByDay)
        {
            var day = dayGroup.Key;
            var slice = SliceFor(slices, day);
            decimal regular = 0m;
            var closed = false;
            foreach (var punch in dayGroup)
            {
                if (punch.OutAtUtc is not DateTime outAt)
                {
                    continue;
                }

                closed = true;
                regular += OvertimeMath.SplitPunch(punch.InAtUtc, outAt, shift, grace).RegularHours;
            }

            if (closed)
            {
                closedDays.Add(day);
            }

            regularHoursTotal += regular;
            if (dayPayOverrides is not null
                && dayPayOverrides.TryGetValue(day, out var dayOverride)
                && slice is not null
                && closed)
            {
                var normal = NormalDayPay(slice, day, regular);
                var (pay, days) = ApplyDayPayOverride(dayOverride, normal);
                totalAmount += pay;
                daysPaid += days;
                overriddenDays.Add(day);
                continue;
            }

            if (slice?.Basis == PayBasis.Hourly)
            {
                if (slice.RatePhp is decimal hourlyRate && regular > 0m)
                {
                    totalAmount += Math.Round(regular * hourlyRate, 2, MidpointRounding.AwayFromZero);
                }
            }
            else if (slice?.Basis == PayBasis.Daily && closed)
            {
                daysPaid++;
                if (slice.RatePhp is decimal dailyRate)
                {
                    totalAmount += dailyRate;
                }
            }
        }

        // Day overrides with no closed punch still apply (Owner correcting a missing/short day).
        if (dayPayOverrides is not null)
        {
            foreach (var (day, dayOverride) in dayPayOverrides.OrderBy(entry => entry.Key))
            {
                if (day < start || day > end || overriddenDays.Contains(day) || closedDays.Contains(day))
                {
                    continue;
                }

                var slice = SliceFor(slices, day);
                if (slice is null)
                {
                    continue;
                }

                var normal = NormalDayPay(slice, day, regularHours: 0m);
                var (pay, days) = ApplyDayPayOverride(dayOverride, normal);
                totalAmount += pay;
                daysPaid += days;
                overriddenDays.Add(day);
            }
        }

        // Paid leave pays only where no closed punch exists that day, so a day is never paid twice.
        // Monthly days are not touched here: they are already paid through the monthly slice.
        // A day-pay override wins over leave for that date.
        if (paidLeaveDays is not null)
        {
            foreach (var (day, factor) in paidLeaveDays.OrderBy(entry => entry.Key))
            {
                if (day < start || day > end || closedDays.Contains(day) || overriddenDays.Contains(day))
                {
                    continue;
                }

                var slice = SliceFor(slices, day);
                if (slice is null || slice.RatePhp is not decimal rate)
                {
                    continue;
                }

                if (slice.Basis == PayBasis.Daily)
                {
                    totalAmount += Math.Round(rate * factor, 2, MidpointRounding.AwayFromZero);
                }
                else if (slice.Basis == PayBasis.Hourly)
                {
                    totalAmount += Math.Round(
                        PayrollPaidLeave.HoursPerLeaveDay * rate * factor,
                        2,
                        MidpointRounding.AwayFromZero);
                }
                else
                {
                    continue;
                }

                leaveDaysPaid += factor;
            }
        }

        foreach (var slice in slices)
        {
            if (slice.Basis != PayBasis.Monthly)
            {
                continue;
            }

            var (monthlyGross, monthlyPaidDays) = ComputeMonthlySliceGross(slice, unpaidDays, overriddenDays, start, end);
            totalAmount += monthlyGross;
            daysPaid += monthlyPaidDays;
        }

        return new PeriodGross(regularHoursTotal, 0m, daysPaid, totalAmount, leaveDaysPaid);
    }

    private static RateSlice? SliceFor(IReadOnlyList<RateSlice> slices, DateOnly day)
    {
        foreach (var slice in slices)
        {
            if (day >= slice.From && day <= slice.To)
            {
                return slice;
            }
        }

        return null;
    }

    /// <summary>
    /// Maps stored day-pay rows onto the spec dictionary <see cref="ComputePeriodGross"/> takes.
    /// Null when the staff has no override rows.
    /// </summary>
    internal static Dictionary<DateOnly, DayPayOverrideSpec>? ToDayPayOverrideSpecs(IEnumerable<PayrollDayPayOverride>? rows) =>
        rows is null
            ? null
            : rows.ToDictionary(
                row => row.WorkDate,
                row => new DayPayOverrideSpec(row.Mode, row.CustomAmount));

    /// <summary>
    /// Normal pay for one day before a #390 override: daily rate, or 8× hourly, or monthly rate / days in
    /// that day's calendar month (the same share the monthly slice pays per day).
    /// </summary>
    private static decimal NormalDayPay(RateSlice slice, DateOnly day, decimal regularHours)
    {
        if (slice.RatePhp is not decimal rate)
        {
            return 0m;
        }

        return slice.Basis switch
        {
            PayBasis.Daily => rate,
            PayBasis.Hourly => Math.Round(
                (regularHours > 0m ? regularHours : PayrollPaidLeave.HoursPerLeaveDay) * rate,
                2,
                MidpointRounding.AwayFromZero),
            PayBasis.Monthly => Math.Round(
                rate / DateTime.DaysInMonth(day.Year, day.Month),
                2,
                MidpointRounding.AwayFromZero),
            _ => 0m
        };
    }

    /// <summary>Maps Full/Half/None/Custom onto (pay amount, days-paid credit).</summary>
    internal static (decimal Pay, decimal Days) ApplyDayPayOverride(DayPayOverrideSpec ov, decimal normalPay)
    {
        return ov.Mode switch
        {
            PayrollDayPayMode.Full => (normalPay, 1m),
            PayrollDayPayMode.Half => (
                Math.Round(normalPay * 0.5m, 2, MidpointRounding.AwayFromZero),
                0.5m),
            PayrollDayPayMode.None => (0m, 0m),
            PayrollDayPayMode.Custom => (
                Math.Round(Math.Clamp(ov.CustomAmount ?? 0m, 0m, Math.Max(normalPay, 0m)), 2, MidpointRounding.AwayFromZero),
                ov.CustomAmount is > 0m ? 1m : 0m),
            _ => (normalPay, 1m)
        };
    }

    /// <summary>
    /// One rate slice of a monthly gross. Each calendar-month piece is rounded AwayFromZero,
    /// exactly like the single-rate Mode 4 rule. Unpaid days and day-pay-overridden days are both
    /// excluded from the monthly share (a day counted in both is excluded once); overridden days are
    /// paid by their override in <see cref="ComputePeriodGross"/>.
    /// </summary>
    private static (decimal Gross, int PaidDays) ComputeMonthlySliceGross(
        RateSlice slice,
        IReadOnlySet<DateOnly>? unpaidDays,
        IReadOnlySet<DateOnly>? overriddenDays,
        DateOnly start,
        DateOnly end)
    {
        var from = slice.From > start ? slice.From : start;
        var to = slice.To < end ? slice.To : end;
        if (from > to)
        {
            return (0m, 0);
        }

        decimal gross = 0m;
        var paidDays = 0;
        var monthCursor = new DateOnly(from.Year, from.Month, 1);
        while (monthCursor <= to)
        {
            var daysInMonth = DateTime.DaysInMonth(monthCursor.Year, monthCursor.Month);
            var monthStart = new DateOnly(monthCursor.Year, monthCursor.Month, 1);
            var monthEnd = new DateOnly(monthCursor.Year, monthCursor.Month, daysInMonth);
            var sliceStart = from > monthStart ? from : monthStart;
            var sliceEnd = to < monthEnd ? to : monthEnd;
            var calendarDays = sliceEnd.DayNumber - sliceStart.DayNumber + 1;
            var excludedInSlice = 0;
            if (unpaidDays is not null)
            {
                foreach (var day in unpaidDays)
                {
                    if (day >= sliceStart && day <= sliceEnd)
                    {
                        excludedInSlice++;
                    }
                }
            }

            if (overriddenDays is not null)
            {
                foreach (var day in overriddenDays)
                {
                    if (day >= sliceStart && day <= sliceEnd && (unpaidDays is null || !unpaidDays.Contains(day)))
                    {
                        excludedInSlice++;
                    }
                }
            }

            var slicePaid = Math.Max(0, calendarDays - excludedInSlice);
            paidDays += slicePaid;
            if (slice.RatePhp is decimal monthlyRate)
            {
                gross += Math.Round(monthlyRate * slicePaid / daysInMonth, 2, MidpointRounding.AwayFromZero);
            }

            monthCursor = monthEnd.AddDays(1);
        }

        return (gross, paidDays);
    }

    /// <summary>
    /// Mode 4 thin rule. Each calendar-month slice is rounded AwayFromZero to 2 decimals, then summed.
    /// Unpaid days are distinct dates already clipped to the period. Paid days cannot go below zero.
    /// </summary>
    internal static (decimal Gross, int PaidDays) ComputeMonthlyGross(
        decimal ratePhp,
        DateOnly start,
        DateOnly end,
        IReadOnlySet<DateOnly>? unpaidDays)
    {
        if (start > end)
        {
            (start, end) = (end, start);
        }

        decimal gross = 0m;
        var paidDays = 0;
        var monthCursor = new DateOnly(start.Year, start.Month, 1);
        while (monthCursor <= end)
        {
            var daysInMonth = DateTime.DaysInMonth(monthCursor.Year, monthCursor.Month);
            var monthStart = new DateOnly(monthCursor.Year, monthCursor.Month, 1);
            var monthEnd = new DateOnly(monthCursor.Year, monthCursor.Month, daysInMonth);
            var sliceStart = start > monthStart ? start : monthStart;
            var sliceEnd = end < monthEnd ? end : monthEnd;
            var calendarDays = sliceEnd.DayNumber - sliceStart.DayNumber + 1;
            var unpaidInSlice = 0;
            if (unpaidDays is not null)
            {
                foreach (var day in unpaidDays)
                {
                    if (day >= sliceStart && day <= sliceEnd)
                    {
                        unpaidInSlice++;
                    }
                }
            }

            var slicePaid = Math.Max(0, calendarDays - unpaidInSlice);
            paidDays += slicePaid;
            gross += Math.Round(ratePhp * slicePaid / daysInMonth, 2, MidpointRounding.AwayFromZero);
            monthCursor = monthEnd.AddDays(1);
        }

        return (gross, paidDays);
    }

    internal static Dictionary<Guid, HashSet<DateOnly>> IndexUnpaidLeaveDays(
        IEnumerable<LeaveRequest> leaves,
        DateOnly start,
        DateOnly end)
    {
        var map = new Dictionary<Guid, HashSet<DateOnly>>();
        foreach (var leave in leaves)
        {
            if (leave.LeaveType != LeaveType.Unpaid || leave.Status != LeaveRequestStatus.Approved)
            {
                continue;
            }

            if (!map.TryGetValue(leave.StaffId, out var days))
            {
                days = [];
                map[leave.StaffId] = days;
            }

            var from = leave.StartDate > start ? leave.StartDate : start;
            var to = leave.EndDate < end ? leave.EndDate : end;
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                days.Add(day);
            }
        }

        return map;
    }

    internal static (DateOnly Start, DateOnly End, DateTime FromUtc, DateTime ToExclusiveUtc) ResolvePeriod(
        DateOnly? from,
        DateOnly? to)
    {
        var (weekFrom, weekTo) = ManilaTime.CurrentWeek();
        var start = from ?? weekFrom;
        var end = to ?? weekTo;
        if (start > end)
        {
            (start, end) = (end, start);
        }

        return (start, end, ManilaTime.StartOfDayUtc(start), ManilaTime.StartOfDayUtc(end.AddDays(1)));
    }

    private IQueryable<Domain.Staff> ScopedStaffQuery(
        IReadOnlyList<Branch> shops,
        bool includeInactive,
        bool includeUnscopedOwners)
    {
        var shopIds = shops.Select(s => s.Id).ToList();
        var staffQuery = db.Staff.AsNoTracking()
            .Include(s => s.Profile)
            .Include(s => s.Branch)
            .AsQueryable();

        if (!includeInactive)
        {
            staffQuery = staffQuery.Where(s => s.Active);
        }

        return includeUnscopedOwners
            ? staffQuery.Where(s =>
                (s.BranchId != null && shopIds.Contains(s.BranchId.Value)) ||
                (s.BranchId == null && s.Role == StaffRole.Owner))
            : staffQuery.Where(s => s.BranchId != null && shopIds.Contains(s.BranchId.Value));
    }

    internal static bool IsInRegisterScope(
        HrCore.Domain.Staff staff,
        IReadOnlyList<Branch> shops,
        bool includeUnscopedOwners)
    {
        if (staff.BranchId is Guid branchId && shops.Any(s => s.Id == branchId))
        {
            return true;
        }

        return includeUnscopedOwners && staff.BranchId is null && staff.Role == StaffRole.Owner;
    }

    /// <summary>
    /// Dated rate slices for every worker in <paramref name="staffList"/> across [start, end].
    /// One query for the whole list so the register and the 13th-month year sum stay cheap.
    /// </summary>
    internal async Task<Dictionary<Guid, IReadOnlyList<RateSlice>>> RateSlicesByStaffAsync(
        IReadOnlyList<Domain.Staff> staffList,
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken)
    {
        var staffIds = staffList.Select(s => s.Id).ToList();
        var rows = staffIds.Count == 0
            ? []
            : await db.StaffPayRates.AsNoTracking()
                .Where(r => staffIds.Contains(r.StaffId))
                .OrderBy(r => r.EffectiveFrom)
                .ToListAsync(cancellationToken);
        var rowsByStaff = rows
            .GroupBy(r => r.StaffId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<StaffPayRate>)g.ToList());

        return staffList.ToDictionary(
            s => s.Id,
            s => PayrollRateService.BuildSlices(s, rowsByStaff.GetValueOrDefault(s.Id, []), start, end));
    }

    /// <summary>
    /// Hourly defaults OT to 8 hours a day at 1.00x, monthly clears both, and daily keeps whatever
    /// was sent. Same rule the staff screens use.
    /// </summary>
    internal static (decimal? OtAfterHoursPerDay, decimal? OtMultiplier) NormaliseOt(
        PayBasis basis,
        decimal? otAfterHoursPerDay,
        decimal? otMultiplier)
    {
        if (basis == PayBasis.Hourly)
        {
            return (otAfterHoursPerDay ?? 8m, otMultiplier ?? 1.00m);
        }

        if (basis == PayBasis.Monthly)
        {
            return (null, null);
        }

        return (otAfterHoursPerDay, otMultiplier);
    }

    /// <summary>Rate slices for the row payload. Null when one rate covers the whole period.</summary>
    internal static IReadOnlyList<PayrollRateSliceDto>? ToRateSliceDtos(IReadOnlyList<RateSlice> slices)
    {
        var priced = slices
            .Where(slice => slice.Basis is not null && slice.RatePhp is not null)
            .Select(slice => new PayrollRateSliceDto(
                slice.From,
                slice.To,
                slice.Basis!.Value,
                slice.RatePhp!.Value,
                slice.OtAfterHoursPerDay,
                slice.OtMultiplier))
            .ToList();

        return priced.Count > 1 ? priced : null;
    }

    internal static decimal RoundMoney(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    internal static string Money(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    internal static string JoinCsv(params string?[] cells) =>
        string.Join(",", cells.Select(CsvEscape));

    internal static string CsvEscape(string? value)
    {
        var text = value ?? "";
        if (text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r'))
        {
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        return text;
    }
}
