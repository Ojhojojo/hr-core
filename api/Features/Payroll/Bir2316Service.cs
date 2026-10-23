using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Payroll;

/// <summary>
/// BIR 2316 data sheet source. Sums the frozen payroll lines of one employee for one calendar year.
/// Only locked runs count; void and draft runs are ignored. The sheet is a data summary for HR to copy
/// into the official form, not a filing.
/// </summary>
public sealed class Bir2316Service(IHrDb db)
{
    public const string MissingEmployeeTin = "employee_tin";
    public const string MissingEmployeeAddress = "employee_address";
    public const string MissingEmployeeZipCode = "employee_zip_code";
    public const string MissingEmployeeBirthDate = "employee_birth_date";
    public const string MissingEmployerTin = "employer_tin";
    public const string MissingEmployerAddress = "employer_address";

    /// <summary>Returns null when the person is not on the shop scope (404).</summary>
    public async Task<Bir2316Response?> GetAsync(
        IReadOnlyList<Branch> shops,
        Guid staffId,
        int year,
        CancellationToken cancellationToken)
    {
        var shopIds = shops.Select(s => s.Id).ToList();
        var staff = await db.Staff.AsNoTracking()
            .Include(s => s.Profile)
            .Include(s => s.Branch)
            .FirstOrDefaultAsync(s => s.Id == staffId, cancellationToken);
        if (staff?.BranchId is not Guid homeBranchId || !shopIds.Contains(homeBranchId) || staff.Branch is null)
        {
            return null;
        }

        var organization = await db.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == staff.Branch.OrganizationId, cancellationToken);

        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);
        var shopRuns = await db.PayrollRuns.AsNoTracking()
            .Include(run => run.Branch)
            .Where(run => shopIds.Contains(run.BranchId)
                && run.Status == PayrollRunStatus.Locked
                && run.PeriodFrom >= yearStart
                && run.PeriodFrom <= yearEnd)
            .ToListAsync(cancellationToken);

        var lines = await db.PayrollRunLines.AsNoTracking()
            .Where(line => line.StaffId == staffId
                && shopIds.Contains(line.Run.BranchId)
                && line.Run.Status == PayrollRunStatus.Locked
                && line.Run.PeriodFrom >= yearStart
                && line.Run.PeriodFrom <= yearEnd)
            .Select(line => new
            {
                line.RunId,
                line.Gross,
                line.Sss,
                line.PhilHealth,
                line.PagIbig,
                line.Wht
            })
            .ToListAsync(cancellationToken);

        var lineRunIds = lines.Select(line => line.RunId).ToHashSet();
        var includedRuns = shopRuns
            .Where(run => lineRunIds.Contains(run.Id))
            .OrderBy(run => run.PeriodFrom)
            .Select(run => new Bir2316IncludedRun(
                run.Id,
                run.PeriodFrom,
                run.PeriodTo,
                run.LockedAtUtc,
                run.Branch.Name))
            .ToList();

        var missingMonths = MissingMonths(
            shopRuns.Select(run => (run.PeriodFrom, run.PeriodTo)),
            year,
            staff.HireDate,
            staff.SeparationDate);

        var employerTin = Blank(organization?.Tin);
        var employerAddress = Blank(staff.Branch.Address);
        var employeeTin = Blank(staff.Tin);
        var employeeAddress = Blank(staff.RegisteredAddress);
        var employeeZip = Blank(staff.ZipCode);

        var missing = new List<string>();
        if (employeeTin is null)
        {
            missing.Add(MissingEmployeeTin);
        }

        if (employeeAddress is null)
        {
            missing.Add(MissingEmployeeAddress);
        }

        if (employeeZip is null)
        {
            missing.Add(MissingEmployeeZipCode);
        }

        if (staff.BirthDate is null)
        {
            missing.Add(MissingEmployeeBirthDate);
        }

        if (employerTin is null)
        {
            missing.Add(MissingEmployerTin);
        }

        if (employerAddress is null)
        {
            missing.Add(MissingEmployerAddress);
        }

        return new Bir2316Response(
            year,
            staff.Id,
            new Bir2316Employee(
                staff.Profile.DisplayName,
                employeeTin,
                employeeAddress,
                employeeZip,
                staff.BirthDate),
            new Bir2316Employer(
                organization?.LegalName ?? "",
                employerTin,
                employerAddress,
                staff.Branch.Name),
            new Bir2316Amounts(
                lines.Sum(line => line.Gross),
                lines.Sum(line => line.Sss),
                lines.Sum(line => line.PhilHealth),
                lines.Sum(line => line.PagIbig),
                lines.Sum(line => line.Wht),
                lines.Count),
            includedRuns,
            missingMonths,
            missing);
    }

    /// <summary>
    /// Months (1 to 12) of the year that the person was employed in but no locked run covers.
    /// A run covers each month its period touches. Months outside the employment window are skipped.
    /// </summary>
    public static IReadOnlyList<int> MissingMonths(
        IEnumerable<(DateOnly From, DateOnly To)> runPeriods,
        int year,
        DateOnly? hireDate,
        DateOnly? separationDate)
    {
        var covered = new bool[13];
        foreach (var (from, to) in runPeriods)
        {
            var first = from.Year == year ? from.Month : 1;
            var last = to.Year == year ? to.Month : 12;
            for (var month = Math.Max(1, first); month <= Math.Min(12, last); month++)
            {
                covered[month] = true;
            }
        }

        var missing = new List<int>();
        for (var month = 1; month <= 12; month++)
        {
            if (covered[month])
            {
                continue;
            }

            var monthStart = new DateOnly(year, month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var employed = (hireDate is null || hireDate <= monthEnd)
                && (separationDate is null || separationDate >= monthStart);
            if (employed)
            {
                missing.Add(month);
            }
        }

        return missing;
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record Bir2316Response(
    int Year,
    Guid StaffId,
    Bir2316Employee Employee,
    Bir2316Employer Employer,
    Bir2316Amounts Amounts,
    IReadOnlyList<Bir2316IncludedRun> Runs,
    IReadOnlyList<int> MissingMonths,
    IReadOnlyList<string> Missing);

public sealed record Bir2316Employee(
    string Name,
    string? Tin,
    string? RegisteredAddress,
    string? ZipCode,
    DateOnly? BirthDate);

/// <summary>Employer name is the organization legal name; address is the home shop address.</summary>
public sealed record Bir2316Employer(
    string LegalName,
    string? Tin,
    string? Address,
    string ShopName);

public sealed record Bir2316Amounts(
    decimal Gross,
    decimal Sss,
    decimal PhilHealth,
    decimal PagIbig,
    decimal Wht,
    int LineCount);

public sealed record Bir2316IncludedRun(
    Guid RunId,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    DateTime? LockedAtUtc,
    string ShopName);
