using HrCore.Features.HrChangeLog;
using HrCore.Features.SettingsLog;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Payroll;

public sealed partial class PayrollService
{
    private sealed class PremiumContext
    {
        public required IReadOnlyList<ShopHoliday> Holidays { get; init; }
        public required Dictionary<Guid, PayrollAmountOverride> Overrides { get; init; }
        public required Dictionary<Guid, HashSet<DateOnly>> PunchDatesByStaff { get; init; }
        public required IReadOnlyList<Domain.Staff> Staff { get; init; }
        public Branch? FallbackShop { get; init; }
    }

    private async Task<PremiumContext> LoadPremiumContextAsync(
        IReadOnlyList<Branch> shops,
        IReadOnlyList<Domain.Staff> staffList,
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken)
    {
        var shopIds = shops.Select(shop => shop.Id).ToList();
        var staffIds = staffList.Select(staff => staff.Id).ToList();
        var holidays = shopIds.Count == 0
            ? []
            : await db.ShopHolidays.AsNoTracking()
                .Where(holiday => holiday.Date >= start && holiday.Date <= end
                    && (holiday.BranchId == null || shopIds.Contains(holiday.BranchId.Value)))
                .ToListAsync(cancellationToken);

        var overrides = staffIds.Count == 0
            ? new Dictionary<Guid, PayrollAmountOverride>()
            : await db.PayrollAmountOverrides.AsNoTracking()
                .Where(row => staffIds.Contains(row.StaffId)
                    && row.PeriodFrom == start
                    && row.PeriodTo == end
                    && row.Key == PayrollPremiumCalculator.OverrideKey)
                .ToDictionaryAsync(row => row.StaffId, cancellationToken);

        var punchDatesByStaff = new Dictionary<Guid, HashSet<DateOnly>>();
        if (staffIds.Count > 0)
        {
            var (fromUtc, toExclusiveUtc) = (ManilaTime.StartOfDayUtc(start), ManilaTime.StartOfDayUtc(end.AddDays(1)));
            var punches = await db.AttendancePunches.AsNoTracking()
                .Where(punch => staffIds.Contains(punch.StaffId)
                    && punch.ApprovalStatus == PunchApprovalStatus.Approved
                    && punch.InAtUtc >= fromUtc
                    && punch.InAtUtc < toExclusiveUtc)
                .Select(punch => new { punch.StaffId, punch.InAtUtc })
                .ToListAsync(cancellationToken);
            foreach (var punch in punches)
            {
                if (!punchDatesByStaff.TryGetValue(punch.StaffId, out var dates))
                {
                    dates = [];
                    punchDatesByStaff[punch.StaffId] = dates;
                }

                dates.Add(ManilaTime.ToManilaDate(punch.InAtUtc));
            }
        }

        return new PremiumContext
        {
            Holidays = holidays,
            Overrides = overrides,
            PunchDatesByStaff = punchDatesByStaff,
            Staff = staffList,
            FallbackShop = shops.Count == 1 ? shops[0] : null
        };
    }

