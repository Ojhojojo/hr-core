using System.Globalization;

namespace HrCore.Features.Payroll;

/// <summary>
/// Totals for the monthly remittance report. Employee shares are the amounts withheld on
/// locked lines. Employer shares are the amounts stored at lock; a null is zero.
/// </summary>
public static class PayrollRemittance
{
    public const string EmptyMessage = "There is nothing to remit yet.";

    public readonly record struct Line(
        decimal Sss,
        decimal PhilHealth,
        decimal PagIbig,
        decimal? EmployerSss,
        decimal? EmployerPhilHealth,
        decimal? EmployerPagIbig);

    public readonly record struct Agency(string Key, string Label, decimal Employee, decimal Employer)
    {
        public decimal Total => Employee + Employer;
    }

    public static IReadOnlyList<Agency> Sum(IEnumerable<Line> lines)
    {
        decimal sssEmployee = 0m;
        decimal sssEmployer = 0m;
        decimal philEmployee = 0m;
        decimal philEmployer = 0m;
        decimal pagEmployee = 0m;
        decimal pagEmployer = 0m;
        foreach (var line in lines)
        {
            sssEmployee += line.Sss;
            sssEmployer += line.EmployerSss ?? 0m;
            philEmployee += line.PhilHealth;
            philEmployer += line.EmployerPhilHealth ?? 0m;
            pagEmployee += line.PagIbig;
            pagEmployer += line.EmployerPagIbig ?? 0m;
        }

        return
        [
            new Agency("sss", "SSS", sssEmployee, sssEmployer),
            new Agency("philhealth", "PhilHealth", philEmployee, philEmployer),
            new Agency("pagibig", "Pag-IBIG", pagEmployee, pagEmployer)
        ];
    }

    public static decimal EmployeeTotal(IReadOnlyList<Agency> agencies)
    {
        decimal total = 0m;
        foreach (var agency in agencies)
        {
            total += agency.Employee;
        }

        return total;
    }

    public static decimal EmployerTotal(IReadOnlyList<Agency> agencies)
    {
        decimal total = 0m;
        foreach (var agency in agencies)
        {
            total += agency.Employer;
        }

        return total;
    }

    /// <summary>
    /// CSV of the same agencies the screen lists, plus a total row.
    /// Amounts are 0.00 with a dot, no thousands separator.
    /// </summary>
    public static string Csv(IReadOnlyList<Agency> agencies)
    {
        var lines = new List<string>
        {
            PayrollService.JoinCsv("Agency", "Employee share", "Employer share", "Total")
        };
        foreach (var agency in agencies)
        {
            lines.Add(PayrollService.JoinCsv(
                agency.Label,
                Money(agency.Employee),
                Money(agency.Employer),
                Money(agency.Total)));
        }

        var employee = EmployeeTotal(agencies);
        var employer = EmployerTotal(agencies);
        lines.Add(PayrollService.JoinCsv("Total", Money(employee), Money(employer), Money(employee + employer)));
        return string.Join("\n", lines) + "\n";
    }

    public static PayrollCsvFile File(int year, int month, IReadOnlyList<Agency> agencies)
    {
        var name = "payroll-remittance-" +
            year.ToString("0000", CultureInfo.InvariantCulture) +
            "-" +
            month.ToString("00", CultureInfo.InvariantCulture) +
            ".csv";
        return new PayrollCsvFile(name, System.Text.Encoding.UTF8.GetBytes(Csv(agencies)));
    }

    private static string Money(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);
}
