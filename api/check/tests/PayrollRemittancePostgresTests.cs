using HrCore.Data;
using HrCore.Domain;
using HrCore.Features.HrChangeLog;
using HrCore.Features.Payroll;
using HrCore.Features.SettingsLog;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HrCore.Tests;

/// <summary>
/// Locks against Postgres. The in-memory provider does not apply column defaults, so it cannot
/// show a null employer share being stored as 0.
/// </summary>
public sealed class PayrollRemittancePostgresTests
{
    private const string AdminConnection =
        "Host=127.0.0.1;Username=hrtest;Password=hrtest;Database=postgres";

    [Fact]
    public async Task A_shop_with_the_report_off_stores_null_employer_shares()
    {
        await using var postgres = await PostgresShop.OpenAsync(AdminConnection);
        var (run, error, line) = await postgres.LockAndReloadAsync(
            new DateOnly(2025, 11, 1),
            new DateOnly(2025, 11, 30),
            enabled: false);

        Assert.Null(error);
        Assert.NotNull(run);
        Assert.Null(line.EmployerSss);
        Assert.Null(line.EmployerPhilHealth);
        Assert.Null(line.EmployerPagIbig);
        Assert.Null(run.Lines[0].EmployerSss);
        Assert.Null(run.Lines[0].EmployerPhilHealth);
        Assert.Null(run.Lines[0].EmployerPagIbig);
    }

    [Fact]
    public async Task A_shop_with_the_report_on_stores_the_employer_shares_from_the_pack()
    {
        await using var postgres = await PostgresShop.OpenAsync(AdminConnection);
        var (run, error, line) = await postgres.LockAndReloadAsync(
            new DateOnly(2025, 11, 1),
            new DateOnly(2025, 11, 30),
            enabled: true);

        Assert.Null(error);
        Assert.NotNull(run);
        Assert.Equal(2_030m, line.EmployerSss);
        Assert.Equal(500m, line.EmployerPhilHealth);
        Assert.Equal(200m, line.EmployerPagIbig);
        Assert.Equal(2_030m, run.Lines[0].EmployerSss);
        Assert.Equal(500m, run.Lines[0].EmployerPhilHealth);
        Assert.Equal(200m, run.Lines[0].EmployerPagIbig);
    }

    private sealed class PostgresShop : IAsyncDisposable
    {
        private readonly string databaseName;
        private readonly string adminConnection;
        private readonly TestHrDb db;
        private readonly HrCoreFeatures features;
        private readonly PayrollRunService runs;

        private PostgresShop(
            string databaseName,
            string adminConnection,
            TestHrDb db,
            HrCoreFeatures features,
            PayrollRunService runs)
        {
            this.databaseName = databaseName;
            this.adminConnection = adminConnection;
            this.db = db;
            this.features = features;
            this.runs = runs;
        }

        public static async Task<PostgresShop> OpenAsync(string adminConnection)
        {
            var databaseName = "hr_" + Guid.NewGuid().ToString("N");
            await using (var admin = new NpgsqlConnection(adminConnection))
            {
                await admin.OpenAsync();
                await using var create = new NpgsqlCommand(
                    "CREATE DATABASE \"" + databaseName + "\"",
                    admin);
                await create.ExecuteNonQueryAsync();
            }

            var features = new HrCoreFeatures { EmployerRemittance = true };
            var connection = new NpgsqlConnectionStringBuilder(adminConnection)
            {
                Database = databaseName
            }.ConnectionString;
            var built = new DbContextOptionsBuilder<TestHrDb>()
                .UseNpgsql(connection)
                .UseHrCoreFeatures(features);
            var db = new TestHrDb((DbContextOptions<TestHrDb>)built.Options);
            await db.Database.EnsureCreatedAsync();
            await SeedAsync(db);
            var payroll = new PayrollService(db, features);
            var runs = new PayrollRunService(
                db,
                new HrChangeLogService(db),
                payroll,
                new PayrollRemittanceService(db, features, payroll, new SettingsChangeLogService(db)));
            return new PostgresShop(databaseName, adminConnection, db, features, runs);
        }

        public async Task<(PayrollRunDetailDto? Run, string? Error, PayrollRunLine Line)> LockAndReloadAsync(
            DateOnly from,
            DateOnly to,
            bool enabled)
        {
            var branch = await db.Branches.SingleAsync();
            if (enabled)
            {
                db.BranchRemittanceSettings().Add(new BranchRemittanceSetting
                {
                    Id = Guid.NewGuid(),
                    BranchId = branch.Id,
                    Enabled = true
                });
                await db.SaveChangesAsync();
            }

            var (run, error) = await runs.LockAsync(
                [branch],
                branch.Id,
                from,
                to,
                null,
                null,
                false,
                CancellationToken.None);
            var line = await db.PayrollRunLines.AsNoTracking()
                .SingleAsync(row => row.RunId == run!.Run.Id);
            return (run, error, line);
        }

        public async ValueTask DisposeAsync()
        {
            await db.DisposeAsync();
            await using var admin = new NpgsqlConnection(adminConnection);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand(
                "DROP DATABASE IF EXISTS \"" + databaseName + "\" WITH (FORCE)",
                admin);
            await drop.ExecuteNonQueryAsync();
        }

        private static async Task SeedAsync(TestHrDb db)
        {
            var now = new DateTime(2025, 11, 1, 0, 0, 0, DateTimeKind.Utc);
            var branchId = Guid.NewGuid();
            db.Organizations.Add(new Organization
            {
                Id = PlatformIds.OrganizationId,
                LegalName = "North Desk Co",
                TradingName = "North Desk",
                CreatedAtUtc = now
            });
            db.Branches.Add(new Branch
            {
                Id = branchId,
                OrganizationId = PlatformIds.OrganizationId,
                Code = "north-desk",
                Name = "North Desk",
                IsLive = true,
                Kind = BranchKind.Owned,
                CreatedAtUtc = now
            });
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
                Id = Guid.NewGuid(),
                ProfileId = profile.Id,
                BranchId = branchId,
                Role = StaffRole.Worker,
                Active = true,
                PayBasis = PayBasis.Monthly,
                RatePhp = 20_000m,
                MustClock = false,
                CreatedAtUtc = now
            });
            await db.SaveChangesAsync();
        }
    }
}