    private static StaffPremium PriceStaffPremium(
        Domain.Staff staff,
        IReadOnlyList<AttendancePunch> closedPunches,
        IReadOnlyList<RateSlice> slices,
        PremiumContext context)
    {
        var settings = (staff.Branch ?? context.FallbackShop) is { } branch
            ? PayrollPremiumCalculator.Settings.FromBranch(branch)
            : PayrollPremiumCalculator.Settings.Defaults;
        var holidays = HolidaysFor(context.Holidays, staff.BranchId ?? context.FallbackShop?.Id);
        var names = HolidayNamesFor(context.Holidays, staff.BranchId ?? context.FallbackShop?.Id);
        var restDays = RestDayList.Parse(staff.RestDays);
        var computed = PremiumForSlices(slices, closedPunches, holidays, restDays, settings);
        var hasOverride = context.Overrides.TryGetValue(staff.Id, out var premiumOverride);
        var shopHasRest = context.Staff.Any(other =>
            other.BranchId == staff.BranchId && RestDayList.HasAny(other.RestDays));
        var configured = settings.NightDiffEnabled || holidays.Count > 0 || shopHasRest || hasOverride;

        decimal added = hasOverride ? premiumOverride!.Amount : computed.PremiumPay;
        decimal? premiumPay = !configured && added == 0m ? null : added;
        decimal? Hours(decimal hours) => premiumPay is null ? null : hours;

        var warnings = new List<string>();
        var hourlyOrDaily = staff.PayBasis is PayBasis.Hourly or PayBasis.Daily;
        if (hourlyOrDaily
            && restDays.Count == 0
            && settings.RestDayPremiumEnabled
            && (holidays.Count > 0 || shopHasRest))
        {
            warnings.Add("No rest day set");
        }

        if (hourlyOrDaily)
        {
            foreach (var holiday in holidays
                .Where(pair => pair.Value == HolidayKind.Regular)
                .OrderBy(pair => pair.Key))
            {
                var dates = context.PunchDatesByStaff.GetValueOrDefault(staff.Id);
                if (dates is null || !dates.Contains(holiday.Key))
                {
                    var name = names.GetValueOrDefault(holiday.Key, "Holiday");
                    warnings.Add(
                        $"Regular holiday {name} {holiday.Key:yyyy-MM-dd}: no punches — check holiday pay");
                }
            }
        }

        return new StaffPremium(
            Hours(computed.HolidayHours),
            Hours(computed.RestDayHours),
            Hours(computed.NightHours),
            premiumPay,
            hasOverride,
            premiumPay ?? 0m,
            warnings);
    }

    private static PayrollPremiumCalculator.Result PremiumForSlices(
        IReadOnlyList<RateSlice> slices,
        IReadOnlyList<AttendancePunch> closedPunches,
        IReadOnlyDictionary<DateOnly, HolidayKind> holidays,
        IReadOnlySet<DayOfWeek> restDays,
        PayrollPremiumCalculator.Settings settings)
    {
        var result = PayrollPremiumCalculator.Result.Zero;
        var groups = closedPunches
            .Select(punch => (Punch: punch, Day: ManilaTime.ToManilaDate(punch.InAtUtc)))
            .GroupBy(row => SliceFor(slices, row.Day));
        foreach (var group in groups)
        {
            if (group.Key is not RateSlice slice)
            {
                continue;
            }

            var punches = group
                .Where(row => row.Punch.OutAtUtc is not null
                    && row.Punch.ApprovalStatus == PunchApprovalStatus.Approved)
                .Select(row => (row.Punch.InAtUtc, row.Punch.OutAtUtc!.Value))
                .ToList();
            result = result.Add(PayrollPremiumCalculator.Compute(
                slice.Basis,
                slice.RatePhp ?? 0m,
                punches,
                holidays,
                restDays,
                settings));
        }

        return result;
    }

    private static Dictionary<DateOnly, HolidayKind> HolidaysFor(
        IReadOnlyList<ShopHoliday> holidays,
        Guid? branchId)
    {
        var map = new Dictionary<DateOnly, HolidayKind>();
        foreach (var holiday in holidays.Where(holiday => holiday.BranchId is null))
        {
            map[holiday.Date] = holiday.Kind;
        }

        if (branchId is Guid id)
        {
            foreach (var holiday in holidays.Where(holiday => holiday.BranchId == id))
            {
                map[holiday.Date] = holiday.Kind;
            }
        }

        return map;
    }

    private static Dictionary<DateOnly, string> HolidayNamesFor(
        IReadOnlyList<ShopHoliday> holidays,
        Guid? branchId)
    {
        var map = new Dictionary<DateOnly, string>();
        foreach (var holiday in holidays.Where(holiday => holiday.BranchId is null))
        {
            map[holiday.Date] = holiday.Name;
        }

        if (branchId is Guid id)
        {
            foreach (var holiday in holidays.Where(holiday => holiday.BranchId == id))
            {
                map[holiday.Date] = holiday.Name;
            }
        }

        return map;
    }

    private readonly record struct StaffPremium(
        decimal? HolidayHours,
        decimal? RestDayHours,
        decimal? NightHours,
        decimal? PremiumPay,
        bool PremiumOverridden,
        decimal AddedToGross,
        IReadOnlyList<string> Warnings);

