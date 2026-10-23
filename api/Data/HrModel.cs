using Microsoft.EntityFrameworkCore;

namespace HrCore.Data;

/// <summary>
/// Table setup for the platform and shared HR. The host app's DbContext calls <see cref="Configure"/>
/// from OnModelCreating, after its own conventions, and then configures its own entities (it may add
/// more settings to Branch, Organization and Staff for the fields it owns).
/// </summary>
public static class HrModel
{
    private static readonly HashSet<string> MoneyNames = new(StringComparer.Ordinal)
    {
        nameof(StaffAllowance.Amount),
        nameof(PayrollRunLineItem.Quantity),
        nameof(PayrollRunLineItem.BalanceAfter),
        nameof(StaffPayRate.RatePhp)
    };

    public static void Configure(ModelBuilder modelBuilder)
    {
        ApplyConventions(modelBuilder);

        modelBuilder.Entity<Organization>(e =>
        {
            e.Property(x => x.LegalName).HasMaxLength(200).IsRequired();
            e.Property(x => x.TradingName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Tin).HasMaxLength(40);
            e.Property(x => x.DemoPublicEnabled).HasDefaultValue(false);
        });

        modelBuilder.Entity<Branch>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => new { x.OrganizationId, x.Code }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Kind).HasDefaultValue(BranchKind.Owned);
            e.HasOne(x => x.Organization).WithMany(o => o.Branches).HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.MinimumDailyWagePhp)
                .HasPrecision(12, 2)
                .HasDefaultValue(Branch.DefaultMinimumDailyWagePhp);
            e.Property(x => x.PayrollRegularHolidayPercent)
                .HasPrecision(7, 2)
                .HasDefaultValue(Branch.DefaultPayrollRegularHolidayPercent);
            e.Property(x => x.PayrollSpecialDayPercent)
                .HasPrecision(7, 2)
                .HasDefaultValue(Branch.DefaultPayrollSpecialDayPercent);
            e.Property(x => x.PayrollRestDayPercent)
                .HasPrecision(7, 2)
                .HasDefaultValue(Branch.DefaultPayrollRestDayPercent);
            e.Property(x => x.PayrollHolidayPremiumEnabled)
                .HasDefaultValue(Branch.DefaultPayrollHolidayPremiumEnabled);
            e.Property(x => x.PayrollRestDayPremiumEnabled)
                .HasDefaultValue(Branch.DefaultPayrollRestDayPremiumEnabled);
            e.Property(x => x.PayrollNightDiffEnabled)
                .HasDefaultValue(Branch.DefaultPayrollNightDiffEnabled);
            e.Property(x => x.PayrollNightDiffPercent)
                .HasPrecision(7, 2)
                .HasDefaultValue(Branch.DefaultPayrollNightDiffPercent);
            e.Property(x => x.PayrollNightStart)
                .HasDefaultValue(Branch.DefaultPayrollNightStart);
            e.Property(x => x.PayrollNightEnd)
                .HasDefaultValue(Branch.DefaultPayrollNightEnd);
            e.Property(x => x.ShiftEnd)
                .HasDefaultValue(Branch.DefaultShiftEnd);
            e.Property(x => x.OtGraceMinutes)
                .HasDefaultValue(Branch.DefaultOtGraceMinutes);
            e.Property(x => x.AdminsCanApproveOt)
                .HasDefaultValue(Branch.DefaultAdminsCanApproveOt);
            e.Property(x => x.OtPremiumPercent)
                .HasPrecision(7, 2)
                .HasDefaultValue(Branch.DefaultOtPremiumPercent);
            e.Property(x => x.OtMonthlyDayDivisor)
                .HasPrecision(7, 2)
                .HasDefaultValue(Branch.DefaultOtMonthlyDayDivisor);
            e.Property(x => x.ClockRadiusMeters).HasDefaultValue(Branch.DefaultClockRadiusMeters);
            e.Property(x => x.RequireClockPhoto)
                .HasDefaultValue(Branch.DefaultRequireClockPhoto);
            e.Property(x => x.AllowOffsitePunchWithApproval)
                .HasDefaultValue(Branch.DefaultAllowOffsitePunchWithApproval);
            e.Property(x => x.WeekStartDay)
                .HasDefaultValue(Branch.DefaultWeekStartDay);
        });

        modelBuilder.Entity<Profile>(e =>
        {
            e.HasIndex(x => x.AuthUserId).IsUnique();
            e.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<Staff>(e =>
        {
            e.HasIndex(x => x.ProfileId).IsUnique();
            e.HasOne(x => x.Profile).WithOne(p => p.Staff).HasForeignKey<Staff>(x => x.ProfileId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            e.Property(x => x.OtAfterHoursPerDay).HasPrecision(5, 2);
            e.Property(x => x.OtMultiplier).HasPrecision(5, 2);
            e.Property(x => x.MustClock).HasDefaultValue(false);
            e.Property(x => x.RestDays).HasMaxLength(32);
            e.Property(x => x.EmergencyContactName).HasMaxLength(200);
            e.Property(x => x.EmergencyContactPhone).HasMaxLength(40);
            e.Property(x => x.Position).HasMaxLength(100);
            e.Property(x => x.Department).HasMaxLength(100);
            e.Property(x => x.EmployeeNo).HasMaxLength(100);
            e.Property(x => x.Tin).HasMaxLength(40);
            e.Property(x => x.Sss).HasMaxLength(40);
            e.Property(x => x.PhilHealth).HasMaxLength(40);
            e.Property(x => x.SeparationReason).HasMaxLength(200);
            e.Property(x => x.PagIbig).HasMaxLength(40);
            e.Property(x => x.RegisteredAddress).HasMaxLength(300);
            e.Property(x => x.ZipCode).HasMaxLength(10);
            e.ToTable(t => t.HasCheckConstraint(
                "CK_Staff_NonOwnerHasHomeBranch",
                "LOWER(\"Role\") = 'owner' OR \"BranchId\" IS NOT NULL"));
        });

        modelBuilder.Entity<EmploymentDocument>(e =>
        {
            e.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(160).IsRequired();
            e.Property(x => x.StoragePath).HasMaxLength(500).IsRequired();
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => x.StaffId);
            e.HasIndex(x => new { x.StaffId, x.CreatedAtUtc });
            e.HasOne(x => x.Staff).WithMany().HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LeaveAllotment>(e =>
        {
            e.Property(x => x.Days).HasPrecision(8, 2);
            e.HasIndex(x => new { x.StaffId, x.Year, x.LeaveType }).IsUnique();
            e.HasOne(x => x.Staff).WithMany().HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint(
                "CK_LeaveAllotments_DaysNonNegative",
                "\"Days\" >= 0"));
        });

        modelBuilder.Entity<LeaveRequest>(e =>
        {
            // Optimistic concurrency: xmin is the row version (Postgres system column, no migration column).
            e.Property<uint>("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => x.StaffId);
            e.HasIndex(x => x.BranchId);
            e.HasIndex(x => x.DecidedByStaffId);
            e.HasIndex(x => x.CancelledByStaffId);
            e.Property(x => x.CancelReason).HasMaxLength(500);
            e.HasOne(x => x.Staff).WithMany().HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DecidedByStaff).WithMany().HasForeignKey(x => x.DecidedByStaffId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CancelledByStaff).WithMany().HasForeignKey(x => x.CancelledByStaffId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AttendancePunch>(e =>
        {
            e.HasIndex(x => x.StaffId)
                .IsUnique()
                .HasFilter("\"OutAtUtc\" IS NULL")
                .HasDatabaseName("IX_AttendancePunches_StaffId_Open");
            e.HasIndex(x => new { x.StaffId, x.InAtUtc });
            e.HasIndex(x => new { x.BranchId, x.InAtUtc });
            e.Property(x => x.Source).HasMaxLength(40).IsRequired();
            e.Property(x => x.CloseReason).HasMaxLength(500);
            e.Property(x => x.InPhotoStoragePath).HasMaxLength(500);
            e.Property(x => x.InPhotoFileName).HasMaxLength(260);
            e.Property(x => x.InPhotoContentType).HasMaxLength(120);
            e.Property(x => x.OutPhotoStoragePath).HasMaxLength(500);
            e.Property(x => x.OutPhotoFileName).HasMaxLength(260);
            e.Property(x => x.OutPhotoContentType).HasMaxLength(120);
            e.Property(x => x.ApprovalStatus).HasDefaultValue(PunchApprovalStatus.Approved);
            e.Property(x => x.OffsiteReason).HasMaxLength(500);
            e.Property(x => x.ReviewNote).HasMaxLength(500);
            e.HasIndex(x => x.ReviewedByStaffId);
            e.HasOne(x => x.Staff).WithMany().HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ReviewedByStaff).WithMany().HasForeignKey(x => x.ReviewedByStaffId)
                .OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint(
                "CK_AttendancePunches_OutAfterIn",
                "\"OutAtUtc\" IS NULL OR \"OutAtUtc\" >= \"InAtUtc\""));
        });

        modelBuilder.Entity<OvertimeEntry>(e =>
        {
            e.ToTable("OvertimeEntries", t =>
            {
                t.HasCheckConstraint("CK_OvertimeEntries_ClaimedHours", "\"ClaimedHours\" > 0");
                t.HasCheckConstraint(
                    "CK_OvertimeEntries_ApprovedHours",
                    "\"ApprovedHours\" IS NULL OR (\"ApprovedHours\" > 0 AND \"ApprovedHours\" <= \"ClaimedHours\")");
            });
            e.Property(x => x.ClaimedHours).HasPrecision(10, 2);
            e.Property(x => x.ApprovedHours).HasPrecision(10, 2);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.DecisionNote).HasMaxLength(500);
            e.HasIndex(x => x.PunchId)
                .IsUnique()
                .HasFilter("\"PunchId\" IS NOT NULL");
            e.HasIndex(x => new { x.StaffId, x.WorkDate });
            e.HasIndex(x => new { x.BranchId, x.WorkDate });
            e.HasOne(x => x.Staff).WithMany().HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Punch).WithMany().HasForeignKey(x => x.PunchId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.DecidedByStaff).WithMany().HasForeignKey(x => x.DecidedByStaffId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PayrollDeduction>(e =>
        {
            e.ToTable("PayrollDeductions", t =>
            {
                t.HasCheckConstraint("CK_PayrollDeductions_Sss", "\"Sss\" >= 0");
                t.HasCheckConstraint("CK_PayrollDeductions_PhilHealth", "\"PhilHealth\" >= 0");
                t.HasCheckConstraint("CK_PayrollDeductions_PagIbig", "\"PagIbig\" >= 0");
                t.HasCheckConstraint("CK_PayrollDeductions_Wht", "\"Wht\" >= 0");
                t.HasCheckConstraint("CK_PayrollDeductions_Other", "\"Other\" >= 0");
            });
            e.Property(x => x.Sss).HasPrecision(12, 2).HasDefaultValue(0m);
            e.Property(x => x.PhilHealth).HasPrecision(12, 2).HasDefaultValue(0m);
            e.Property(x => x.PagIbig).HasPrecision(12, 2).HasDefaultValue(0m);
            e.Property(x => x.Wht).HasPrecision(12, 2).HasDefaultValue(0m);
            e.Property(x => x.Other).HasPrecision(12, 2).HasDefaultValue(0m);
            e.HasIndex(x => new { x.StaffId, x.PeriodFrom, x.PeriodTo }).IsUnique();
            e.HasOne(x => x.Staff).WithMany().HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PayrollThirteenthOverride>(e =>
        {
            e.ToTable("PayrollThirteenthOverrides", t =>
            {
                t.HasCheckConstraint(
                    "CK_PayrollThirteenthOverrides_BasicEarnedOverride",
                    "\"BasicEarnedOverride\" >= 0");
                t.HasCheckConstraint(
                    "CK_PayrollThirteenthOverrides_Year",
                    "\"Year\" >= 2000 AND \"Year\" <= 2100");
            });
            e.Property(x => x.BasicEarnedOverride).HasPrecision(12, 2);
            e.HasIndex(x => new { x.StaffId, x.Year }).IsUnique();
            e.HasOne(x => x.Staff).WithMany().HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.UpdatedByStaff).WithMany().HasForeignKey(x => x.UpdatedByStaffId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PayrollRun>(e =>
        {
            // Optimistic concurrency: xmin is the row version (Postgres system column, no migration column).
            e.Property<uint>("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
            e.ToTable("PayrollRuns", t =>
            {
                t.HasCheckConstraint("CK_PayrollRuns_Period", "\"PeriodTo\" >= \"PeriodFrom\"");
                t.HasCheckConstraint(
                    "CK_PayrollRuns_Totals",
                    "\"TotalGross\" >= 0 AND \"TotalDeductions\" >= 0");
            });
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.TotalGross).HasPrecision(12, 2);
            e.Property(x => x.TotalDeductions).HasPrecision(12, 2);
            e.Property(x => x.TotalNet).HasPrecision(12, 2);
            e.HasIndex(x => new { x.BranchId, x.PeriodFrom, x.PeriodTo }).IsUnique();
            e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedByStaff).WithMany().HasForeignKey(x => x.CreatedByStaffId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.LockedByStaff).WithMany().HasForeignKey(x => x.LockedByStaffId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.VoidedByStaff).WithMany().HasForeignKey(x => x.VoidedByStaffId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PayrollRunLine>(e =>
        {
            e.ToTable("PayrollRunLines", t =>
            {
                t.HasCheckConstraint(
                    "CK_PayrollRunLines_Amounts",
                    "\"Gross\" >= 0 AND \"Sss\" >= 0 AND \"PhilHealth\" >= 0 AND \"PagIbig\" >= 0 " +
                    "AND \"Wht\" >= 0 AND \"Other\" >= 0 AND \"Deductions\" >= 0");
                t.HasCheckConstraint(
                    "CK_PayrollRunLines_Allowances",
                    "\"Allowances\" IS NULL OR \"Allowances\" >= 0");
                t.HasCheckConstraint(
                    "CK_PayrollRunLines_LoanDeductions",
                    "\"LoanDeductions\" IS NULL OR \"LoanDeductions\" >= 0");
                t.HasCheckConstraint(
                    "CK_PayrollRunLines_OtPay",
                    "\"OtPay\" IS NULL OR \"OtPay\" >= 0");
            });
            e.Property(x => x.StaffName).HasMaxLength(200).IsRequired();
            e.Property(x => x.RatePhp).HasPrecision(12, 2);
            e.Property(x => x.RegularHours).HasPrecision(10, 2);
            e.Property(x => x.OtHours).HasPrecision(10, 2);
            e.Property(x => x.OtPay).HasPrecision(12, 2);
            e.Property(x => x.Gross).HasPrecision(12, 2);
            e.Property(x => x.Sss).HasPrecision(12, 2);
            e.Property(x => x.PhilHealth).HasPrecision(12, 2);
            e.Property(x => x.PagIbig).HasPrecision(12, 2);
            e.Property(x => x.Wht).HasPrecision(12, 2);
            e.Property(x => x.Other).HasPrecision(12, 2);
            e.Property(x => x.Deductions).HasPrecision(12, 2);
            e.Property(x => x.Net).HasPrecision(12, 2);
            e.Property(x => x.Notes).HasMaxLength(200);
            e.Property(x => x.HolidayHours).HasPrecision(10, 2);
            e.Property(x => x.RestDayHours).HasPrecision(10, 2);
            e.Property(x => x.NightHours).HasPrecision(10, 2);
            e.Property(x => x.PremiumPay).HasPrecision(12, 2);
            e.Property(x => x.Allowances).HasPrecision(12, 2);
            e.Property(x => x.LoanDeductions).HasPrecision(12, 2);
            e.HasIndex(x => new { x.RunId, x.StaffId }).IsUnique();
            e.HasOne(x => x.Run).WithMany(run => run.Lines).HasForeignKey(x => x.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Staff).WithMany().HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ShopHoliday>(e =>
        {
            e.ToTable("ShopHolidays");
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.HasIndex(x => new { x.BranchId, x.Date }).IsUnique().AreNullsDistinct(false);
            e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PayrollAmountOverride>(e =>
        {
            e.ToTable("PayrollAmountOverrides", t =>
            {
                t.HasCheckConstraint("CK_PayrollAmountOverrides_Period", "\"PeriodTo\" >= \"PeriodFrom\"");
                t.HasCheckConstraint("CK_PayrollAmountOverrides_Amount", "\"Amount\" >= 0");
            });
            e.Property(x => x.Key).HasMaxLength(80).IsRequired();
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            e.HasIndex(x => new { x.StaffId, x.PeriodFrom, x.PeriodTo, x.Key }).IsUnique();
            e.HasOne(x => x.Staff).WithMany().HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SetByStaff).WithMany().HasForeignKey(x => x.SetByStaffId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PayrollDayPayOverride>(e =>
        {
            e.ToTable("PayrollDayPayOverrides", t =>
            {
                t.HasCheckConstraint(
                    "CK_PayrollDayPayOverrides_Custom",
                    "(\"Mode\" <> 'Custom' AND \"CustomAmount\" IS NULL) OR (\"Mode\" = 'Custom' AND \"CustomAmount\" IS NOT NULL AND \"CustomAmount\" >= 0)");
            });
            e.Property(x => x.CustomAmount).HasPrecision(12, 2);
            e.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            e.HasIndex(x => new { x.StaffId, x.WorkDate }).IsUnique();
            e.HasOne(x => x.Staff).WithMany().HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SetByStaff).WithMany().HasForeignKey(x => x.SetByStaffId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<StaffAllowance>(e =>
        {
            e.ToTable("StaffAllowances", t =>
            {
                t.HasCheckConstraint("CK_StaffAllowances_Amount", "\"Amount\" >= 0");
                t.HasCheckConstraint(
                    "CK_StaffAllowances_Effective",
                    "\"EffectiveTo\" IS NULL OR \"EffectiveFrom\" IS NULL OR \"EffectiveTo\" >= \"EffectiveFrom\"");
            });
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.Taxable).HasDefaultValue(true);
            e.Property(x => x.Active).HasDefaultValue(true);
            e.HasOne(x => x.Staff).WithMany().HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedByStaff).WithMany().HasForeignKey(x => x.CreatedByStaffId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<StaffLoan>(e =>
        {
            e.ToTable("StaffLoans", t =>
            {
                t.HasCheckConstraint("CK_StaffLoans_Principal", "\"Principal\" > 0");
                t.HasCheckConstraint("CK_StaffLoans_PerPeriodAmount", "\"PerPeriodAmount\" > 0");
            });
            e.Property(x => x.Kind).HasMaxLength(80).IsRequired();
            e.Property(x => x.Principal).HasPrecision(12, 2);
            e.Property(x => x.PerPeriodAmount).HasPrecision(12, 2);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.CancelReason).HasMaxLength(500);
            e.HasIndex(x => x.StaffId);
            e.HasOne(x => x.Staff).WithMany().HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedByStaff).WithMany().HasForeignKey(x => x.CreatedByStaffId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<StaffLoanPayment>(e =>
        {
            e.ToTable("StaffLoanPayments", t =>
            {
                t.HasCheckConstraint("CK_StaffLoanPayments_Amount", "\"Amount\" > 0");
                t.HasCheckConstraint(
                    "CK_StaffLoanPayments_Period",
                    "\"PeriodTo\" >= \"PeriodFrom\"");
            });
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.HasIndex(x => x.LoanId);
            e.HasIndex(x => x.RunId);
            e.HasIndex(x => new { x.LoanId, x.RunId }).IsUnique();
            e.HasOne(x => x.Loan).WithMany().HasForeignKey(x => x.LoanId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Run).WithMany().HasForeignKey(x => x.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Staff).WithMany().HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PayrollRunLineItem>(e =>
        {
            e.ToTable("PayrollRunLineItems", t =>
                t.HasCheckConstraint("CK_PayrollRunLineItems_Amount", "\"Amount\" >= 0"));
            e.Property(x => x.Kind).HasMaxLength(20).IsRequired();
            e.Property(x => x.Label).HasMaxLength(120).IsRequired();
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Quantity).HasPrecision(12, 2);
            e.Property(x => x.BalanceAfter).HasPrecision(12, 2);
            e.HasOne(x => x.Line).WithMany().HasForeignKey(x => x.LineId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StaffPayRate>(e =>
        {
            e.ToTable("StaffPayRates", t =>
                t.HasCheckConstraint("CK_StaffPayRates_Rate", "\"RatePhp\" >= 0"));
            e.Property(x => x.RatePhp).HasPrecision(12, 2);
            e.Property(x => x.OtAfterHoursPerDay).HasPrecision(6, 2);
            e.Property(x => x.OtMultiplier).HasPrecision(6, 2);
            e.Property(x => x.Reason).HasMaxLength(200);
            e.HasIndex(x => new { x.StaffId, x.EffectiveFrom }).IsUnique();
            e.HasOne(x => x.Staff).WithMany().HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedByStaff).WithMany().HasForeignKey(x => x.CreatedByStaffId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<HrChangeLog>(e =>
        {
            e.Property(x => x.Field).HasMaxLength(80).IsRequired();
            e.Property(x => x.OldValue).HasMaxLength(200);
            e.Property(x => x.NewValue).HasMaxLength(200);
            e.Property(x => x.RefType).HasMaxLength(40);
            e.HasIndex(x => new { x.SubjectStaffId, x.AtUtc });
            e.HasIndex(x => new { x.Field, x.AtUtc });
            e.HasOne(x => x.ActorStaff).WithMany().HasForeignKey(x => x.ActorStaffId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SubjectStaff).WithMany().HasForeignKey(x => x.SubjectStaffId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SettingsChangeLog>(e =>
        {
            // BranchId is a loose reference (no foreign key) so the trail outlives a hard-deleted shop.
            e.Property(x => x.Area).HasMaxLength(40).IsRequired();
            e.Property(x => x.Field).HasMaxLength(80).IsRequired();
            e.Property(x => x.OldValue).HasMaxLength(500);
            e.Property(x => x.NewValue).HasMaxLength(500);
            e.HasIndex(x => new { x.BranchId, x.Area, x.ChangedAtUtc });
            e.HasIndex(x => new { x.Area, x.Field, x.ChangedAtUtc });
            e.HasOne(x => x.ChangedByStaff).WithMany().HasForeignKey(x => x.ChangedByStaffId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Announcement>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(160).IsRequired();
            e.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            e.Property(x => x.Active).HasDefaultValue(true);
            e.HasIndex(x => new { x.Active, x.StartDate, x.EndDate });
            e.HasOne(x => x.CreatedByStaff).WithMany().HasForeignKey(x => x.CreatedByStaffId)
                .OnDelete(DeleteBehavior.SetNull);
            e.ToTable(t => t.HasCheckConstraint(
                "CK_Announcements_DateRange",
                "\"EndDate\" >= \"StartDate\""));
        });

        modelBuilder.Entity<IncentiveBonus>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Reason).HasMaxLength(200).IsRequired();
            e.Property(x => x.VoidReason).HasMaxLength(200);
            e.Property(x => x.IdempotencyKey).HasMaxLength(64);
            e.HasIndex(x => new { x.StaffId, x.Year, x.Quarter });
            e.HasIndex(x => new { x.StaffId, x.IdempotencyKey })
                .IsUnique()
                .HasFilter("\"IdempotencyKey\" IS NOT NULL");
            e.HasOne<Staff>().WithMany().HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Staff>().WithMany().HasForeignKey(x => x.CreatedByStaffId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Staff>().WithMany().HasForeignKey(x => x.VoidedByStaffId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PayrollRun>().WithMany().HasForeignKey(x => x.PaidRunId)
                .OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint(
                    "CK_IncentiveBonuses_Quarter",
                    "\"Quarter\" >= 1 AND \"Quarter\" <= 4");
                t.HasCheckConstraint(
                    "CK_IncentiveBonuses_Amount",
                    "\"Amount\" > 0");
                t.HasCheckConstraint(
                    "CK_IncentiveBonuses_Void",
                    "\"IsVoided\" = FALSE OR (\"VoidReason\" IS NOT NULL AND \"VoidedByStaffId\" IS NOT NULL AND \"VoidedAtUtc\" IS NOT NULL)");
                t.HasCheckConstraint(
                    "CK_IncentiveBonuses_Status",
                    "\"Status\" IN ('Scheduled', 'Paid', 'Void') AND (\"Status\" = 'Void') = \"IsVoided\"");
                t.HasCheckConstraint(
                    "CK_IncentiveBonuses_Paid",
                    "\"Status\" <> 'Paid' OR (\"PaidRunId\" IS NOT NULL AND \"PaidAtUtc\" IS NOT NULL)");
                t.HasCheckConstraint(
                    "CK_IncentiveBonuses_PayPeriod",
                    "(\"PayPeriodFrom\" IS NULL AND \"PayPeriodTo\" IS NULL) OR (\"PayPeriodFrom\" IS NOT NULL AND \"PayPeriodTo\" IS NOT NULL AND \"PayPeriodFrom\" <= \"PayPeriodTo\")");
            });
        });
    }

    /// <summary>
    /// Enums are stored as strings (max 40). Money-named decimals on HR tables are numeric(12,2).
    /// Only touches the entity types this package owns, and only enums from HrCore.Domain:
    /// enums an app adds on its own part of a shared entity keep the app's own mapping.
    /// </summary>
    private static void ApplyConventions(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes().Where(t => Owned.Contains(t.ClrType)))
        {
            foreach (var property in entity.GetProperties())
            {
                var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (type.IsEnum && type.Namespace == typeof(Staff).Namespace)
                {
                    property.SetProviderClrType(typeof(string));
                    property.SetMaxLength(40);
                }
                else if (type == typeof(decimal) && MoneyNames.Contains(property.Name))
                {
                    property.SetPrecision(12);
                    property.SetScale(2);
                }
            }
        }
    }

    private static readonly HashSet<Type> Owned =
    [
        typeof(Organization), typeof(Branch), typeof(Profile), typeof(Staff), typeof(EmploymentDocument),
        typeof(LeaveAllotment), typeof(LeaveRequest), typeof(AttendancePunch), typeof(OvertimeEntry),
        typeof(PayrollDeduction), typeof(PayrollThirteenthOverride), typeof(PayrollRun), typeof(PayrollRunLine),
        typeof(ShopHoliday), typeof(PayrollAmountOverride), typeof(PayrollDayPayOverride), typeof(StaffAllowance),
        typeof(StaffLoan), typeof(StaffLoanPayment), typeof(PayrollRunLineItem), typeof(StaffPayRate),
        typeof(HrChangeLog), typeof(SettingsChangeLog), typeof(Announcement), typeof(IncentiveBonus)
    ];
}
