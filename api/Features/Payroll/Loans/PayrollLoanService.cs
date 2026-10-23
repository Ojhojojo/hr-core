using System.Globalization;
using HrCore.Features.HrChangeLog;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Payroll;

public sealed partial class PayrollService
{
    private sealed class LoanContext
    {
        public required Dictionary<Guid, IReadOnlyList<PayrollLoanSchedule.LoanInput>> ByStaff { get; init; }
        public required Dictionary<Guid, Dictionary<Guid, decimal>> OverridesByStaff { get; init; }
    }

    private readonly record struct LoanPricing(
        decimal? LoanDeductions,
        IReadOnlyList<PayrollLoanItemDto>? Items,
        IReadOnlyList<string> Warnings);

    private async Task<LoanContext> LoadLoanContextAsync(
        IReadOnlyList<Guid> staffIds,
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken)
    {
        var loans = staffIds.Count == 0
            ? []
            : await db.StaffLoans.AsNoTracking()
                .Where(row => staffIds.Contains(row.StaffId))
                .ToListAsync(cancellationToken);
        var loanIds = loans.Select(row => row.Id).ToList();
        var paid = await UnreversedPaidByLoanAsync(loanIds, cancellationToken);
        var byStaff = loans
            .GroupBy(row => row.StaffId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PayrollLoanSchedule.LoanInput>)group
                    .Select(row => new PayrollLoanSchedule.LoanInput(
                        row.Id,
                        row.Kind,
                        RoundMoney(row.Principal - paid.GetValueOrDefault(row.Id)),
                        row.PerPeriodAmount,
                        row.StartPeriod,
                        row.CreatedAtUtc,
                        row.CancelledAtUtc is not null))
                    .ToList());

        var overrideRows = staffIds.Count == 0
            ? []
            : await db.PayrollAmountOverrides.AsNoTracking()
                .Where(row => staffIds.Contains(row.StaffId)
                    && row.PeriodFrom == start
                    && row.PeriodTo == end
                    && row.Key.StartsWith(PayrollLoanSchedule.OverrideKeyPrefix))
                .ToListAsync(cancellationToken);
        var overridesByStaff = new Dictionary<Guid, Dictionary<Guid, decimal>>();
        foreach (var row in overrideRows)
        {
            if (!PayrollLoanSchedule.TryParseOverrideKey(row.Key, out var loanId))
            {
                continue;
            }

            if (!overridesByStaff.TryGetValue(row.StaffId, out var map))
            {
                map = [];
                overridesByStaff[row.StaffId] = map;
            }

            map[loanId] = row.Amount;
        }

