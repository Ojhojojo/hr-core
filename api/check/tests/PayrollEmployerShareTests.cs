using HrCore.Data;
using HrCore.Domain;
using HrCore.Features.Payroll;
using HrCore.Features.SettingsLog;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Tests;

public sealed class PayrollEmployerShareTests
{
    private static readonly DateOnly November2025 = new(2025, 11, 1);
    private static readonly DateOnly November2024 = new(2024, 11, 1);

    [Fact]
    public void Sss_employer_share_follows_the_msc_table_and_includes_ec()
    {
        // 2025: 10% of MSC plus EC. MSC 20,000 is above 14,500, so EC is 30.
        Assert.Equal(2_030m, StatutoryPack.SssEmployerShare(November2025, 20_000m));
        Assert.Equal(1_000m, StatutoryPack.SssEmployeeShare(November2025, 20_000m));

        // MSC 14,500 still pays EC 10. The next step, 15,000, pays EC 30.
        Assert.Equal(1_460m, StatutoryPack.SssEmployerShare(November2025, 14_500m));
        Assert.Equal(1_530m, StatutoryPack.SssEmployerShare(November2025, 15_000m));

        // Below the floor the MSC is 5,000. EC stays 10.
        Assert.Equal(510m, StatutoryPack.SssEmployerShare(November2025, 4_000m));
        Assert.Equal(250m, StatutoryPack.SssEmployeeShare(November2025, 4_000m));

        // The ceiling MSC is 35,000.
        Assert.Equal(3_530m, StatutoryPack.SssEmployerShare(November2025, 120_000m));

        // 2024: 9.5% of MSC 30,000 is 2,850, plus EC 30.
        Assert.Equal(2_880m, StatutoryPack.SssEmployerShare(November2024, 30_000m));
        Assert.Equal(1_350m, StatutoryPack.SssEmployeeShare(November2024, 30_000m));
    }

    [Fact]
    public void PhilHealth_employer_share_is_the_other_half_of_the_premium()
    {
        Assert.Equal(500m, StatutoryPack.PhilHealthEmployerShare(November2025, 20_000m));
        Assert.Equal(500m, StatutoryPack.PhilHealthEmployeeShare(November2025, 20_000m));

        // Floor 10,000 in 2025: 2.5% is 250. Ceiling 100,000: 2.5% is 2,500.
        Assert.Equal(250m, StatutoryPack.PhilHealthEmployerShare(November2025, 8_000m));
        Assert.Equal(2_500m, StatutoryPack.PhilHealthEmployerShare(November2025, 120_000m));

        // 2024 rate is 2.25%.
        Assert.Equal(675m, StatutoryPack.PhilHealthEmployerShare(November2024, 30_000m));
        Assert.Equal(675m, StatutoryPack.PhilHealthEmployeeShare(November2024, 30_000m));

        // 20,001 × 2.5% = 500.025, which rounds away from zero to 500.03.
        Assert.Equal(500.03m, StatutoryPack.PhilHealthEmployerShare(November2025, 20_001m));
    }

    [Fact]
    public void PagIbig_employer_share_is_2_percent_of_the_capped_base()
    {
        // The employee share on a small salary is 1%. The employer share stays 2%.
        Assert.Equal(40m, StatutoryPack.PagIbigEmployeeShare(4_000m));
        Assert.Equal(80m, StatutoryPack.PagIbigEmployerShare(4_000m));
        Assert.Equal(50m, StatutoryPack.PagIbigEmployeeShare(5_000m));
        Assert.Equal(100m, StatutoryPack.PagIbigEmployerShare(5_000m));

        // Above the pack's small-salary band both sides are 2%, and the base stops at 10,000.
        Assert.Equal(200m, StatutoryPack.PagIbigEmployeeShare(20_000m));
        Assert.Equal(200m, StatutoryPack.PagIbigEmployerShare(20_000m));
        Assert.Equal(200m, StatutoryPack.PagIbigEmployerShare(120_000m));
        Assert.Equal(0m, StatutoryPack.PagIbigEmployerShare(0m));
    }

