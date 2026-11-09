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

        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var runIds = await db.PayrollRuns.AsNoTracking()
            .Where(run => run.BranchId == branchId
                && run.Status == PayrollRunStatus.Locked
                && run.PeriodTo >= monthStart
                && run.PeriodTo <= monthEnd)
            .Select(run => run.Id)
            .ToListAsync(cancellationToken);
        if (runIds.Count == 0)
        {
            return ToDto(branchId, monthText, PayrollRemittance.Sum([]), PayrollRemittance.EmptyMessage);
        }

        var stored = await db.PayrollRunLines.AsNoTracking()
            .Where(line => runIds.Contains(line.RunId))
            .ToListAsync(cancellationToken);
        var lines = stored.Select(line => new PayrollRemittance.Line(
            line.Sss,
            line.PhilHealth,
            line.PagIbig,
            line.EmployerSss,
            line.EmployerPhilHealth,
            line.EmployerPagIbig));
        return ToDto(branchId, monthText, PayrollRemittance.Sum(lines), null);
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

    private static RemittanceReportDto ToDto(
        Guid branchId,
        string month,
        IReadOnlyList<PayrollRemittance.Agency> agencies,
        string? emptyMessage)
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
            emptyMessage);
    }

    internal static string MonthText(int year, int month) =>
        year.ToString("0000") + "-" + month.ToString("00");
}