    public async Task<PayrollPremiumSettingsDto?> GetPremiumSettingsAsync(
        IReadOnlyList<Branch> shops,
        Guid branchId,
        CancellationToken cancellationToken)
    {
        if (shops.All(shop => shop.Id != branchId))
        {
            return null;
        }

        var branch = await db.Branches.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == branchId, cancellationToken);
        return branch is null ? null : ToSettings(branch);
    }

    public async Task<(PayrollPremiumSettingsDto? Saved, string? Error)> SavePremiumSettingsAsync(
        IReadOnlyList<Branch> shops,
        Guid branchId,
        UpdatePayrollPremiumSettingsRequest request,
        Guid? actorStaffId,
        CancellationToken cancellationToken)
    {
        if (shops.All(shop => shop.Id != branchId))
        {
            return (null, null);
        }

        var branch = await db.Branches.FirstOrDefaultAsync(row => row.Id == branchId, cancellationToken);
        if (branch is null)
        {
            return (null, null);
        }

        var before = PremiumLogValues(branch);
        try
        {
            branch.PayrollRegularHolidayPercent = PremiumPercent(
                request.RegularHolidayPercent, "Regular holiday percent");
            branch.PayrollSpecialDayPercent = PremiumPercent(
                request.SpecialDayPercent, "Special day percent");
            branch.PayrollRestDayPercent = PremiumPercent(
                request.RestDayPercent, "Rest day percent");
            branch.PayrollNightDiffPercent = PremiumPercent(
                request.NightDiffPercent, "Night differential percent");
        }
        catch (InvalidOperationException ex)
        {
            return (null, ex.Message);
        }

        branch.PayrollHolidayPremiumEnabled = request.HolidayPremiumEnabled;
        branch.PayrollRestDayPremiumEnabled = request.RestDayPremiumEnabled;
        branch.PayrollNightDiffEnabled = request.NightDiffEnabled;
        branch.PayrollNightStart = request.NightStart;
        branch.PayrollNightEnd = request.NightEnd;
        // Same rule as the shop desk: a window that starts and ends at the same time is not a window.
        if (branch.PayrollNightStart == branch.PayrollNightEnd)
        {
            return (null, "Night differential start and end must be different.");
        }
        if (request.ShiftEnd is TimeOnly shiftEnd)
        {
            branch.ShiftEnd = shiftEnd;
        }

        if (request.AdminsCanApproveOt is bool adminsCanApprove)
        {
            branch.AdminsCanApproveOt = adminsCanApprove;
        }

        try
        {
            if (request.OtGraceMinutes is int grace)
            {
                if (grace < 0 || grace > Branch.MaxOtGraceMinutes)
                {
                    return (null, $"Overtime grace must be from 0 through {Branch.MaxOtGraceMinutes} minutes.");
                }

                branch.OtGraceMinutes = grace;
            }

            if (request.OtPremiumPercent is decimal premium)
            {
                branch.OtPremiumPercent = PremiumPercent(
                    premium, "Overtime premium percent");
            }

            if (request.OtMonthlyDayDivisor is decimal divisor)
            {
                if (divisor <= 0m || divisor > 1000m)
                {
                    return (null, "Monthly day divisor must be greater than 0 and at most 1000.");
                }

                branch.OtMonthlyDayDivisor = divisor;
            }
        }
        catch (InvalidOperationException ex)
        {
            return (null, ex.Message);
        }

        var changes = new SettingsChangeLogService(db);
        changes.RecordChanges(
            actorStaffId,
            branch.Id,
            SettingsArea.ShopDesk,
            SettingsChangeLogService.Diff(before, PremiumLogValues(branch)));
        await db.SaveChangesAsync(cancellationToken);
        return (ToSettings(branch), null);
    }

    /// <summary>The premium fields under the same keys the shop desk logs, so both write paths share one trail.</summary>
    private static List<(string Field, string? Value)> PremiumLogValues(Branch branch) =>
    [
        ("payroll_regular_holiday_percent", SettingsChangeLogService.Show(branch.PayrollRegularHolidayPercent)),
        ("payroll_special_day_percent", SettingsChangeLogService.Show(branch.PayrollSpecialDayPercent)),
        ("payroll_rest_day_percent", SettingsChangeLogService.Show(branch.PayrollRestDayPercent)),
        ("payroll_holiday_premium_enabled", SettingsChangeLogService.Show(branch.PayrollHolidayPremiumEnabled)),
        ("payroll_rest_day_premium_enabled", SettingsChangeLogService.Show(branch.PayrollRestDayPremiumEnabled)),
        ("payroll_night_diff_enabled", SettingsChangeLogService.Show(branch.PayrollNightDiffEnabled)),
        ("payroll_night_diff_percent", SettingsChangeLogService.Show(branch.PayrollNightDiffPercent)),
        ("payroll_night_start", SettingsChangeLogService.Show(branch.PayrollNightStart)),
        ("payroll_night_end", SettingsChangeLogService.Show(branch.PayrollNightEnd)),
    ];

    private static PayrollPremiumSettingsDto ToSettings(Branch branch) => new(
        branch.Id,
        branch.PayrollRegularHolidayPercent,
        branch.PayrollSpecialDayPercent,
        branch.PayrollRestDayPercent,
        branch.PayrollHolidayPremiumEnabled,
        branch.PayrollRestDayPremiumEnabled,
        branch.PayrollNightDiffEnabled,
        branch.PayrollNightDiffPercent,
        branch.PayrollNightStart,
        branch.PayrollNightEnd,
        branch.ShiftEnd,
        branch.OtGraceMinutes,
        branch.AdminsCanApproveOt,
        branch.OtPremiumPercent,
        branch.OtMonthlyDayDivisor);

    public async Task<ShopHolidayListResponse> ListHolidaysAsync(
        IReadOnlyList<Branch> shops,
        int year,
        CancellationToken cancellationToken)
    {
        var shopIds = shops.Select(shop => shop.Id).ToList();
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        var rows = await db.ShopHolidays.AsNoTracking()
            .Where(holiday => holiday.Date >= from && holiday.Date <= to
                && (holiday.BranchId == null || shopIds.Contains(holiday.BranchId.Value)))
            .OrderBy(holiday => holiday.Date)
            .ThenBy(holiday => holiday.Name)
            .ToListAsync(cancellationToken);
        return new ShopHolidayListResponse(year, rows.Select(ToHolidayDto).ToList());
    }

    public async Task<(ShopHolidayDto? Saved, string? Error, int Status)> CreateHolidayAsync(
        IReadOnlyList<Branch> shops,
        Guid currentBranchId,
        UpsertShopHolidayRequest request,
        CancellationToken cancellationToken)
    {
        var (branchId, error, status) = ResolveHolidayBranch(shops, currentBranchId, request.BranchId);
        if (error is not null)
        {
            return (null, error, status);
        }

        var nameError = HolidayNameError(request.Name);
        if (nameError is not null)
        {
            return (null, nameError, StatusCodes.Status400BadRequest);
        }

        if (!Enum.IsDefined(request.Kind))
        {
            return (null, "Unknown holiday kind.", StatusCodes.Status400BadRequest);
        }

        var clash = await db.ShopHolidays.AnyAsync(
            holiday => holiday.BranchId == branchId && holiday.Date == request.Date,
            cancellationToken);
        if (clash)
        {
            return (null, "That date already has a holiday.", StatusCodes.Status409Conflict);
        }

        var row = new ShopHoliday
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            Date = request.Date,
            Name = request.Name.Trim(),
            Kind = request.Kind,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.ShopHolidays.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return (ToHolidayDto(row), null, StatusCodes.Status200OK);
    }

    public async Task<(ShopHolidayDto? Saved, string? Error, int Status)> UpdateHolidayAsync(
        IReadOnlyList<Branch> shops,
        Guid id,
        UpsertShopHolidayRequest request,
        CancellationToken cancellationToken)
    {
        var row = await db.ShopHolidays.FirstOrDefaultAsync(holiday => holiday.Id == id, cancellationToken);
        if (row is null || !HolidayInScope(row, shops))
        {
            return (null, null, StatusCodes.Status404NotFound);
        }

        var branchId = request.BranchId ?? row.BranchId;
        if (branchId is Guid shopId && shops.All(shop => shop.Id != shopId))
        {
            return (null, "That shop is not on your payroll scope.", StatusCodes.Status404NotFound);
        }

        var nameError = HolidayNameError(request.Name);
        if (nameError is not null)
        {
            return (null, nameError, StatusCodes.Status400BadRequest);
        }

        if (!Enum.IsDefined(request.Kind))
        {
            return (null, "Unknown holiday kind.", StatusCodes.Status400BadRequest);
        }

        var clash = await db.ShopHolidays.AnyAsync(
            holiday => holiday.Id != id && holiday.BranchId == branchId && holiday.Date == request.Date,
            cancellationToken);
        if (clash)
        {
            return (null, "That date already has a holiday.", StatusCodes.Status409Conflict);
        }

        row.BranchId = branchId;
        row.Date = request.Date;
        row.Name = request.Name.Trim();
        row.Kind = request.Kind;
        await db.SaveChangesAsync(cancellationToken);
        return (ToHolidayDto(row), null, StatusCodes.Status200OK);
    }

    public async Task<bool> DeleteHolidayAsync(
        IReadOnlyList<Branch> shops,
        Guid id,
        CancellationToken cancellationToken)
    {
        var row = await db.ShopHolidays.FirstOrDefaultAsync(holiday => holiday.Id == id, cancellationToken);
        if (row is null || !HolidayInScope(row, shops))
        {
            return false;
        }

        db.ShopHolidays.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<(CopyHolidayYearResponse? Result, string? Error)> CopyHolidayYearAsync(
        IReadOnlyList<Branch> shops,
        int fromYear,
        int toYear,
        CancellationToken cancellationToken)
    {
        if (fromYear is < 2000 or > 2100 || toYear is < 2000 or > 2100)
        {
            return (null, "Year must be between 2000 and 2100.");
        }

        var shopIds = shops.Select(shop => shop.Id).ToList();
        var sourceFrom = new DateOnly(fromYear, 1, 1);
        var sourceTo = new DateOnly(fromYear, 12, 31);
        var source = await db.ShopHolidays.AsNoTracking()
            .Where(holiday => holiday.Date >= sourceFrom && holiday.Date <= sourceTo
                && (holiday.BranchId == null || shopIds.Contains(holiday.BranchId.Value)))
            .ToListAsync(cancellationToken);

        var targetFrom = new DateOnly(toYear, 1, 1);
        var targetTo = new DateOnly(toYear, 12, 31);
        var existing = await db.ShopHolidays.AsNoTracking()
            .Where(holiday => holiday.Date >= targetFrom && holiday.Date <= targetTo
                && (holiday.BranchId == null || shopIds.Contains(holiday.BranchId.Value)))
            .Select(holiday => new { holiday.BranchId, holiday.Date })
            .ToListAsync(cancellationToken);
        var taken = existing.Select(row => (row.BranchId, row.Date)).ToHashSet();

        var created = 0;
        var skipped = 0;
        foreach (var holiday in source)
        {
            if (holiday.Date.Month == 2 && holiday.Date.Day == 29)
            {
                skipped++;
                continue;
            }

            var date = new DateOnly(toYear, holiday.Date.Month, holiday.Date.Day);
            if (!taken.Add((holiday.BranchId, date)))
            {
                skipped++;
                continue;
            }

            db.ShopHolidays.Add(new ShopHoliday
            {
                Id = Guid.NewGuid(),
                BranchId = holiday.BranchId,
                Date = date,
                Name = holiday.Name,
                Kind = holiday.Kind,
                CreatedAtUtc = DateTime.UtcNow
            });
            created++;
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return (new CopyHolidayYearResponse(created, skipped), null);
    }

    public async Task<PayrollOverrideSaveResult> SaveOverrideAsync(
        IReadOnlyList<Branch> shops,
        SavePayrollOverrideRequest request,
        bool includeUnscopedOwners,
        Guid? actorStaffId,
        CancellationToken cancellationToken)
    {
        var key = request.Key?.Trim() ?? "";
        if (key.Length == 0 || key.Length > 80)
        {
            return PayrollOverrideSaveResult.Bad("Key is required and must be 80 characters or fewer.");
        }

        var clearing = request.Amount is null;
        decimal amount = 0m;
        string? reason = null;
        if (request.Amount is decimal rawAmount)
        {
            if (rawAmount < 0)
            {
                return PayrollOverrideSaveResult.Bad("Amount cannot be negative.");
            }

            reason = request.Reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                return PayrollOverrideSaveResult.Bad("A reason is required.");
            }

            if (reason.Length > 500)
            {
                return PayrollOverrideSaveResult.Bad("Reason must be 500 characters or fewer.");
            }

            amount = RoundMoney(rawAmount);
            clearing = false;
        }

        var (start, end, _, _) = ResolvePeriod(request.From, request.To);
        var staff = await db.Staff.AsNoTracking()
            .Include(row => row.Profile)
            .FirstOrDefaultAsync(row => row.Id == request.StaffId, cancellationToken);
        if (staff is null || !IsInRegisterScope(staff, shops, includeUnscopedOwners))
        {
            return PayrollOverrideSaveResult.NotFound();
        }

        if (await PayrollRunService.IsPeriodLockedAsync(db, staff.Id, start, end, cancellationToken))
        {
            return PayrollOverrideSaveResult.Locked(PayrollRunService.LockedPeriodMessage);
        }

        var existing = await db.PayrollAmountOverrides.FirstOrDefaultAsync(
            row => row.StaffId == staff.Id
                && row.PeriodFrom == start
                && row.PeriodTo == end
                && row.Key == key,
            cancellationToken);
        var hr = new HrChangeLogService(db);
        if (clearing)
        {
            if (existing is null)
            {
                return PayrollOverrideSaveResult.Removed(
                    new PayrollOverrideClearedDto(staff.Id, start, end, key, true));
            }

            var previous = existing.Amount;
            db.PayrollAmountOverrides.Remove(existing);
            hr.RecordMoney(
                actorStaffId,
                staff.Id,
                HrChangeField.PayrollOverride,
                previous,
                null,
                start,
                end,
                refType: key.Length <= 40 ? key : null);
            await db.SaveChangesAsync(cancellationToken);
            return PayrollOverrideSaveResult.Removed(
                new PayrollOverrideClearedDto(staff.Id, start, end, key, true));
        }

        var before = existing?.Amount;
        if (existing is null)
        {
            existing = new PayrollAmountOverride
            {
                Id = Guid.NewGuid(),
                StaffId = staff.Id,
                PeriodFrom = start,
                PeriodTo = end,
                Key = key
            };
            db.PayrollAmountOverrides.Add(existing);
        }

        existing.Amount = amount;
        existing.Reason = reason!;
        existing.SetByStaffId = actorStaffId;
        existing.SetAtUtc = DateTime.UtcNow;
        hr.RecordMoney(
            actorStaffId,
            staff.Id,
            HrChangeField.PayrollOverride,
            before,
            amount,
            start,
            end,
            refType: key.Length <= 40 ? key : null);
        await db.SaveChangesAsync(cancellationToken);
        return PayrollOverrideSaveResult.Ok(ToOverrideDto(existing, staff.Profile?.DisplayName));
    }

    private static (Guid? BranchId, string? Error, int Status) ResolveHolidayBranch(
        IReadOnlyList<Branch> shops,
        Guid currentBranchId,
        Guid? requested)
    {
        var branchId = requested ?? (shops.Any(shop => shop.Id == currentBranchId) ? currentBranchId : shops.FirstOrDefault()?.Id);
        if (branchId is not Guid id || shops.All(shop => shop.Id != id))
        {
            return (null, "That shop is not on your payroll scope.", StatusCodes.Status404NotFound);
        }

        return (id, null, StatusCodes.Status200OK);
    }

    private static bool HolidayInScope(ShopHoliday holiday, IReadOnlyList<Branch> shops) =>
        holiday.BranchId is null || shops.Any(shop => shop.Id == holiday.BranchId);

    private static string? HolidayNameError(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
        {
            return "Holiday name is required and must be 120 characters or fewer.";
        }

        return null;
    }

    private static ShopHolidayDto ToHolidayDto(ShopHoliday holiday) =>
        new(holiday.Id, holiday.BranchId, holiday.Date, holiday.Name, holiday.Kind);

    private static PayrollOverrideDto ToOverrideDto(PayrollAmountOverride row, string? staffName) =>
        new(
            row.Id,
            row.StaffId,
            staffName,
            row.PeriodFrom,
            row.PeriodTo,
            row.Key,
            row.Amount,
            row.Reason,
            row.SetByStaffId,
            row.SetAtUtc);

    internal static string CsvOptionalMoney(decimal? value) =>
        value is null ? "" : Money(value.Value);

    /// <summary>A premium percent from 0 to 1000, rounded to 2 places. Throws with the label when out of range.</summary>
    public static decimal PremiumPercent(decimal value, string label)
    {
        if (value < 0 || value > 1000)
        {
            throw new InvalidOperationException($"{label} must be between 0 and 1000.");
        }

        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
