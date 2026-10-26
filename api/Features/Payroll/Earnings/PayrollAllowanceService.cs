using System.Globalization;
using HrCore.Features.HrChangeLog;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Payroll;

public sealed partial class PayrollService
{
    private sealed class AllowanceContext
    {
        public required Dictionary<Guid, IReadOnlyList<StaffAllowance>> ByStaff { get; init; }
        public required Dictionary<Guid, PayrollAmountOverride> Overrides { get; init; }
        public required Dictionary<Guid, IReadOnlyList<IncentiveBonus>> Bonuses { get; init; }
    }

    private readonly record struct AllowancePricing(
        decimal? Allowances,
        IReadOnlyList<PayrollAllowanceItemDto>? Items,
        bool Overridden,
        decimal AddedToGross,
        IReadOnlyList<string> Warnings);

    private async Task<AllowanceContext> LoadAllowanceContextAsync(
        IReadOnlyList<Guid> staffIds,
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken)
    {
        var rows = staffIds.Count == 0
            ? []
            : await db.StaffAllowances.AsNoTracking()
                .Where(row => staffIds.Contains(row.StaffId) && row.Active)
                .ToListAsync(cancellationToken);
        var byStaff = rows
            .GroupBy(row => row.StaffId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<StaffAllowance>)group.ToList());

        var overrides = staffIds.Count == 0
            ? new Dictionary<Guid, PayrollAmountOverride>()
            : await db.PayrollAmountOverrides.AsNoTracking()
                .Where(row => staffIds.Contains(row.StaffId)
                    && row.PeriodFrom == start
                    && row.PeriodTo == end
                    && row.Key == PayrollAllowanceCalculator.OverrideKey)
                .ToDictionaryAsync(row => row.StaffId, cancellationToken);

