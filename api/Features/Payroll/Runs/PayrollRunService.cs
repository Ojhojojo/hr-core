using HrCore.Features.HrChangeLog;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Payroll;

/// <summary>
/// Locked payroll runs. Locking freezes the register for one shop and period into
/// <see cref="PayrollRunLine"/> rows. Runs are never edited or deleted; a mistake is voided and
/// corrected in the next period.
/// </summary>
public sealed class PayrollRunService(IHrDb db, HrChangeLogService hr, PayrollService payroll)
{
    public const string RefType = "payroll_run";

    public const string LockedPeriodMessage =
        "This period is locked. Post an adjustment in the next period.";

    public const string DuplicateMessage =
        "This shop and period already has a payroll run.";

    public const string OverlapMessage =
        "Another locked payroll run overlaps this period.";

    public const int MaxNoteLength = 500;

    /// <summary>Runs for the shop scope, newest period first. Optional from/to filter by overlap.</summary>
    public async Task<IReadOnlyList<PayrollRunSummaryDto>> ListAsync(
        IReadOnlyList<Branch> shops,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var shopIds = shops.Select(s => s.Id).ToList();
        var query = RunsQuery().Where(run => shopIds.Contains(run.BranchId));
        if (from is DateOnly start)
        {
            query = query.Where(run => run.PeriodTo >= start);
        }

        if (to is DateOnly end)
        {
            query = query.Where(run => run.PeriodFrom <= end);
        }

        var runs = await query
            .OrderByDescending(run => run.PeriodFrom)
            .ThenBy(run => run.Branch!.Name)
            .ToListAsync(cancellationToken);
        var lineCounts = await LineCountsAsync(runs.Select(run => run.Id).ToList(), cancellationToken);
        return runs.Select(run => ToSummary(run, lineCounts.GetValueOrDefault(run.Id))).ToList();
    }

    /// <summary>One run with its frozen lines, or null when it is outside the shop scope.</summary>
    public async Task<PayrollRunDetailDto?> GetRunAsync(
        IReadOnlyList<Branch> shops,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var run = await RunsQuery().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null || !shops.Any(s => s.Id == run.BranchId))
        {
            return null;
        }