    [Fact]
    public void Months_before_the_pack_have_no_employer_share()
    {
        var before = new DateOnly(2023, 12, 1);
        var shares = PayrollEmployerShare.Monthly(before, 20_000m);
        Assert.Equal(0m, shares.Sss);
        Assert.Equal(0m, shares.PhilHealth);
        Assert.Equal(0m, shares.PagIbig);
    }

    [Fact]
    public void A_half_period_prorates_and_a_cut_off_rule_places_the_whole_month()
    {
        var full = PayrollEmployerShare.ForPeriod(November2025, 20_000m, 20_000m, [], null, 0);
        Assert.Equal(2_030m, full.Sss);
        Assert.Equal(500m, full.PhilHealth);
        Assert.Equal(200m, full.PagIbig);

        var half = PayrollEmployerShare.ForPeriod(November2025, 20_000m, 10_000m, [], null, 0);
        Assert.Equal(1_015m, half.Sss);
        Assert.Equal(250m, half.PhilHealth);
        Assert.Equal(100m, half.PagIbig);

        var rules = new PayrollEmployerShare.Rule[]
        {
            new("sss", PayrollStatutoryAllocation.Cutoff, 2)
        };
        var first = PayrollEmployerShare.ForPeriod(November2025, 20_000m, 10_000m, rules, 1, 2);
        var second = PayrollEmployerShare.ForPeriod(November2025, 20_000m, 10_000m, rules, 2, 2);
        Assert.Equal(0m, first.Sss);
        Assert.Equal(250m, first.PhilHealth);
        Assert.Equal(2_030m, second.Sss);
        Assert.Equal(250m, second.PhilHealth);

        // Split keeps the rounding remainder on the last period. 200 / 3 is 66.67, 66.67, 66.66.
        var split = new PayrollEmployerShare.Rule[]
        {
            new("pagibig", PayrollStatutoryAllocation.Split, null)
        };
        Assert.Equal(66.67m, PayrollEmployerShare.ForPeriod(November2025, 20_000m, 6_000m, split, 1, 3).PagIbig);
        Assert.Equal(66.67m, PayrollEmployerShare.ForPeriod(November2025, 20_000m, 7_000m, split, 2, 3).PagIbig);
        Assert.Equal(66.66m, PayrollEmployerShare.ForPeriod(November2025, 20_000m, 7_000m, split, 3, 3).PagIbig);
    }

    [Fact]
    public void Report_totals_add_employee_and_employer_shares_and_treat_null_as_zero()
    {
        var agencies = PayrollRemittance.Sum(
        [
            new PayrollRemittance.Line(1_000m, 500m, 200m, 2_030m, 500m, 200m),
            new PayrollRemittance.Line(10m, 0m, 0m, null, null, null)
        ]);

        Assert.Equal(1_010m, agencies[0].Employee);
        Assert.Equal(2_030m, agencies[0].Employer);
        Assert.Equal(3_040m, agencies[0].Total);
        Assert.Equal(1_000m, agencies[1].Total);
        Assert.Equal(400m, agencies[2].Total);
        Assert.Equal(1_710m, PayrollRemittance.EmployeeTotal(agencies));
        Assert.Equal(2_730m, PayrollRemittance.EmployerTotal(agencies));

        var csv = PayrollRemittance.Csv(agencies);
        Assert.Contains("SSS,1010.00,2030.00,3040.00", csv);
        Assert.Contains("PhilHealth,500.00,500.00,1000.00", csv);
        Assert.Contains("Pag-IBIG,200.00,200.00,400.00", csv);
        Assert.Contains("Total,1710.00,2730.00,4440.00", csv);
    }

    [Fact]
    public void Flag_off_leaves_employer_share_columns_out_of_the_model()
    {
        var options = new DbContextOptionsBuilder<TestHrDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new TestHrDb(options);
        var line = db.Model.FindEntityType(typeof(PayrollRunLine));
        Assert.NotNull(line);
        Assert.Null(line.FindProperty(nameof(PayrollRunLine.EmployerSss)));
        Assert.Null(line.FindProperty(nameof(PayrollRunLine.EmployerPhilHealth)));
        Assert.Null(line.FindProperty(nameof(PayrollRunLine.EmployerPagIbig)));
        Assert.DoesNotContain(
            typeof(BranchRemittanceSetting),
            db.Model.GetEntityTypes().Select(entity => entity.ClrType));
    }

