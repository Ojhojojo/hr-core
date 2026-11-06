using HrCore.Data;
using HrCore.Domain;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Tests;

/// <summary>Stand-in host context. It calls <see cref="HrModel.Configure"/> the way Rakkii and Qurio do.</summary>
public sealed class TestHrDb(DbContextOptions<TestHrDb> options) : DbContext(options), IHrDb
{
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
        HrModel.Configure(modelBuilder);
    }
}