        var lines = await db.PayrollRunLines.AsNoTracking()
            .Where(line => line.RunId == runId)
            .OrderBy(line => line.StaffName)
            .ToListAsync(cancellationToken);
        var lineIds = lines.Select(line => line.Id).ToList();
        var allowanceItems = await PayrollService.LoadLockedAllowanceItemsAsync(db, lineIds, cancellationToken);
        var loanItems = await PayrollService.LoadLockedLoanItemsAsync(db, lineIds, cancellationToken);
        return new PayrollRunDetailDto(
            ToSummary(run, lines.Count),
            lines.Select(line => ToLineDto(
                line,
                allowanceItems.GetValueOrDefault(line.Id),
                loanItems.GetValueOrDefault(line.Id))).ToList());
    }

    /// <summary>
    /// Freezes the live register for one shop and period. Returns an error string when the period
    /// already has a run or overlaps a locked one; returns null when the shop is out of scope.
    /// </summary>
    /// <summary>
    /// Locks a period. The shop row is locked first, inside one transaction, so two locks of the same
    /// period cannot both pass the duplicate and overlap checks. The second one waits, then sees the
    /// first run and is refused.
    /// </summary>
    public async Task<(PayrollRunDetailDto? Run, string? Error)> LockAsync(
        IReadOnlyList<Branch> shops,
        Guid branchId,
        DateOnly from,
        DateOnly to,
        string? note,
        Guid? actorStaffId,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var result = await LockRunAsync(shops, branchId, from, to, note, actorStaffId, includeUnscopedOwners, cancellationToken);
        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        return result;
    }

    private async Task<(PayrollRunDetailDto? Run, string? Error)> LockRunAsync(
        IReadOnlyList<Branch> shops,
        Guid branchId,
        DateOnly from,
        DateOnly to,
        string? note,
        Guid? actorStaffId,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        var branch = shops.FirstOrDefault(s => s.Id == branchId);
        if (branch is null)
        {
            return (null, null);
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Branches\" WHERE \"Id\" = {branchId} FOR UPDATE",
            cancellationToken);

        if (note is { Length: > MaxNoteLength })
        {
            return (null, $"Note must be {MaxNoteLength} characters or fewer.");
        }

        var (start, end, _, _) = PayrollService.ResolvePeriod(from, to);
        var duplicate = await db.PayrollRuns.AsNoTracking()
            .AnyAsync(
                r => r.BranchId == branchId && r.PeriodFrom == start && r.PeriodTo == end,
                cancellationToken);
        if (duplicate)
        {
            return (null, DuplicateMessage);
        }

        var overlaps = await db.PayrollRuns.AsNoTracking()
            .AnyAsync(
                r => r.BranchId == branchId
                     && r.Status == PayrollRunStatus.Locked
                     && r.PeriodFrom <= end
                     && r.PeriodTo >= start,
                cancellationToken);
        if (overlaps)
        {
            return (null, OverlapMessage);
        }

        var register = await payroll.GetRegisterAsync(
            [branch], start, end, includeInactive: false, includeUnscopedOwners, cancellationToken);

        var now = DateTime.UtcNow;
        var run = new PayrollRun
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            PeriodFrom = start,
            PeriodTo = end,
            Status = PayrollRunStatus.Locked,
            Note = BlankToNull(note),
            TotalGross = register.TotalAmount,
            TotalDeductions = register.Rows.Sum(row => row.Deductions),
            TotalNet = register.TotalNet,
            CreatedAtUtc = now,
            CreatedByStaffId = actorStaffId,
            LockedAtUtc = now,
            LockedByStaffId = actorStaffId
        };
        db.PayrollRuns.Add(run);

        foreach (var row in register.Rows)
        {
            var lineId = Guid.NewGuid();
            db.PayrollRunLines.Add(new PayrollRunLine
            {
                Id = lineId,
                RunId = run.Id,
                StaffId = row.StaffId,
                StaffName = row.StaffName,
                BranchId = row.BranchId,
                Basis = row.Basis,
                RatePhp = row.RatePhp,
                RegularHours = row.RegularHours,
                OtHours = row.OtHours,
                OtPay = row.OtPay,
                DaysPaid = row.DaysPaid,
                Gross = row.Amount,
                Sss = row.Sss,
                PhilHealth = row.PhilHealth,
                PagIbig = row.PagIbig,
                Wht = row.Wht,
                Other = row.Other,
                Deductions = row.Deductions,
                Net = row.Net,
                Notes = row.Notes,
                HolidayHours = row.HolidayHours,
                RestDayHours = row.RestDayHours,
                NightHours = row.NightHours,
                PremiumPay = row.PremiumPay,
                Allowances = row.Allowances,
                LoanDeductions = row.LoanDeductions
            });
            if (row.AllowanceItems is { Count: > 0 } items)
            {
                foreach (var item in items)
                {
                    db.PayrollRunLineItems.Add(new PayrollRunLineItem
                    {
                        Id = Guid.NewGuid(),
                        LineId = lineId,
                        Kind = PayrollAllowanceCalculator.ItemKind,
                        Label = item.Name,
                        Amount = item.Amount,
                        Taxable = item.Taxable,
                        RefId = item.AllowanceId,
                        Quantity = item.Quantity
                    });
                }
            }

            if (row.LoanItems is { Count: > 0 } loanItems)
            {
                foreach (var item in loanItems)
                {
                    db.PayrollRunLineItems.Add(new PayrollRunLineItem
                    {
                        Id = Guid.NewGuid(),
                        LineId = lineId,
                        Kind = PayrollLoanSchedule.ItemKind,
                        Label = item.Kind,
                        Amount = item.Amount,
                        RefId = item.LoanId,
                        BalanceAfter = item.BalanceAfter
                    });
                    if (item.Amount > 0m)
                    {
                        db.StaffLoanPayments.Add(new StaffLoanPayment
                        {
                            Id = Guid.NewGuid(),
                            LoanId = item.LoanId,
                            RunId = run.Id,
                            StaffId = row.StaffId,
                            PeriodFrom = start,
                            PeriodTo = end,
                            Amount = item.Amount,
                            CreatedAtUtc = now
                        });
                    }
                }
            }
        }

        // Every bonus paid on this run goes Paid in the same transaction as the lock. Only bonuses that
        // were scheduled for exactly this period and are in the frozen register are marked.
        var paidBonusIds = register.Rows
            .SelectMany(row => row.AllowanceItems ?? [])
            .Select(item => item.AllowanceId)
            .OfType<Guid>()
            .ToList();
        if (paidBonusIds.Count > 0)
        {
            var bonuses = await db.IncentiveBonuses
                .Where(bonus => paidBonusIds.Contains(bonus.Id)
                    && bonus.Status == IncentiveBonusStatus.Scheduled
                    && bonus.PayPeriodFrom >= start
                    && bonus.PayPeriodTo <= end)
                .ToListAsync(cancellationToken);
            foreach (var bonus in bonuses)
            {
                bonus.Status = IncentiveBonusStatus.Paid;
                bonus.PaidRunId = run.Id;
                bonus.PaidAtUtc = now;
            }
        }

        // One audit row for the lock. The subject is the actor: the run itself owns no staff.
        if (actorStaffId is Guid actor)
        {
            hr.Record(
                actor,
                actor,
                HrChangeField.PayrollRunLock,
                null,
                Period(start, end),
                start,
                end,
                refType: RefType,
                refId: run.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
        return (await GetRunAsync(shops, run.Id, cancellationToken), null);
    }

    /// <summary>Marks a run void. The row and its lines stay; a later period carries the fix.</summary>
    public async Task<PayrollRunDetailDto?> VoidAsync(
        IReadOnlyList<Branch> shops,
        Guid runId,
        Guid? actorStaffId,
        CancellationToken cancellationToken)
    {
        var run = await db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null || !shops.Any(s => s.Id == run.BranchId))
        {
            return null;
        }

        var openPayments = await db.StaffLoanPayments
            .Where(payment => payment.RunId == run.Id && payment.ReversedAtUtc == null)
            .ToListAsync(cancellationToken);
        if (run.Status != PayrollRunStatus.Void)
        {
            var now = DateTime.UtcNow;
            run.Status = PayrollRunStatus.Void;
            run.VoidedAtUtc = now;
            run.VoidedByStaffId = actorStaffId;

            // Bonuses paid on this run go back to Scheduled, so the next run for their period can pay them.
            var returnedBonuses = await db.IncentiveBonuses
                .Where(bonus => bonus.PaidRunId == run.Id && bonus.Status == IncentiveBonusStatus.Paid)
                .ToListAsync(cancellationToken);
            foreach (var bonus in returnedBonuses)
            {
                bonus.Status = IncentiveBonusStatus.Scheduled;
                bonus.PaidRunId = null;
                bonus.PaidAtUtc = null;
            }
            if (actorStaffId is Guid actor)
            {
                hr.Record(
                    actor,
                    actor,
                    HrChangeField.PayrollRunVoid,
                    null,
                    Period(run.PeriodFrom, run.PeriodTo),
                    run.PeriodFrom,
                    run.PeriodTo,
                    refType: RefType,
                    refId: run.Id);
            }

            foreach (var payment in openPayments)
            {
                payment.ReversedAtUtc = now;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        else if (openPayments.Count > 0)
        {
            var now = run.VoidedAtUtc ?? DateTime.UtcNow;
            foreach (var payment in openPayments)
            {
                payment.ReversedAtUtc = now;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return await GetRunAsync(shops, runId, cancellationToken);
    }

    /// <summary>
    /// Shared with the register so a live read and a locked read use one lookup. Only a
    /// single-shop scope can match, because a run belongs to exactly one shop.
    /// </summary>
    internal static async Task<PayrollRun?> FindExactLockedRunAsync(
        IHrDb db,
        IReadOnlyList<Branch> shops,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        if (shops.Count != 1)
        {
            return null;
        }

        var branchId = shops[0].Id;
        return await db.PayrollRuns.AsNoTracking()
            .Include(run => run.Branch)
            .Include(run => run.LockedByStaff)
            .ThenInclude(staff => staff!.Profile)
            .FirstOrDefaultAsync(
                run => run.BranchId == branchId
                       && run.Status == PayrollRunStatus.Locked
                       && run.PeriodFrom == from
                       && run.PeriodTo == to,
                cancellationToken);
    }

    /// <summary>True when a locked run covers this worker's shop and overlaps [from, to].</summary>
    public Task<bool> IsPeriodLockedAsync(
        Guid staffId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken) =>
        IsPeriodLockedAsync(db, staffId, from, to, cancellationToken);

    internal static async Task<bool> IsPeriodLockedAsync(
        IHrDb db,
        Guid staffId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            (from, to) = (to, from);
        }

        var branchId = await db.Staff.AsNoTracking()
            .Where(staff => staff.Id == staffId)
            .Select(staff => staff.BranchId)
            .FirstOrDefaultAsync(cancellationToken);
        if (branchId is not Guid branch)
        {
            return false;
        }

        return await db.PayrollRuns.AsNoTracking()
            .AnyAsync(
                run => run.BranchId == branch
                       && run.Status == PayrollRunStatus.Locked
                       && run.PeriodFrom <= to
                       && run.PeriodTo >= from,
                cancellationToken);
    }

    /// <summary>
    /// The caller's own payslip history: lines from locked runs only (draft and void excluded),
    /// newest period first. Unscoped owners have no lines, so the list is empty.
    /// </summary>
    public async Task<IReadOnlyList<MyPayslipSummaryDto>> ListMyPayslipsAsync(
        Guid staffId,
        CancellationToken cancellationToken)
    {
        return await db.PayrollRunLines.AsNoTracking()
            .Where(line => line.StaffId == staffId && line.Run!.Status == PayrollRunStatus.Locked)
            .OrderByDescending(line => line.Run!.PeriodTo)
            .ThenByDescending(line => line.Run!.PeriodFrom)
            .Select(line => new MyPayslipSummaryDto(
                line.RunId,
                line.Id,
                line.Run!.PeriodFrom,
                line.Run!.PeriodTo,
                line.Gross,
                line.Deductions,
                line.Net,
                line.Run.LockedAtUtc ?? line.Run.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// One saved payslip line exactly as stored (not recalculated), with its run period and its
    /// frozen line items. 403 when the line belongs to another worker; 404 when it is missing or
    /// its run is not locked.
    /// </summary>
    public async Task<MyPayslipLookup> GetMyPayslipAsync(
        Guid staffId,
        Guid lineId,
        CancellationToken cancellationToken)
    {
        var line = await db.PayrollRunLines.AsNoTracking()
            .Include(l => l.Run)
            .Include(l => l.Run!.LockedByStaff)
            .ThenInclude(staff => staff!.Profile)
            .FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null || line.Run is null || line.Run.Status != PayrollRunStatus.Locked)
        {
            return MyPayslipLookup.NotFound();
        }

        if (line.StaffId != staffId)
        {
            return MyPayslipLookup.Forbidden();
        }

        var items = await db.PayrollRunLineItems.AsNoTracking()
            .Where(item => item.LineId == lineId)
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.Label)
            .ThenBy(item => item.Id)
            .Select(item => new PayrollRunLineItemDto(
                item.Id,
                item.Kind,
                item.Label,
                item.Amount,
                item.Taxable,
                item.RefId,
                item.Quantity,
                item.BalanceAfter))
            .ToListAsync(cancellationToken);

        var run = line.Run;
        return MyPayslipLookup.Ok(new MyPayslipDetailDto(
            run.Id,
            line.Id,
            run.PeriodFrom,
            run.PeriodTo,
            run.LockedAtUtc ?? run.CreatedAtUtc,
            run.LockedByStaff?.Profile.DisplayName,
            line.StaffId,
            line.StaffName,
            line.BranchId,
            line.Basis,
            line.RatePhp,
            line.RegularHours,
            line.OtHours,
            line.OtPay,
            line.DaysPaid,
            line.Gross,
            line.Sss,
            line.PhilHealth,
            line.PagIbig,
            line.Wht,
            line.Other,
            line.Deductions,
            line.Net,
            line.Notes,
            line.HolidayHours,
            line.RestDayHours,
            line.NightHours,
            line.PremiumPay,
            line.Allowances,
            line.LoanDeductions,
            items));
    }

    internal static string Period(DateOnly from, DateOnly to) =>
        $"{from:yyyy-MM-dd}..{to:yyyy-MM-dd}";

    private IQueryable<PayrollRun> RunsQuery() =>
        db.PayrollRuns.AsNoTracking()
            .Include(run => run.Branch)
            .Include(run => run.CreatedByStaff)
            .ThenInclude(staff => staff!.Profile)
            .Include(run => run.LockedByStaff)
            .ThenInclude(staff => staff!.Profile)
            .Include(run => run.VoidedByStaff)
            .ThenInclude(staff => staff!.Profile);

    private async Task<Dictionary<Guid, int>> LineCountsAsync(
        IReadOnlyList<Guid> runIds,
        CancellationToken cancellationToken)
    {
        if (runIds.Count == 0)
        {
            return [];
        }

        return await db.PayrollRunLines.AsNoTracking()
            .Where(line => runIds.Contains(line.RunId))
            .GroupBy(line => line.RunId)
            .Select(group => new { RunId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.RunId, row => row.Count, cancellationToken);
    }

    private static PayrollRunSummaryDto ToSummary(PayrollRun run, int lineCount) =>
        new(
            run.Id,
            run.BranchId,
            run.Branch?.Name,
            run.PeriodFrom,
            run.PeriodTo,
            run.Status,
            run.Note,
            run.TotalGross,
            run.TotalDeductions,
            run.TotalNet,
            lineCount,
            run.CreatedAtUtc,
            run.CreatedByStaff?.Profile.DisplayName,
            run.LockedAtUtc,
            run.LockedByStaff?.Profile.DisplayName,
            run.VoidedAtUtc,
            run.VoidedByStaff?.Profile.DisplayName);

    private static PayrollRunLineDto ToLineDto(
        PayrollRunLine line,
        IReadOnlyList<PayrollAllowanceItemDto>? allowanceItems,
        IReadOnlyList<PayrollLoanItemDto>? loanItems) =>
        new(
            line.StaffId,
            line.StaffName,
            line.BranchId,
            line.Basis,
            line.RatePhp,
            line.RegularHours,
            line.OtHours,
            line.DaysPaid,
            line.Gross,
            line.Sss,
            line.PhilHealth,
            line.PagIbig,
            line.Wht,
            line.Other,
            line.Deductions,
            line.Net,
            line.Notes,
            line.HolidayHours,
            line.RestDayHours,
            line.NightHours,
            line.PremiumPay,
            line.Allowances,
            allowanceItems is { Count: > 0 } ? allowanceItems : null,
            line.LoanDeductions,
            loanItems is { Count: > 0 } ? loanItems : null,
            line.OtPay);

    private static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
