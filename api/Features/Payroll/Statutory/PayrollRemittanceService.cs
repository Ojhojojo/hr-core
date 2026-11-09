using HrCore.Features.SettingsLog;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Payroll;

/// <summary>
/// Employer shares at lock, and the monthly remittance report.
/// The host flag gates the setting table. A shop with no enabled row keeps today's lock:
/// the three line columns stay null and payslips are unchanged.
/// </summary>
public sealed class PayrollRemittanceService(
    IHrDb db,
    HrCoreFeatures features,
    PayrollService payroll,
    SettingsChangeLogService settings)
{
    public const string NotReviewed = "Not reviewed";

    public const string ExportedMonthConfirmMessage =
        "This month has been exported. Employer shares are read-only.";

    public const string ExportedMonthVoidMessage =
        "This month has been exported. Voiding a run in an exported month is not available yet.";

    public const string ExportBlockedMessage =
        "Review every locked run in this month before exporting.";
    /// <summary>
    /// Null when the host flag is off or this shop has not turned remittance on.
    /// Otherwise one share per staff row. The caller stores those on the locked line.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, PayrollEmployerShare.Shares>?> ForRegisterAsync(
        Branch branch,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<PayrollStaffRowDto> periodRows,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        if (!features.EmployerRemittance || !await IsEnabledAsync(branch.Id, cancellationToken))
        {
            return null;
        }

        var monthStart = new DateOnly(to.Year, to.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        IReadOnlyList<PayrollStaffRowDto> monthRows;
        if (from == monthStart && to == monthEnd)
        {
            monthRows = periodRows;
        }
        else
        {
            var month = await payroll.GetRegisterAsync(
                [branch], monthStart, monthEnd, false, includeUnscopedOwners, cancellationToken);
            monthRows = month.Rows;
        }

        var schedule = new PayrollDeductionScheduleService(db, features);
        var (rules, matched, periodCount) = await schedule.ForPeriodAsync(branch.Id, from, to, cancellationToken);
        var allocation = rules
            .Select(rule => new PayrollEmployerShare.Rule(rule.DeductionKey, rule.Mode, rule.CutoffOrdinal))
            .ToList();

        return PayrollEmployerShare.Compute(
            from, to, periodRows, monthRows, allocation, matched, periodCount);
    }

    public async Task<RemittanceReportDto?> GetAsync(
        IReadOnlyList<Branch> shops,
        Guid branchId,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        if (!features.EmployerRemittance || shops.All(shop => shop.Id != branchId))
        {
            return null;
        }

        var monthText = MonthText(year, month);
        if (!await IsEnabledAsync(branchId, cancellationToken))
        {
            return new RemittanceReportDto(branchId, monthText, false, [], 0m, 0m, 0m);
        }

        var (agencies, unreviewed, empty) = await MonthLinesAsync(branchId, year, month, cancellationToken);
        return ToDto(branchId, monthText, agencies, empty, unreviewed);
    }

    /// <summary>
    /// CSV for a shop month, or the locked runs that still need review.
    /// A successful export records the month. A later export returns the same stored figures.
    /// </summary>
    public async Task<(PayrollCsvFile? File, IReadOnlyList<RemittanceRunRefDto>? Blocking, string? Error)> ExportAsync(
        IReadOnlyList<Branch> shops,
        Guid branchId,
        int year,
        int month,
        Guid? actorStaffId,
        CancellationToken cancellationToken)
    {
        var report = await GetAsync(shops, branchId, year, month, cancellationToken);
        if (report is null)
        {
            return (null, null, null);
        }

        if (!report.Enabled)
        {
            return (PayrollRemittance.File(year, month, []), null, null);
        }

        var blocking = report.UnreviewedRuns ?? [];
        if (blocking.Count > 0)
        {
            return (null, blocking, ExportBlockedMessage);
        }

        if (await MonthIsExportedAsync(branchId, new DateOnly(year, month, 1), cancellationToken)
            || report.Agencies.Count > 0 && report.EmptyMessage is null)
        {
            await RecordExportAsync(branchId, year, month, actorStaffId, cancellationToken);
        }

        var agencies = report.Agencies
            .Select(agency => new PayrollRemittance.Agency(
                agency.Key,
                agency.Label,
                agency.EmployeeShare,
                agency.EmployerShare))
            .ToList();
        return (PayrollRemittance.File(year, month, agencies), null, null);
    }

    public async Task<bool> MonthIsExportedAsync(
        Guid branchId,
        DateOnly periodEnd,
        CancellationToken cancellationToken)
    {
        if (!features.EmployerRemittance)
        {
            return false;
        }

        return await db.BranchRemittanceExports().AsNoTracking()
            .AnyAsync(
                row => row.BranchId == branchId && row.Year == periodEnd.Year && row.Month == periodEnd.Month,
                cancellationToken);
    }

    /// <summary>Void clears a review and puts employer shares back to null. No-op when the flag is off.</summary>
    public async Task ClearReviewAsync(PayrollRun run, CancellationToken cancellationToken)
    {
        if (!features.EmployerRemittance)
        {
            return;
        }

        run.EmployerSharesReviewedAtUtc = null;
        run.EmployerSharesReviewedBy = null;
        var lines = await db.PayrollRunLines
            .Where(line => line.RunId == run.Id)
            .ToListAsync(cancellationToken);
        foreach (var line in lines)
        {
            line.EmployerSss = null;
            line.EmployerPhilHealth = null;
            line.EmployerPagIbig = null;
        }
    }

    public async Task<EmployerShareReviewDto?> GetReviewAsync(
        IReadOnlyList<Branch> shops,
        Guid runId,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        if (!features.EmployerRemittance)
        {
            return null;
        }

        var run = await db.PayrollRuns.AsNoTracking()
            .Include(row => row.EmployerSharesReviewedByStaff)
            .ThenInclude(staff => staff!.Profile)
            .FirstOrDefaultAsync(row => row.Id == runId, cancellationToken);
        if (run is null || shops.All(shop => shop.Id != run.BranchId) || run.Status != PayrollRunStatus.Locked)
        {
            return null;
        }

        return await ReviewDtoAsync(shops, run, includeUnscopedOwners, cancellationToken);
    }

    public async Task<(EmployerShareReviewDto? Saved, Dictionary<string, string[]>? Errors, string? Conflict)> ConfirmAsync(
        IReadOnlyList<Branch> shops,
        Guid runId,
        ConfirmEmployerSharesRequest? request,
        Guid? actorStaffId,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        if (!features.EmployerRemittance)
        {
            return (null, null, null);
        }

        var run = await db.PayrollRuns
            .FirstOrDefaultAsync(row => row.Id == runId, cancellationToken);
        if (run is null || shops.All(shop => shop.Id != run.BranchId) || run.Status != PayrollRunStatus.Locked)
        {
            return (null, null, null);
        }

        if (!await IsEnabledAsync(run.BranchId, cancellationToken))
        {
            return (null, new Dictionary<string, string[]>
            {
                ["enabled"] = ["Turn the remittance report on for this shop."]
            }, null);
        }

        if (await MonthIsExportedAsync(run.BranchId, run.PeriodTo, cancellationToken))
        {
            return (null, null, ExportedMonthConfirmMessage);
        }

        var lines = await db.PayrollRunLines
            .Where(line => line.RunId == run.Id)
            .ToListAsync(cancellationToken);
        var posted = request?.Lines ?? [];
        var errors = new Dictionary<string, string[]>();
        if (posted.Count != lines.Count || posted.Select(line => line.StaffId).Distinct().Count() != lines.Count)
        {
            errors["lines"] = ["Send one share for every person on this run."];
        }

        foreach (var line in posted)
        {
            if (line.EmployerSss is null or < 0
                || line.EmployerPhilHealth is null or < 0
                || line.EmployerPagIbig is null or < 0)
            {
                errors["lines"] = ["Each employer share must be zero or more."];
                break;
            }
        }

        if (errors.Count > 0)
        {
            return (null, errors, null);
        }

        foreach (var line in lines)
        {
            var share = posted.FirstOrDefault(candidate => candidate.StaffId == line.StaffId);
            if (share is null)
            {
                return (null, new Dictionary<string, string[]>
                {
                    ["lines"] = ["Send one share for every person on this run."]
                }, null);
            }

            line.EmployerSss = Money(share.EmployerSss!.Value);
            line.EmployerPhilHealth = Money(share.EmployerPhilHealth!.Value);
            line.EmployerPagIbig = Money(share.EmployerPagIbig!.Value);
        }

        run.EmployerSharesReviewedAtUtc = DateTime.UtcNow;
        run.EmployerSharesReviewedBy = actorStaffId;
        await db.SaveChangesAsync(cancellationToken);
        var saved = await GetReviewAsync(shops, runId, includeUnscopedOwners, cancellationToken);
        return (saved, null, null);
    }

    public async Task<(RemittanceSettingDto? Saved, Dictionary<string, string[]>? Errors)> SetEnabledAsync(
        IReadOnlyList<Branch> shops,
        Guid branchId,
        UpdateRemittanceSettingRequest? request,
        Guid? actorStaffId,
        CancellationToken cancellationToken)
    {
        if (!features.EmployerRemittance || shops.All(shop => shop.Id != branchId))
        {
            return (null, null);
        }

        if (request?.Enabled is not bool enabled)
        {
            return (null, new Dictionary<string, string[]>
            {
                ["enabled"] = ["Say whether remittance is on."]
            });
        }

        var existing = await db.BranchRemittanceSettings()
            .FirstOrDefaultAsync(row => row.BranchId == branchId, cancellationToken);
        var before = existing is { Enabled: true } ? "on" : "off";
        if (existing is null)
        {
            existing = new BranchRemittanceSetting
            {
                Id = Guid.NewGuid(),
                BranchId = branchId
            };
            db.BranchRemittanceSettings().Add(existing);
        }

        existing.Enabled = enabled;
        settings.Record(
            actorStaffId,
            branchId,
            SettingsArea.PayrollRemittance,
            "enabled",
            before,
            enabled ? "on" : "off");
        await db.SaveChangesAsync(cancellationToken);
        return (new RemittanceSettingDto(branchId, existing.Enabled), null);
    }

    private async Task<bool> IsEnabledAsync(Guid branchId, CancellationToken cancellationToken) =>
        await db.BranchRemittanceSettings().AsNoTracking()
            .AnyAsync(row => row.BranchId == branchId && row.Enabled, cancellationToken);

    private async Task<(
        IReadOnlyList<PayrollRemittance.Agency> Agencies,
        IReadOnlyList<RemittanceRunRefDto> Unreviewed,
        string? Empty)> MonthLinesAsync(
        Guid branchId,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var runs = await db.PayrollRuns.AsNoTracking()
            .Where(run => run.BranchId == branchId
                && run.Status == PayrollRunStatus.Locked
                && run.PeriodTo >= monthStart
                && run.PeriodTo <= monthEnd)
            .Select(run => new
            {
                run.Id,
                run.PeriodFrom,
                run.PeriodTo,
                run.EmployerSharesReviewedAtUtc,
                Name = run.Branch.Name
            })
            .ToListAsync(cancellationToken);
        if (runs.Count == 0)
        {
            return (PayrollRemittance.Sum([]), [], PayrollRemittance.EmptyMessage);
        }

        var reviewed = runs
            .Where(run => run.EmployerSharesReviewedAtUtc is not null)
            .Select(run => run.Id)
            .ToHashSet();
        var unreviewed = runs
            .Where(run => run.EmployerSharesReviewedAtUtc is null)
            .Select(run => new RemittanceRunRefDto(
                run.Id,
                run.PeriodFrom,
                run.PeriodTo,
                run.Name,
                NotReviewed))
            .ToList();
        var runIds = runs.Select(run => run.Id).ToList();
        var stored = await db.PayrollRunLines.AsNoTracking()
            .Where(line => runIds.Contains(line.RunId))
            .ToListAsync(cancellationToken);
        var lines = stored.Select(line =>
        {
            var includeEmployer = reviewed.Contains(line.RunId);
            return new PayrollRemittance.Line(
                line.Sss,
                line.PhilHealth,
                line.PagIbig,
                includeEmployer ? line.EmployerSss : null,
                includeEmployer ? line.EmployerPhilHealth : null,
                includeEmployer ? line.EmployerPagIbig : null);
        });
        return (PayrollRemittance.Sum(lines), unreviewed, null);
    }

    private async Task RecordExportAsync(
        Guid branchId,
        int year,
        int month,
        Guid? actorStaffId,
        CancellationToken cancellationToken)
    {
        var existing = await db.BranchRemittanceExports()
            .FirstOrDefaultAsync(
                row => row.BranchId == branchId && row.Year == year && row.Month == month,
                cancellationToken);
        if (existing is null)
        {
            existing = new BranchRemittanceExport
            {
                Id = Guid.NewGuid(),
                BranchId = branchId,
                Year = year,
                Month = month
            };
            db.BranchRemittanceExports().Add(existing);
        }

        existing.ExportedAtUtc = DateTime.UtcNow;
        existing.ExportedBy = actorStaffId;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<EmployerShareReviewDto> ReviewDtoAsync(
        IReadOnlyList<Branch> shops,
        PayrollRun run,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        var branch = shops.First(shop => shop.Id == run.BranchId);
        var register = await payroll.GetRegisterAsync(
            [branch],
            run.PeriodFrom,
            run.PeriodTo,
            false,
            includeUnscopedOwners,
            cancellationToken);
        var quoted = await ForRegisterAsync(
            branch,
            run.PeriodFrom,
            run.PeriodTo,
            register.Rows,
            includeUnscopedOwners,
            cancellationToken);
        var stored = await db.PayrollRunLines.AsNoTracking()
            .Where(line => line.RunId == run.Id)
            .OrderBy(line => line.StaffName)
            .ToListAsync(cancellationToken);
        var lines = stored.Select(line =>
        {
            PayrollEmployerShare.Shares share = default;
            if (quoted is not null)
            {
                quoted.TryGetValue(line.StaffId, out share);
            }

            return new EmployerShareReviewLineDto(
                line.StaffId,
                line.StaffName,
                share.Sss,
                share.PhilHealth,
                share.PagIbig,
                line.EmployerSss,
                line.EmployerPhilHealth,
                line.EmployerPagIbig);
        }).ToList();
        return new EmployerShareReviewDto(
            run.Id,
            await MonthIsExportedAsync(run.BranchId, run.PeriodTo, cancellationToken),
            lines,
            run.EmployerSharesReviewedAtUtc,
            run.EmployerSharesReviewedByStaff?.Profile.DisplayName);
    }

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static RemittanceReportDto ToDto(
        Guid branchId,
        string month,
        IReadOnlyList<PayrollRemittance.Agency> agencies,
        string? emptyMessage,
        IReadOnlyList<RemittanceRunRefDto>? unreviewed = null)
    {
        var employee = PayrollRemittance.EmployeeTotal(agencies);
        var employer = PayrollRemittance.EmployerTotal(agencies);
        return new RemittanceReportDto(
            branchId,
            month,
            true,
            agencies.Select(agency => new RemittanceAgencyDto(
                agency.Key,
                agency.Label,
                agency.Employee,
                agency.Employer,
                agency.Total)).ToList(),
            employee,
            employer,
            employee + employer,
            emptyMessage,
            unreviewed is { Count: > 0 } ? unreviewed : null);
    }

    internal static string MonthText(int year, int month) =>
        year.ToString("0000") + "-" + month.ToString("00");
}
