using HrCore.Features.Payroll;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace HrCore.Tests;

/// <summary>
/// Locked-period guard used by overtime file/decide and owner-recorded leave (rakkii-cars#959).
/// </summary>
public class PayrollPeriodLockTests
{
    [Fact]
    public async Task IsPeriodLocked_is_true_when_a_locked_run_covers_the_work_date()
    {
        await using var db = new FakeHrDb();
        var staffId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        db.Staff.Add(new Staff
        {
            Id = staffId,
            ProfileId = Guid.NewGuid(),
            BranchId = branchId,
            Role = StaffRole.Admin,
            Active = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            PeriodFrom = new DateOnly(2026, 9, 14),
            PeriodTo = new DateOnly(2026, 9, 20),
            Status = PayrollRunStatus.Locked,
            CreatedAtUtc = DateTime.UtcNow,
            LockedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var locked = await PayrollRunService.IsPeriodLockedAsync(
            db, staffId, new DateOnly(2026, 9, 17), new DateOnly(2026, 9, 17), CancellationToken.None);

        Assert.True(locked);
    }

    [Fact]
    public async Task IsPeriodLocked_is_false_outside_the_locked_period()
    {
        await using var db = new FakeHrDb();
        var staffId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        db.Staff.Add(new Staff
        {
            Id = staffId,
            ProfileId = Guid.NewGuid(),
            BranchId = branchId,
            Role = StaffRole.Admin,
            Active = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            PeriodFrom = new DateOnly(2026, 9, 14),
            PeriodTo = new DateOnly(2026, 9, 20),
            Status = PayrollRunStatus.Locked,
            CreatedAtUtc = DateTime.UtcNow,
            LockedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var locked = await PayrollRunService.IsPeriodLockedAsync(
            db, staffId, new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 21), CancellationToken.None);

        Assert.False(locked);
    }

    [Fact]
    public void Locked_period_refusal_message_is_stable()
    {
        // Overtime file/decide and CreateForStaff leave return this string on 409 Conflict.
        Assert.Equal(
            "This period is locked. Post an adjustment in the next period.",
            PayrollRunService.LockedPeriodMessage);
    }

    /// <summary>Minimal in-memory IHrDb for lock-overlap checks.</summary>
    private sealed class FakeHrDb : DbContext, IHrDb
    {
        public FakeHrDb()
            : base(new DbContextOptionsBuilder<FakeHrDb>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options)
        {
        }

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

        DatabaseFacade IHrDb.Database => Database;

        EntityEntry<TEntity> IHrDb.Entry<TEntity>(TEntity entity) => Entry(entity);
    }
}
