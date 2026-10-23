using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace HrCore.Data;

/// <summary>
/// The tables shared HR reads and writes. The host app's own DbContext implements this (its DbSet
/// properties already match by name) and registers it in DI, so HR and the app share one database,
/// one transaction and one set of migrations.
/// </summary>
public interface IHrDb
{
    DbSet<Organization> Organizations { get; }
    DbSet<Branch> Branches { get; }
    DbSet<Profile> Profiles { get; }
    DbSet<Staff> Staff { get; }
    DbSet<EmploymentDocument> EmploymentDocuments { get; }
    DbSet<LeaveAllotment> LeaveAllotments { get; }
    DbSet<LeaveRequest> LeaveRequests { get; }
    DbSet<AttendancePunch> AttendancePunches { get; }
    DbSet<OvertimeEntry> OvertimeEntries { get; }
    DbSet<PayrollDeduction> PayrollDeductions { get; }
    DbSet<PayrollThirteenthOverride> PayrollThirteenthOverrides { get; }
    DbSet<PayrollRun> PayrollRuns { get; }
    DbSet<PayrollRunLine> PayrollRunLines { get; }
    DbSet<ShopHoliday> ShopHolidays { get; }
    DbSet<PayrollAmountOverride> PayrollAmountOverrides { get; }
    DbSet<PayrollDayPayOverride> PayrollDayPayOverrides { get; }
    DbSet<StaffAllowance> StaffAllowances { get; }
    DbSet<StaffLoan> StaffLoans { get; }
    DbSet<StaffLoanPayment> StaffLoanPayments { get; }
    DbSet<PayrollRunLineItem> PayrollRunLineItems { get; }
    DbSet<StaffPayRate> StaffPayRates { get; }
    DbSet<HrChangeLog> HrChangeLogs { get; }
    DbSet<SettingsChangeLog> SettingsChangeLogs { get; }
    DbSet<Announcement> Announcements { get; }
    DbSet<IncentiveBonus> IncentiveBonuses { get; }

    DatabaseFacade Database { get; }

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
