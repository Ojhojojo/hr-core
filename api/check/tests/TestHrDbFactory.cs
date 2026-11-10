using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HrCore.Tests;

/// <summary>
/// Design-time context for <c>dotnet ef migrations has-pending-model-changes</c>.
/// Flags stay off unless the matching environment variable is <c>1</c>.
/// </summary>
public sealed class TestHrDbFactory : IDesignTimeDbContextFactory<TestHrDb>
{
    public TestHrDb CreateDbContext(string[] args)
    {
        var features = new HrCoreFeatures
        {
            PayrollSchedules = On("HR_PAYROLL_SCHEDULES"),
            EmployerRemittance = On("HR_REMITTANCE"),
            LoanKinds = On("HR_LOAN_KINDS"),
            PayoutHold = On("HR_PAYOUT_HOLD")
        };
        var built = new DbContextOptionsBuilder<TestHrDb>()
            .UseNpgsql("Host=127.0.0.1;Database=hr_design;Username=hr;Password=hr")
            .UseHrCoreFeatures(features);
        return new TestHrDb((DbContextOptions<TestHrDb>)built.Options);
    }

    private static bool On(string name) => Environment.GetEnvironmentVariable(name) == "1";
}