        // Scheduled incentive bonuses for exactly this pay period. They are taxable earnings, so they
        // ride the allowance items into gross, the statutory base, the payslip and the locked run.
        var bonuses = staffIds.Count == 0
            ? new List<IncentiveBonus>()
            : await db.IncentiveBonuses.AsNoTracking()
                .Where(bonus => staffIds.Contains(bonus.StaffId)
                    && bonus.Status == IncentiveBonusStatus.Scheduled
                    && bonus.PayPeriodFrom >= start
                    && bonus.PayPeriodTo <= end)
                .OrderBy(bonus => bonus.CreatedAtUtc)
                .ThenBy(bonus => bonus.Id)
                .ToListAsync(cancellationToken);
        var bonusByStaff = bonuses
            .GroupBy(bonus => bonus.StaffId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<IncentiveBonus>)group.ToList());

        return new AllowanceContext { ByStaff = byStaff, Overrides = overrides, Bonuses = bonusByStaff };
    }

    /// <summary>
    /// Scheduled bonuses for these people whose pay period has ended (Manila) and that no locked run paid.
    /// Shown on the register so a bonus that never fell inside a register range is not forgotten.
    /// </summary>
    internal async Task<IReadOnlyList<UnpaidBonusDto>> LoadUnpaidBonusesAsync(
        IReadOnlyList<Guid> staffIds,
        CancellationToken cancellationToken)
    {
        if (staffIds.Count == 0)
        {
            return [];
        }

        var today = ManilaTime.Today();
        var bonuses = await db.IncentiveBonuses.AsNoTracking()
            .Where(bonus => staffIds.Contains(bonus.StaffId)
                && bonus.Status == IncentiveBonusStatus.Scheduled
                && bonus.PayPeriodFrom != null
                && bonus.PayPeriodTo != null
                && bonus.PayPeriodTo < today)
            .OrderBy(bonus => bonus.PayPeriodTo)
            .ThenBy(bonus => bonus.Id)
            .ToListAsync(cancellationToken);
        if (bonuses.Count == 0)
        {
            return [];
        }

        var names = await db.Staff.AsNoTracking()
            .Include(staff => staff.Profile)
            .Where(staff => bonuses.Select(bonus => bonus.StaffId).Contains(staff.Id))
            .ToDictionaryAsync(staff => staff.Id, staff => staff.Profile.DisplayName, cancellationToken);

        return bonuses
            .Select(bonus => new UnpaidBonusDto(
                bonus.Id,
                bonus.StaffId,
                names.GetValueOrDefault(bonus.StaffId, "Unknown"),
                bonus.Amount,
                bonus.Reason,
                bonus.PayPeriodFrom!.Value,
                bonus.PayPeriodTo!.Value))
            .ToList();
    }

    private static AllowancePricing PriceStaffAllowances(
        Domain.Staff staff,
        IReadOnlySet<DateOnly> daysWorked,
        DateOnly start,
        DateOnly end,
        AllowanceContext context)
    {
        var mine = context.ByStaff.GetValueOrDefault(staff.Id) ?? [];
        var computed = PayrollAllowanceCalculator.Compute(mine, start, end, daysWorked);
        var hasOverride = context.Overrides.TryGetValue(staff.Id, out var allowanceOverride);
        var bonusRows = context.Bonuses.GetValueOrDefault(staff.Id) ?? [];
        var bonusTotal = bonusRows.Sum(bonus => bonus.Amount);

        var allItems = computed.Items
            .Select(item => new PayrollAllowanceItemDto(
                item.AllowanceId,
                item.Name,
                item.Per,
                item.Taxable,
                item.Quantity,
                item.Amount))
            .Concat(bonusRows.Select(bonus => new PayrollAllowanceItemDto(
                bonus.Id,
                PayrollAllowanceCalculator.IncentiveLabel(bonus.Year, bonus.Quarter, bonus.Reason),
                null,
                true,
                1m,
                bonus.Amount)))
            .ToList();
        IReadOnlyList<PayrollAllowanceItemDto>? items = allItems.Count == 0 ? null : allItems;

        // The owner's allowance override replaces the computed allowances only. Bonuses are added on top.
        decimal? allowances = items is null && !hasOverride
            ? null
            : (hasOverride ? allowanceOverride!.Amount : computed.Total) + bonusTotal;

        var warnings = new List<string>();
        if (staff.PayBasis == PayBasis.Monthly)
        {
            var noRestDays = !staff.MustClock && RestDayList.Parse(staff.RestDays).Count == 0;
            foreach (var item in computed.Items)
            {
                if (item.Per == AllowancePer.Day && item.Quantity == 0)
                {
                    if (staff.MustClock)
                    {
                        warnings.Add($"Per-day allowance {item.Name} needs clock punches");
                    }
                    else if (noRestDays)
                    {
                        warnings.Add($"Set rest days so per-day allowance {item.Name} can be counted");
                    }
                }
            }
        }

        return new AllowancePricing(allowances, items, hasOverride, allowances ?? 0m, warnings);
    }

    /// <summary>
    /// The days per-day allowances are counted on. A Monthly person who does not clock in is counted on
    /// their schedule, provided rest days are set. Everyone else is counted on approved clock punches.
    /// </summary>
    internal static IReadOnlySet<DateOnly> AllowanceDaysWorked(
        Domain.Staff staff,
        IReadOnlyList<AttendancePunch> punches,
        DateOnly start,
        DateOnly end,
        IEnumerable<DateOnly>? unpaidDays,
        IReadOnlyDictionary<DateOnly, decimal>? paidLeaveDays,
        IEnumerable<PayrollDayPayOverride>? dayOverrides)
    {
        var restDays = RestDayList.Parse(staff.RestDays);
        if (staff.PayBasis != PayBasis.Monthly || staff.MustClock || restDays.Count == 0)
        {
            return DaysWorked(punches);
        }

        var leaveDays = (unpaidDays ?? Enumerable.Empty<DateOnly>())
            .Concat(paidLeaveDays?.Keys ?? Enumerable.Empty<DateOnly>());
        var noneDays = (dayOverrides ?? Enumerable.Empty<PayrollDayPayOverride>())
            .Where(row => row.Mode == PayrollDayPayMode.None)
            .Select(row => row.WorkDate);
        return ScheduledDaysWorked(start, end, restDays, leaveDays, noneDays);
    }

    /// <summary>
    /// Every date in [start, end] that is not a rest day, not approved leave (paid or unpaid) and not a
    /// day overridden to None. Overrides Full, Half and Custom count as worked days.
    /// </summary>
    internal static HashSet<DateOnly> ScheduledDaysWorked(
        DateOnly start,
        DateOnly end,
        IReadOnlySet<DayOfWeek> restDays,
        IEnumerable<DateOnly> leaveDays,
        IEnumerable<DateOnly> noneOverrideDays)
    {
        var excluded = new HashSet<DateOnly>(leaveDays);
        excluded.UnionWith(noneOverrideDays);

        var days = new HashSet<DateOnly>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            if (!restDays.Contains(day.DayOfWeek) && !excluded.Contains(day))
            {
                days.Add(day);
            }
        }

        return days;
    }

    private static HashSet<DateOnly> DaysWorked(IReadOnlyList<AttendancePunch> punches)
    {
        var days = new HashSet<DateOnly>();
        foreach (var punch in punches)
        {
            if (punch.OutAtUtc is not null && punch.ApprovalStatus == PunchApprovalStatus.Approved)
            {
                days.Add(ManilaTime.ToManilaDate(punch.InAtUtc));
            }
        }

        return days;
    }

    internal static async Task<Dictionary<Guid, IReadOnlyList<PayrollAllowanceItemDto>>> LoadLockedAllowanceItemsAsync(
        IHrDb db,
        IReadOnlyCollection<Guid> lineIds,
        CancellationToken cancellationToken)
    {
        if (lineIds.Count == 0)
        {
            return [];
        }

        var rows = await db.PayrollRunLineItems.AsNoTracking()
            .Where(item => lineIds.Contains(item.LineId) && item.Kind == PayrollAllowanceCalculator.ItemKind)
            .OrderBy(item => item.Label)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(item => item.LineId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PayrollAllowanceItemDto>)group.Select(ToLockedAllowanceItem).ToList());
    }

    private static PayrollAllowanceItemDto ToLockedAllowanceItem(PayrollRunLineItem item) =>
        new(item.RefId, item.Label, null, item.Taxable ?? true, item.Quantity ?? 0m, item.Amount);

    public async Task<StaffAllowanceListResponse?> ListAllowancesAsync(
        IReadOnlyList<Branch> shops,
        Guid? staffId,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        var staffIds = await ScopedStaffQuery(shops, includeInactive: true, includeUnscopedOwners)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);
        if (staffId is Guid requested && !staffIds.Contains(requested))
        {
            return null;
        }

        var rows = staffIds.Count == 0
            ? []
            : await db.StaffAllowances.AsNoTracking()
                .Where(row => staffIds.Contains(row.StaffId))
                .OrderBy(row => row.Name)
                .ThenBy(row => row.CreatedAtUtc)
                .ToListAsync(cancellationToken);

        var suggested = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in rows.Select(row => row.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            if (seen.Add(name))
            {
                suggested.Add(name);
            }
        }

        var shown = staffId is Guid filter ? rows.Where(row => row.StaffId == filter).ToList() : rows;
        return new StaffAllowanceListResponse(shown.Select(ToAllowanceDto).ToList(), suggested);
    }

    public async Task<AllowanceWriteResult> CreateAllowanceAsync(
        IReadOnlyList<Branch> shops,
        SaveStaffAllowanceRequest request,
        bool includeUnscopedOwners,
        Guid? actorStaffId,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeAllowance(
                request.Name,
                request.Amount,
                request.Per,
                request.EffectiveFrom,
                request.EffectiveTo,
                request.Note,
                out var name,
                out var note,
                out var amount,
                out var error))
        {
            return AllowanceWriteResult.Bad(error!);
        }

        var staff = await db.Staff.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == request.StaffId, cancellationToken);
        if (staff is null || !IsInRegisterScope(staff, shops, includeUnscopedOwners))
        {
            return AllowanceWriteResult.NotFound();
        }

        var row = new StaffAllowance
        {
            Id = Guid.NewGuid(),
            StaffId = staff.Id,
            Name = name,
            Amount = amount,
            Per = request.Per,
            Taxable = request.Taxable ?? true,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            Note = note,
            Active = request.Active ?? true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByStaffId = actorStaffId
        };
        db.StaffAllowances.Add(row);
        new HrChangeLogService(db).Record(
            actorStaffId,
            staff.Id,
            HrChangeField.PayrollAllowance,
            null,
            AllowanceLogValue(name, amount),
            refType: "allowance",
            refId: row.Id);
        await db.SaveChangesAsync(cancellationToken);
        return AllowanceWriteResult.Ok(ToAllowanceDto(row));
    }

    public async Task<AllowanceWriteResult> UpdateAllowanceAsync(
        IReadOnlyList<Branch> shops,
        Guid id,
        UpdateStaffAllowanceRequest request,
        bool includeUnscopedOwners,
        Guid? actorStaffId,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeAllowance(
                request.Name,
                request.Amount,
                request.Per,
                request.EffectiveFrom,
                request.EffectiveTo,
                request.Note,
                out var name,
                out var note,
                out var amount,
                out var error))
        {
            return AllowanceWriteResult.Bad(error!);
        }

        var row = await db.StaffAllowances.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return AllowanceWriteResult.NotFound();
        }

        var staff = await db.Staff.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == row.StaffId, cancellationToken);
        if (staff is null || !IsInRegisterScope(staff, shops, includeUnscopedOwners))
        {
            return AllowanceWriteResult.NotFound();
        }

        var before = AllowanceLogValue(row.Name, row.Amount);
        row.Name = name;
        row.Amount = amount;
        row.Per = request.Per;
        row.Taxable = request.Taxable ?? true;
        row.EffectiveFrom = request.EffectiveFrom;
        row.EffectiveTo = request.EffectiveTo;
        row.Note = note;
        row.Active = request.Active ?? true;
        new HrChangeLogService(db).Record(
            actorStaffId,
            row.StaffId,
            HrChangeField.PayrollAllowance,
            before,
            AllowanceLogValue(name, amount),
            refType: "allowance",
            refId: row.Id);
        await db.SaveChangesAsync(cancellationToken);
        return AllowanceWriteResult.Ok(ToAllowanceDto(row));
    }

    public async Task<bool> DeleteAllowanceAsync(
        IReadOnlyList<Branch> shops,
        Guid id,
        bool includeUnscopedOwners,
        Guid? actorStaffId,
        CancellationToken cancellationToken)
    {
        var row = await db.StaffAllowances.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return false;
        }

        var staff = await db.Staff.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == row.StaffId, cancellationToken);
        if (staff is null || !IsInRegisterScope(staff, shops, includeUnscopedOwners))
        {
            return false;
        }

        var before = AllowanceLogValue(row.Name, row.Amount);
        db.StaffAllowances.Remove(row);
        new HrChangeLogService(db).Record(
            actorStaffId,
            row.StaffId,
            HrChangeField.PayrollAllowance,
            before,
            null,
            refType: "allowance",
            refId: row.Id);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static bool TryNormalizeAllowance(
        string? name,
        decimal amount,
        AllowancePer per,
        DateOnly? effectiveFrom,
        DateOnly? effectiveTo,
        string? note,
        out string trimmedName,
        out string? trimmedNote,
        out decimal rounded,
        out string? error)
    {
        trimmedName = name?.Trim() ?? "";
        trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        rounded = 0m;
        if (trimmedName.Length == 0 || trimmedName.Length > 80)
        {
            error = "Name is required and must be 80 characters or fewer.";
            return false;
        }

        if (!Enum.IsDefined(per))
        {
            error = "Unknown allowance period.";
            return false;
        }

        if (amount < 0)
        {
            error = "Amount cannot be negative.";
            return false;
        }

        if (trimmedNote is { Length: > 500 })
        {
            error = "Note must be 500 characters or fewer.";
            return false;
        }

        if (effectiveFrom is DateOnly from && effectiveTo is DateOnly to && to < from)
        {
            error = "Effective to must be on or after effective from.";
            return false;
        }

        rounded = RoundMoney(amount);
        error = null;
        return true;
    }

    private static string AllowanceLogValue(string name, decimal amount) =>
        $"{name} {amount.ToString("0.00", CultureInfo.InvariantCulture)}";

    private static StaffAllowanceDto ToAllowanceDto(StaffAllowance row) =>
        new(
            row.Id,
            row.StaffId,
            row.Name,
            row.Amount,
            row.Per,
            row.Taxable,
            row.EffectiveFrom,
            row.EffectiveTo,
            row.Note,
            row.Active,
            row.CreatedAtUtc);
}