    [Fact]
    public void Flag_on_maps_employer_share_columns_as_nullable_14_2_without_a_default()
    {
        var built = new DbContextOptionsBuilder<TestHrDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .UseHrCoreFeatures(new HrCoreFeatures { EmployerRemittance = true });
        using var db = new TestHrDb((DbContextOptions<TestHrDb>)built.Options);
        var line = db.Model.FindEntityType(typeof(PayrollRunLine));
        Assert.NotNull(line);
        foreach (var name in new[]
        {
            nameof(PayrollRunLine.EmployerSss),
            nameof(PayrollRunLine.EmployerPhilHealth),
            nameof(PayrollRunLine.EmployerPagIbig)
        })
        {
            var property = line.FindProperty(name);
            Assert.NotNull(property);
            Assert.True(property.IsNullable);
            Assert.Equal(14, property.GetPrecision());
            Assert.Equal(2, property.GetScale());
            Assert.Null(property.GetDefaultValue());
        }
    }

    [Fact]
    public async Task Flag_off_does_not_stamp_shares_and_does_not_read_the_setting_table()
    {
        var features = new HrCoreFeatures();
        await using var db = Open(features);
        var remittance = Service(db, features);
        var branch = new Branch { Id = Guid.NewGuid(), Name = "North Desk" };

        var shares = await remittance.ForRegisterAsync(
            branch,
            new DateOnly(2025, 11, 1),
            new DateOnly(2025, 11, 30),
            [],
            false,
            CancellationToken.None);

        Assert.Null(shares);
    }

    [Fact]
    public async Task An_enabled_shop_stamps_the_monthly_employer_shares_for_a_20000_salary()
    {
        var features = new HrCoreFeatures { EmployerRemittance = true };
        await using var db = Open(features);
        var (branch, staffId) = await SeedMonthlyShop(db, 20_000m);
        db.BranchRemittanceSettings().Add(new BranchRemittanceSetting
        {
            Id = Guid.NewGuid(),
            BranchId = branch.Id,
            Enabled = true
        });
        await db.SaveChangesAsync();

        var payroll = new PayrollService(db, features);
        var remittance = Service(db, features);
        var from = new DateOnly(2025, 11, 1);
        var to = new DateOnly(2025, 11, 30);
        var register = await payroll.GetRegisterAsync([branch], from, to, false, false, CancellationToken.None);
        var row = Assert.Single(register.Rows);
        Assert.Equal(staffId, row.StaffId);
        Assert.Equal(20_000m, row.Amount);

        var shares = await remittance.ForRegisterAsync(
            branch, from, to, register.Rows, false, CancellationToken.None);

        Assert.NotNull(shares);
        Assert.Equal(2_030m, shares[staffId].Sss);
        Assert.Equal(500m, shares[staffId].PhilHealth);
        Assert.Equal(200m, shares[staffId].PagIbig);
    }

    [Fact]
    public async Task A_shop_that_has_not_turned_the_report_on_stamps_nothing()
    {
        var features = new HrCoreFeatures { EmployerRemittance = true };
        await using var db = Open(features);
        var (branch, _) = await SeedMonthlyShop(db, 20_000m);
        var payroll = new PayrollService(db, features);
        var from = new DateOnly(2025, 11, 1);
        var to = new DateOnly(2025, 11, 30);
        var register = await payroll.GetRegisterAsync([branch], from, to, false, false, CancellationToken.None);

        var shares = await Service(db, features).ForRegisterAsync(
            branch, from, to, register.Rows, false, CancellationToken.None);

        Assert.Null(shares);
    }

