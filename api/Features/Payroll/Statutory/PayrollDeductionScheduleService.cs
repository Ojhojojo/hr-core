using HrCore.Features.SettingsLog;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Payroll;

public sealed record DeductionScheduleRuleDto(string DeductionKey, string Mode, int? CutoffOrdinal);

public sealed record DeductionScheduleDto(
    Guid BranchId,
    int CutoffCount,
    IReadOnlyList<DeductionScheduleRuleDto> Rules);

public sealed class UpdateDeductionScheduleRequest
{
    public List<DeductionScheduleRuleInput>? Rules { get; set; }
}

public sealed class DeductionScheduleRuleInput
{
    public string DeductionKey { get; set; } = "";
    public string Mode { get; set; } = PayrollStatutoryAllocation.Prorate;
    public int? CutoffOrdinal { get; set; }
}

public sealed record PayrollCatchUpDto(
    Guid Id,
    Guid StaffId,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string DeductionKey,
    decimal Amount,
    string Note);

public sealed class SavePayrollCatchUpRequest
{
    public Guid StaffId { get; set; }
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public string DeductionKey { get; set; } = "";
    public decimal Amount { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// Per-branch rules for which cut-off takes each deduction, plus catch-up rows stored by period dates.
/// </summary>
public sealed class PayrollDeductionScheduleService(IHrDb db, HrCoreFeatures features)
{
    public async Task<DeductionScheduleDto?> GetAsync(
        IReadOnlyList<Branch> shops,
        Guid branchId,
        CancellationToken cancellationToken)
    {
        if (!features.PayrollSchedules || shops.All(shop => shop.Id != branchId))
        {
            return null;
        }

        var cutoffCount = await db.BranchPayrollCutoffs().AsNoTracking()
            .CountAsync(row => row.BranchId == branchId, cancellationToken);
        var rules = await db.BranchDeductionSchedules().AsNoTracking()
            .Where(row => row.BranchId == branchId)
            .OrderBy(row => row.DeductionKey)
            .Select(row => new DeductionScheduleRuleDto(row.DeductionKey, row.Mode, row.CutoffOrdinal))
            .ToListAsync(cancellationToken);
        return new DeductionScheduleDto(branchId, cutoffCount, rules);
    }

    public async Task<(DeductionScheduleDto? Saved, Dictionary<string, string[]>? Errors)> SaveAsync(
        IReadOnlyList<Branch> shops,
        Guid branchId,
        UpdateDeductionScheduleRequest request,
        Guid? actorStaffId,
        CancellationToken cancellationToken)
    {
        if (!features.PayrollSchedules || shops.All(shop => shop.Id != branchId))
        {
            return (null, null);
        }

        var cutoffCount = await db.BranchPayrollCutoffs().AsNoTracking()
            .CountAsync(row => row.BranchId == branchId, cancellationToken);
        var inputs = request.Rules ?? [];
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cleaned = new List<(string Key, string Mode, int? Ordinal)>();
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            var key = (input.DeductionKey ?? "").Trim();
            var mode = (input.Mode ?? "").Trim().ToLowerInvariant();
            if (key.Length is 0 or > 80)
            {
                Add(errors, $"rules[{i}].deductionKey", "Deduction key is required and must be 80 characters or fewer.");
                continue;
            }

            if (!seen.Add(key))
            {
                Add(errors, $"rules[{i}].deductionKey", "That deduction is already in the list.");
            }

            if (mode is not (PayrollStatutoryAllocation.Prorate or PayrollStatutoryAllocation.Cutoff or PayrollStatutoryAllocation.Split))
            {
                Add(errors, $"rules[{i}].mode", "Mode must be prorate, cutoff, or split.");
                continue;
            }

            int? ordinal = null;
            if (mode == PayrollStatutoryAllocation.Cutoff)
            {
                ordinal = input.CutoffOrdinal;
                if (ordinal is null or < 1)
                {
                    Add(errors, $"rules[{i}].cutoffOrdinal", "Choose the cut-off number.");
                }
                else if (ordinal > cutoffCount)
                {
                    Add(errors, $"rules[{i}].cutoffOrdinal",
                        $"Cut-off {ordinal} does not exist. This shop has {cutoffCount}.");
                }
            }
            else if (mode == PayrollStatutoryAllocation.Split && cutoffCount == 0)
            {
                Add(errors, $"rules[{i}].mode", "Split needs a cut-off schedule on this shop.");
            }

            cleaned.Add((key, mode, ordinal));
        }

        if (errors.Count > 0)
        {
            return (null, errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal));
        }

        var existing = await db.BranchDeductionSchedules()
            .Where(row => row.BranchId == branchId)
            .OrderBy(row => row.DeductionKey)
            .ToListAsync(cancellationToken);
        var before = Describe(existing.Select(row => (row.DeductionKey, row.Mode, row.CutoffOrdinal)));
        var after = Describe(cleaned);
        db.BranchDeductionSchedules().RemoveRange(existing);
        if (existing.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        foreach (var (key, mode, ordinal) in cleaned.Where(row => row.Mode != PayrollStatutoryAllocation.Prorate))
        {
            db.BranchDeductionSchedules().Add(new BranchDeductionSchedule
            {
                Id = Guid.NewGuid(),
                BranchId = branchId,
                DeductionKey = key,
                Mode = mode,
                CutoffOrdinal = ordinal
            });
        }

        new SettingsChangeLogService(db).Record(
            actorStaffId,
            branchId,
            SettingsArea.PayrollDeduction,
            "schedule",
            before,
            after);
        await db.SaveChangesAsync(cancellationToken);
        return (await GetAsync(shops, branchId, cancellationToken), null);
    }

    public async Task<(PayrollCatchUpDto? Saved, Dictionary<string, string[]>? Errors, int Status)> SaveCatchUpAsync(
        IReadOnlyList<Branch> shops,
        SavePayrollCatchUpRequest request,
        Guid? actorStaffId,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        if (!features.PayrollSchedules)
        {
            return (null, null, StatusCodes.Status404NotFound);
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var key = (request.DeductionKey ?? "").Trim();
        if (key.Length is 0 or > 80)
        {
            errors["deductionKey"] = ["Deduction key is required and must be 80 characters or fewer."];
        }

        if (request.PeriodFrom > request.PeriodTo)
        {
            errors["periodTo"] = ["The period end must not be before the start."];
        }

        if (request.Amount < 0m || request.Amount != PayrollStatutoryAllocation.Round(request.Amount))
        {
            errors["amount"] = ["Amount must be zero or more, with at most 2 decimal places."];
        }

        var note = (request.Note ?? "").Trim();
        if (request.Amount > 0m && note.Length == 0)
        {
            errors["note"] = ["A catch-up amount needs a note."];
        }

        if (note.Length > 500)
        {
            errors["note"] = ["Note must be 500 characters or fewer."];
        }

        if (errors.Count > 0)
        {
            return (null, errors, StatusCodes.Status400BadRequest);
        }

        var staff = await db.Staff.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == request.StaffId, cancellationToken);
        var shopIds = shops.Select(shop => shop.Id).ToList();
        var inScope = staff is not null && (
            (staff.BranchId is Guid home && shopIds.Contains(home))
            || (includeUnscopedOwners && staff.BranchId is null && staff.Role == StaffRole.Owner));
        if (!inScope)
        {
            return (null, null, StatusCodes.Status404NotFound);
        }

        var existing = await db.PayrollCatchUps()
            .FirstOrDefaultAsync(row => row.StaffId == request.StaffId
                && row.PeriodFrom == request.PeriodFrom
                && row.PeriodTo == request.PeriodTo
                && row.DeductionKey == key, cancellationToken);
        if (request.Amount == 0m)
        {
            if (existing is not null)
            {
                db.PayrollCatchUps().Remove(existing);
                await db.SaveChangesAsync(cancellationToken);
            }

            return (null, null, StatusCodes.Status204NoContent);
        }

        if (existing is null)
        {
            existing = new PayrollCatchUp
            {
                Id = Guid.NewGuid(),
                StaffId = request.StaffId,
                DeductionKey = key
            };
            db.PayrollCatchUps().Add(existing);
        }

        existing.PeriodFrom = request.PeriodFrom;
        existing.PeriodTo = request.PeriodTo;
        existing.Amount = request.Amount;
        existing.Note = note;
        existing.RecordedByStaffId = actorStaffId;
        existing.RecordedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return (ToCatchUp(existing), null, StatusCodes.Status200OK);
    }

    /// <summary>
    /// Rules and the positional period the dates match, for one branch. Empty rules mean prorate.
    /// The matched ordinal is computed from dates and is not persisted.
    /// </summary>
    public async Task<(IReadOnlyList<BranchDeductionSchedule> Rules, int? MatchedOrdinal, int PeriodCount)> ForPeriodAsync(
        Guid branchId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        if (!features.PayrollSchedules)
        {
            return ([], null, 0);
        }

        var rules = await db.BranchDeductionSchedules().AsNoTracking()
            .Where(row => row.BranchId == branchId)
            .ToListAsync(cancellationToken);
        if (rules.Count == 0)
        {
            return ([], null, 0);
        }

        var cutoffs = await db.BranchPayrollCutoffs().AsNoTracking()
            .Where(row => row.BranchId == branchId)
            .OrderBy(row => row.Ordinal)
            .ToListAsync(cancellationToken);
        var cutoffRules = cutoffs.Select(row => new PayrollCutoffRule(row.Ordinal, row.StartDay, row.EndDay)).ToList();
        var periods = cutoffRules.Count == 0
            ? []
            : PayrollCutoffCalendar.PeriodsCovering(from, to, cutoffRules);
        var match = periods.FirstOrDefault(period => period.From == from && period.To == to);
        return (rules, match?.Ordinal, periods.Count);
    }

    private static PayrollCatchUpDto ToCatchUp(PayrollCatchUp row) => new(
        row.Id,
        row.StaffId,
        row.PeriodFrom,
        row.PeriodTo,
        row.DeductionKey,
        row.Amount,
        row.Note);

    private static string Describe(IEnumerable<(string Key, string Mode, int? Ordinal)> rows)
    {
        var text = string.Join(
            ';',
            rows.OrderBy(row => row.Key, StringComparer.Ordinal)
                .Select(row => row.Ordinal is int ordinal
                    ? $"{row.Key}:{row.Mode}:{ordinal}"
                    : $"{row.Key}:{row.Mode}"));
        return text.Length == 0 ? PayrollStatutoryAllocation.Prorate : text;
    }

    private static void Add(Dictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var list))
        {
            list = [];
            errors[key] = list;
        }

        list.Add(message);
    }
}