        return new LoanContext { ByStaff = byStaff, OverridesByStaff = overridesByStaff };
    }

    private static LoanPricing PriceStaffLoans(
        Guid staffId,
        decimal netBeforeLoans,
        DateOnly start,
        DateOnly end,
        LoanContext context)
    {
        var mine = context.ByStaff.GetValueOrDefault(staffId) ?? [];
        var overrides = context.OverridesByStaff.GetValueOrDefault(staffId)
            ?? new Dictionary<Guid, decimal>();
        var suggested = PayrollLoanSchedule.Suggest(mine, start, end, netBeforeLoans, overrides);
        if (suggested.Items.Count == 0)
        {
            return new LoanPricing(null, null, []);
        }

        var items = suggested.Items
            .Select(item => new PayrollLoanItemDto(
                item.LoanId,
                item.Kind,
                item.Suggested,
                item.Amount,
                item.BalanceBefore,
                item.BalanceAfter,
                item.Capped))
            .ToList();
        return new LoanPricing(suggested.Total, items, suggested.Warnings);
    }

    private static IReadOnlyList<string>? MergeWarnings(
        IReadOnlyList<string>? premium,
        IReadOnlyList<string> allowance,
        IReadOnlyList<string> loans,
        IReadOnlyList<string> deductionCap)
    {
        if (allowance.Count == 0 && loans.Count == 0 && deductionCap.Count == 0)
        {
            return premium;
        }

        var merged = new List<string>();
        if (premium is { Count: > 0 })
        {
            merged.AddRange(premium);
        }

        if (allowance.Count > 0)
        {
            merged.AddRange(allowance);
        }

        if (loans.Count > 0)
        {
            merged.AddRange(loans);
        }

        if (deductionCap.Count > 0)
        {
            merged.AddRange(deductionCap);
        }

        return merged;
    }

    internal static async Task<Dictionary<Guid, IReadOnlyList<PayrollLoanItemDto>>> LoadLockedLoanItemsAsync(
        IHrDb db,
        IReadOnlyCollection<Guid> lineIds,
        CancellationToken cancellationToken)
    {
        if (lineIds.Count == 0)
        {
            return [];
        }

        var rows = await db.PayrollRunLineItems.AsNoTracking()
            .Where(item => lineIds.Contains(item.LineId) && item.Kind == PayrollLoanSchedule.ItemKind)
            .OrderBy(item => item.Label)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(item => item.LineId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PayrollLoanItemDto>)group.Select(ToLockedLoanItem).ToList());
    }

    private static PayrollLoanItemDto ToLockedLoanItem(PayrollRunLineItem item)
    {
        var balanceAfter = item.BalanceAfter ?? 0m;
        return new PayrollLoanItemDto(
            item.RefId ?? Guid.Empty,
            item.Label,
            item.Amount,
            item.Amount,
            balanceAfter + item.Amount,
            balanceAfter,
            false);
    }

    public async Task<StaffLoanListResponse?> ListLoansAsync(
        IReadOnlyList<Branch> shops,
        Guid? staffId,
        bool includeClosed,
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
            : await db.StaffLoans.AsNoTracking()
                .Where(row => staffIds.Contains(row.StaffId))
                .OrderBy(row => row.StartPeriod)
                .ThenBy(row => row.CreatedAtUtc)
                .ToListAsync(cancellationToken);
        var paid = await UnreversedPaidByLoanAsync(rows.Select(row => row.Id).ToList(), cancellationToken);
        var shown = rows.Select(row => ToLoanDto(row, paid)).ToList();
        if (staffId is Guid filter)
        {
            shown = shown.Where(row => row.StaffId == filter).ToList();
        }

        if (!includeClosed)
        {
            shown = shown.Where(row => row.Status == StaffLoanStatus.Active).ToList();
        }

        return new StaffLoanListResponse(shown, SuggestedLoanKinds(rows.Select(row => row.Kind)));
    }

    public async Task<StaffLoanDetailDto?> GetLoanAsync(
        IReadOnlyList<Branch> shops,
        Guid id,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        var row = await db.StaffLoans.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null || !await StaffInScopeAsync(shops, row.StaffId, includeUnscopedOwners, cancellationToken))
        {
            return null;
        }

        var paid = await UnreversedPaidByLoanAsync([row.Id], cancellationToken);
        var payments = await db.StaffLoanPayments.AsNoTracking()
            .Where(payment => payment.LoanId == row.Id)
            .OrderBy(payment => payment.CreatedAtUtc)
            .ThenBy(payment => payment.Id)
            .ToListAsync(cancellationToken);
        var dto = ToLoanDto(row, paid);
        return new StaffLoanDetailDto(
            dto.Id,
            dto.StaffId,
            dto.Kind,
            dto.Principal,
            dto.PerPeriodAmount,
            dto.StartPeriod,
            dto.Note,
            dto.CreatedAtUtc,
            dto.CancelledAtUtc,
            dto.CancelReason,
            dto.Paid,
            dto.Balance,
            dto.Status,
            payments.Select(payment => new StaffLoanPaymentDto(
                payment.Id,
                payment.LoanId,
                payment.RunId,
                payment.StaffId,
                payment.PeriodFrom,
                payment.PeriodTo,
                payment.Amount,
                payment.CreatedAtUtc,
                payment.ReversedAtUtc)).ToList());
    }

    public async Task<LoanWriteResult> CreateLoanAsync(
        IReadOnlyList<Branch> shops,
        SaveStaffLoanRequest request,
        bool includeUnscopedOwners,
        Guid? actorStaffId,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeLoan(request.Kind, request.PerPeriodAmount, request.Note, out var kind, out var perPeriod, out var note, out var error))
        {
            return LoanWriteResult.Bad(error!);
        }

        var principal = RoundMoney(request.Principal);
        if (principal <= 0m)
        {
            return LoanWriteResult.Bad("Principal must be greater than zero.");
        }

        var staff = await db.Staff.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == request.StaffId, cancellationToken);
        if (staff is null || !IsInRegisterScope(staff, shops, includeUnscopedOwners))
        {
            return LoanWriteResult.NotFound();
        }

        var row = new StaffLoan
        {
            Id = Guid.NewGuid(),
            StaffId = staff.Id,
            Kind = kind,
            Principal = principal,
            PerPeriodAmount = perPeriod,
            StartPeriod = request.StartPeriod,
            Note = note,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByStaffId = actorStaffId
        };
        db.StaffLoans.Add(row);
        new HrChangeLogService(db).Record(
            actorStaffId,
            staff.Id,
            HrChangeField.PayrollLoan,
            null,
            LoanLogValue(kind, principal, perPeriod, request.StartPeriod),
            refType: "loan",
            refId: row.Id);
        await db.SaveChangesAsync(cancellationToken);
        return LoanWriteResult.Ok(ToLoanDto(row, EmptyPaid));
    }

    public async Task<LoanWriteResult> UpdateLoanAsync(
        IReadOnlyList<Branch> shops,
        Guid id,
        UpdateStaffLoanRequest request,
        bool includeUnscopedOwners,
        Guid? actorStaffId,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeLoan(request.Kind, request.PerPeriodAmount, request.Note, out var kind, out var perPeriod, out var note, out var error))
        {
            return LoanWriteResult.Bad(error!);
        }

        var row = await db.StaffLoans.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null || !await StaffInScopeAsync(shops, row.StaffId, includeUnscopedOwners, cancellationToken))
        {
            return LoanWriteResult.NotFound();
        }

        var before = LoanLogValue(row.Kind, row.Principal, row.PerPeriodAmount, row.StartPeriod);
        if (request.Principal is decimal nextPrincipal)
        {
            var rounded = RoundMoney(nextPrincipal);
            if (rounded != row.Principal)
            {
                if (rounded <= 0m)
                {
                    return LoanWriteResult.Bad("Principal must be greater than zero.");
                }

                var unreversed = await db.StaffLoanPayments
                    .AnyAsync(payment => payment.LoanId == row.Id && payment.ReversedAtUtc == null, cancellationToken);
                if (unreversed)
                {
                    return LoanWriteResult.Conflict(
                        "Principal cannot change while this loan has a payment on a run. Void the run to reverse it.");
                }

                row.Principal = rounded;
            }
        }

        row.Kind = kind;
        row.PerPeriodAmount = perPeriod;
        row.StartPeriod = request.StartPeriod;
        row.Note = note;
        new HrChangeLogService(db).Record(
            actorStaffId,
            row.StaffId,
            HrChangeField.PayrollLoan,
            before,
            LoanLogValue(row.Kind, row.Principal, row.PerPeriodAmount, row.StartPeriod),
            refType: "loan",
            refId: row.Id);
        await db.SaveChangesAsync(cancellationToken);
        var paid = await UnreversedPaidByLoanAsync([row.Id], cancellationToken);
        return LoanWriteResult.Ok(ToLoanDto(row, paid));
    }

    public async Task<LoanWriteResult> CancelLoanAsync(
        IReadOnlyList<Branch> shops,
        Guid id,
        CancelStaffLoanRequest request,
        bool includeUnscopedOwners,
        Guid? actorStaffId,
        CancellationToken cancellationToken)
    {
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (reason is null || reason.Length > 500)
        {
            return LoanWriteResult.Bad("A reason is required and must be 500 characters or fewer.");
        }

        var row = await db.StaffLoans.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null || !await StaffInScopeAsync(shops, row.StaffId, includeUnscopedOwners, cancellationToken))
        {
            return LoanWriteResult.NotFound();
        }

        if (row.CancelledAtUtc is null)
        {
            var before = LoanLogValue(row.Kind, row.Principal, row.PerPeriodAmount, row.StartPeriod);
            row.CancelledAtUtc = DateTime.UtcNow;
            row.CancelReason = reason;
            new HrChangeLogService(db).Record(
                actorStaffId,
                row.StaffId,
                HrChangeField.PayrollLoan,
                before,
                "cancelled: " + reason,
                refType: "loan",
                refId: row.Id);
            await db.SaveChangesAsync(cancellationToken);
        }

        var paid = await UnreversedPaidByLoanAsync([row.Id], cancellationToken);
        return LoanWriteResult.Ok(ToLoanDto(row, paid));
    }

    public async Task<LoanDeleteResult> DeleteLoanAsync(
        IReadOnlyList<Branch> shops,
        Guid id,
        bool includeUnscopedOwners,
        Guid? actorStaffId,
        CancellationToken cancellationToken)
    {
        var row = await db.StaffLoans.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null || !await StaffInScopeAsync(shops, row.StaffId, includeUnscopedOwners, cancellationToken))
        {
            return LoanDeleteResult.NotFound();
        }

        var anyPayment = await db.StaffLoanPayments.AnyAsync(payment => payment.LoanId == row.Id, cancellationToken);
        if (anyPayment)
        {
            return LoanDeleteResult.Conflict(
                "This loan has payments, so it cannot be deleted. Cancel it to stop future instalments.");
        }

        var before = LoanLogValue(row.Kind, row.Principal, row.PerPeriodAmount, row.StartPeriod);
        db.StaffLoans.Remove(row);
        new HrChangeLogService(db).Record(
            actorStaffId,
            row.StaffId,
            HrChangeField.PayrollLoan,
            before,
            null,
            refType: "loan",
            refId: row.Id);
        await db.SaveChangesAsync(cancellationToken);
        return LoanDeleteResult.Ok();
    }

    private async Task<bool> StaffInScopeAsync(
        IReadOnlyList<Branch> shops,
        Guid staffId,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        var staff = await db.Staff.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == staffId, cancellationToken);
        return staff is not null && IsInRegisterScope(staff, shops, includeUnscopedOwners);
    }

    private async Task<Dictionary<Guid, decimal>> UnreversedPaidByLoanAsync(
        IReadOnlyCollection<Guid> loanIds,
        CancellationToken cancellationToken)
    {
        if (loanIds.Count == 0)
        {
            return EmptyPaid;
        }

        var rows = await db.StaffLoanPayments.AsNoTracking()
            .Where(payment => loanIds.Contains(payment.LoanId) && payment.ReversedAtUtc == null)
            .Select(payment => new { payment.LoanId, payment.Amount })
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(row => row.LoanId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Amount));
    }

    private static readonly Dictionary<Guid, decimal> EmptyPaid = [];

    private static IReadOnlyList<string> SuggestedLoanKinds(IEnumerable<string> used)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kind in PayrollLoanSchedule.DefaultKinds)
        {
            if (seen.Add(kind))
            {
                result.Add(kind);
            }
        }

        foreach (var kind in used.OrderBy(kind => kind, StringComparer.OrdinalIgnoreCase))
        {
            if (seen.Add(kind))
            {
                result.Add(kind);
            }
        }

        return result;
    }

    private static bool TryNormalizeLoan(
        string? kind,
        decimal perPeriodAmount,
        string? note,
        out string trimmedKind,
        out decimal perPeriod,
        out string? trimmedNote,
        out string? error)
    {
        trimmedKind = kind?.Trim() ?? "";
        trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        perPeriod = 0m;
        if (trimmedKind.Length == 0 || trimmedKind.Length > 80)
        {
            error = "Kind is required and must be 80 characters or fewer.";
            return false;
        }

        if (trimmedNote is { Length: > 500 })
        {
            error = "Note must be 500 characters or fewer.";
            return false;
        }

        perPeriod = RoundMoney(perPeriodAmount);
        if (perPeriod <= 0m)
        {
            error = "Per-period amount must be greater than zero.";
            return false;
        }

        error = null;
        return true;
    }

    private static string LoanLogValue(string kind, decimal principal, decimal perPeriod, DateOnly start) =>
        kind + " " + principal.ToString("0.00", CultureInfo.InvariantCulture)
        + " @ " + perPeriod.ToString("0.00", CultureInfo.InvariantCulture)
        + " from " + start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static StaffLoanStatus LoanStatus(StaffLoan row, decimal balance)
    {
        if (row.CancelledAtUtc is not null)
        {
            return StaffLoanStatus.Cancelled;
        }

        return balance <= 0m ? StaffLoanStatus.Paid : StaffLoanStatus.Active;
    }

    private static StaffLoanDto ToLoanDto(StaffLoan row, IReadOnlyDictionary<Guid, decimal> paid)
    {
        var paidAmount = RoundMoney(paid.GetValueOrDefault(row.Id));
        var balance = RoundMoney(row.Principal - paidAmount);
        return new StaffLoanDto(
            row.Id,
            row.StaffId,
            row.Kind,
            row.Principal,
            row.PerPeriodAmount,
            row.StartPeriod,
            row.Note,
            row.CreatedAtUtc,
            row.CancelledAtUtc,
            row.CancelReason,
            paidAmount,
            balance,
            LoanStatus(row, balance));
    }
}