    [Fact]
    public async Task Sss_on_cut_off_2_stores_the_employer_share_only_on_that_period()
    {
        var features = new HrCoreFeatures { EmployerRemittance = true, PayrollSchedules = true };
        await using var db = Open(features);
        var (branch, staffId) = await SeedMonthlyShop(db, 20_000m);
        db.BranchRemittanceSettings().Add(new BranchRemittanceSetting
        {
            Id = Guid.NewGuid(),
            BranchId = branch.Id,
            Enabled = true
        });
        db.BranchPayrollCutoffs().AddRange(
            new BranchPayrollCutoff { Id = Guid.NewGuid(), BranchId = branch.Id, Ordinal = 1, StartDay = 1, EndDay = 15 },
            new BranchPayrollCutoff { Id = Guid.NewGuid(), BranchId = branch.Id, Ordinal = 2, StartDay = 16, EndDay = 31 });
        db.BranchDeductionSchedules().Add(new BranchDeductionSchedule
        {
            Id = Guid.NewGuid(),
            BranchId = branch.Id,
            DeductionKey = "sss",
            Mode = PayrollStatutoryAllocation.Cutoff,
            CutoffOrdinal = 2
        });
        await db.SaveChangesAsync();

        var payroll = new PayrollService(db, features);
        var remittance = Service(db, features);

        var firstRegister = await payroll.GetRegisterAsync(
            [branch], new DateOnly(2025, 11, 1), new DateOnly(2025, 11, 15), false, false, CancellationToken.None);
        Assert.Equal(10_000m, Assert.Single(firstRegister.Rows).Amount);
        var first = await remittance.ForRegisterAsync(
            branch,
            new DateOnly(2025, 11, 1),
            new DateOnly(2025, 11, 15),
            firstRegister.Rows,
            false,
            CancellationToken.None);
        Assert.NotNull(first);
        Assert.Equal(0m, first[staffId].Sss);
        Assert.Equal(250m, first[staffId].PhilHealth);
        Assert.Equal(100m, first[staffId].PagIbig);

        var secondRegister = await payroll.GetRegisterAsync(
            [branch], new DateOnly(2025, 11, 16), new DateOnly(2025, 11, 30), false, false, CancellationToken.None);
        Assert.Equal(10_000m, Assert.Single(secondRegister.Rows).Amount);
        var second = await remittance.ForRegisterAsync(
            branch,
            new DateOnly(2025, 11, 16),
            new DateOnly(2025, 11, 30),
            secondRegister.Rows,
            false,
            CancellationToken.None);
        Assert.NotNull(second);
        Assert.Equal(2_030m, second[staffId].Sss);
        Assert.Equal(250m, second[staffId].PhilHealth);
        Assert.Equal(100m, second[staffId].PagIbig);
    }

    private static TestHrDb Open(HrCoreFeatures features)
    {
        var built = new DbContextOptionsBuilder<TestHrDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .UseHrCoreFeatures(features);
        var db = new TestHrDb((DbContextOptions<TestHrDb>)built.Options);
        db.Database.EnsureCreated();
        return db;
    }

    private static PayrollRemittanceService Service(TestHrDb db, HrCoreFeatures features) =>
        new(db, features, new PayrollService(db, features), new SettingsChangeLogService(db));

    private static async Task<(Branch Branch, Guid StaffId)> SeedMonthlyShop(TestHrDb db, decimal rate)
    {
        var now = new DateTime(2025, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var branchId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        db.Organizations.Add(new Organization
        {
            Id = PlatformIds.OrganizationId,
            LegalName = "North Desk Co",
            TradingName = "North Desk",
            CreatedAtUtc = now
        });
        var branch = new Branch
        {
            Id = branchId,
            OrganizationId = PlatformIds.OrganizationId,
            Code = "north-desk",
            Name = "North Desk",
            IsLive = true,
            Kind = BranchKind.Owned,
            CreatedAtUtc = now
        };
        db.Branches.Add(branch);
        var profile = new Profile
        {
            Id = Guid.NewGuid(),
            AuthUserId = "casey-diaz",
            Kind = ProfileKind.Staff,
            DisplayName = "Casey Diaz",
            Email = "casey@example.com",
            CreatedAtUtc = now
        };
        db.Profiles.Add(profile);
        db.Staff.Add(new Staff
        {
            Id = staffId,
            ProfileId = profile.Id,
            BranchId = branchId,
            Role = StaffRole.Worker,
            Active = true,
            PayBasis = PayBasis.Monthly,
            RatePhp = rate,
            MustClock = false,
            CreatedAtUtc = now
        });
        await db.SaveChangesAsync();
        return (branch, staffId);
    }
}
