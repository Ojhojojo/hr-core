using HrCore.Data;
using HrCore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HrCore.Tests;

/// <summary>
/// Stand-in host context. It calls <see cref="HrModel.Configure"/> the way Rakkii and Qurio do.
/// Payroll schedule tables are included only when the options carry
/// <see cref="HrCoreFeatures.PayrollSchedules"/>. The remittance setting is included only when
/// <see cref="HrCoreFeatures.EmployerRemittance"/> is on. <see cref="HrCoreFeatures.PayoutHold"/>
/// adds the hold table. There is no <c>DbSet</c> for those types, so a flag-off context does not
/// discover them.
/// </summary>
public sealed class TestHrDb(DbContextOptions<TestHrDb> options) : DbContext(options), IHrDb
{
    private readonly HrCoreFeatures features =
        options.FindExtension<HrCoreFeaturesExtension>()?.Features ?? new HrCoreFeatures();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<Staff> Staff => Set<Staff>();
    public DbSet<EmploymentDocument> EmploymentDocuments => Set<EmploymentDocument>();
    public DbSet<LeaveAllotment> LeaveAllotments => Set<LeaveAllotment>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<AttendancePunch> AttendancePunches => Set<AttendancePunch>();
    public DbSet<OvertimeEntry> OvertimeEntries => Set<OvertimeEntry>();
    public DbSet<PayrollDeduction> PayrollDeductions => Set<PayrollDeduction>();
    public DbSet<PayrollThirteenthOverride> PayrollThirteenthOverrides => Set<PayrollThirteenthOverride>();
    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
    public DbSet<PayrollRunLine> PayrollRunLines => Set<PayrollRunLine>();
    public DbSet<ShopHoliday> ShopHolidays => Set<ShopHoliday>();
    public DbSet<PayrollAmountOverride> PayrollAmountOverrides => Set<PayrollAmountOverride>();
    public DbSet<PayrollDayPayOverride> PayrollDayPayOverrides => Set<PayrollDayPayOverride>();
    public DbSet<StaffAllowance> StaffAllowances => Set<StaffAllowance>();
    public DbSet<StaffLoan> StaffLoans => Set<StaffLoan>();
    public DbSet<StaffLoanPayment> StaffLoanPayments => Set<StaffLoanPayment>();
    public DbSet<PayrollRunLineItem> PayrollRunLineItems => Set<PayrollRunLineItem>();
    public DbSet<StaffPayRate> StaffPayRates => Set<StaffPayRate>();
    public DbSet<HrChangeLog> HrChangeLogs => Set<HrChangeLog>();
    public DbSet<SettingsChangeLog> SettingsChangeLogs => Set<SettingsChangeLog>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<IncentiveBonus> IncentiveBonuses => Set<IncentiveBonus>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        HrModel.Configure(modelBuilder, features);
    }
}

/// <summary>Passes <see cref="HrCoreFeatures"/> into <see cref="TestHrDb"/> without a <c>DbSet</c>.</summary>
public sealed class HrCoreFeaturesExtension : IDbContextOptionsExtension
{
    public HrCoreFeaturesExtension(HrCoreFeatures features) => Features = features;

    public HrCoreFeatures Features { get; }

    private DbContextOptionsExtensionInfo? info;

    public DbContextOptionsExtensionInfo Info => info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(HrCoreFeaturesExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment =>
            (extension.Features.PayrollSchedules ? "PayrollSchedules " : "")
            + (extension.Features.EmployerRemittance ? "EmployerRemittance " : "")
            + (extension.Features.LoanKinds ? "LoanKinds " : "")
            + (extension.Features.PayoutHold ? "PayoutHold " : "");

        public override int GetServiceProviderHashCode() =>
            (extension.Features.PayrollSchedules ? 1 : 0)
            + (extension.Features.EmployerRemittance ? 2 : 0)
            + (extension.Features.LoanKinds ? 4 : 0)
            + (extension.Features.PayoutHold ? 8 : 0);

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo otherInfo
            && otherInfo.GetServiceProviderHashCode() == GetServiceProviderHashCode();

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["HrCore:PayrollSchedules"] = extension.Features.PayrollSchedules.ToString();
            debugInfo["HrCore:EmployerRemittance"] = extension.Features.EmployerRemittance.ToString();
            debugInfo["HrCore:LoanKinds"] = extension.Features.LoanKinds.ToString();
            debugInfo["HrCore:PayoutHold"] = extension.Features.PayoutHold.ToString();
        }
    }
}

public static class HrCoreFeaturesTestExtensions
{
    public static DbContextOptionsBuilder UseHrCoreFeatures(
        this DbContextOptionsBuilder builder,
        HrCoreFeatures features)
    {
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(new HrCoreFeaturesExtension(features));
        return builder;
    }
}
