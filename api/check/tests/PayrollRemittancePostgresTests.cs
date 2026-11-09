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
    public async Task A_shop_with_the_report_on_leaves_employer_shares_null_until_confirm()
    {
        await using var postgres = await PostgresShop.OpenAsync(AdminConnection);
        var (run, error, line) = await postgres.LockAndReloadAsync(
            new DateOnly(2025, 11, 1),
            new DateOnly(2025, 11, 30),
            enabled: true);

        Assert.Null(error);
        Assert.NotNull(run);
        Assert.Null(line.EmployerSss);
        Assert.Null(line.EmployerPhilHealth);
        Assert.Null(line.EmployerPagIbig);
        Assert.Null(run.Lines[0].EmployerSss);
        var stored = await postgres.ReloadRunAsync(run.Run.Id);
        Assert.Null(stored.EmployerSharesReviewedAtUtc);
        Assert.Null(stored.EmployerSharesReviewedBy);
    }

    [Fact]
    public async Task Confirm_stores_edited_shares_and_a_second_confirm_updates_the_review()
    {
        await using var postgres = await PostgresShop.OpenAsync(AdminConnection);
        var (run, error, _) = await postgres.LockAndReloadAsync(
            new DateOnly(2025, 11, 1),
            new DateOnly(2025, 11, 30),
            enabled: true);
        Assert.Null(error);
        Assert.NotNull(run);

        var first = await postgres.ConfirmAsync(run.Run.Id, 1_999m, 500m, 200m);
        var line = await postgres.ReloadLineAsync(run.Run.Id);
        var stored = await postgres.ReloadRunAsync(run.Run.Id);
        Assert.Equal(1_999m, line.EmployerSss);
        Assert.Equal(500m, line.EmployerPhilHealth);
        Assert.Equal(200m, line.EmployerPagIbig);
        Assert.NotNull(stored.EmployerSharesReviewedAtUtc);
        Assert.Equal(postgres.StaffId, stored.EmployerSharesReviewedBy);
        Assert.Equal(1_999m, first!.Lines[0].EmployerSss);

        var reviewedAt = stored.EmployerSharesReviewedAtUtc;
        await Task.Delay(20);
        await postgres.ConfirmAsync(run.Run.Id, 2_030m, 500m, 200m);
        var again = await postgres.ReloadRunAsync(run.Run.Id);
        var edited = await postgres.ReloadLineAsync(run.Run.Id);
        Assert.Equal(2_030m, edited.EmployerSss);
        Assert.True(again.EmployerSharesReviewedAtUtc > reviewedAt);
        Assert.Equal(postgres.StaffId, again.EmployerSharesReviewedBy);
    }

    [Fact]
    public async Task Export_is_blocked_until_review_and_then_a_second_export_matches()
    {
        await using var postgres = await PostgresShop.OpenAsync(AdminConnection);
        var (run, error, _) = await postgres.LockAndReloadAsync(
            new DateOnly(2025, 11, 1),
            new DateOnly(2025, 11, 30),
            enabled: true);
        Assert.Null(error);
        Assert.NotNull(run);

        var (blocked, blocking, blockError) = await postgres.ExportAsync(2025, 11);
        Assert.Null(blocked);
        Assert.Equal(PayrollRemittanceService.ExportBlockedMessage, blockError);
        var pending = Assert.Single(blocking!);
        Assert.Equal(run.Run.Id, pending.RunId);
        Assert.Equal(new DateOnly(2025, 11, 1), pending.From);
        Assert.Equal(new DateOnly(2025, 11, 30), pending.To);
        Assert.Equal("North Desk", pending.Name);
        Assert.Equal(PayrollRemittanceService.NotReviewed, pending.Status);

        await postgres.ConfirmAsync(run.Run.Id, 2_030m, 500m, 200m);
        var (first, none, exportError) = await postgres.ExportAsync(2025, 11);
        Assert.Null(exportError);
        Assert.Null(none);
        Assert.NotNull(first);
        var csv = System.Text.Encoding.UTF8.GetString(first.Bytes);
        Assert.Contains("SSS,0.00,2030.00,2030.00", csv);

        var (second, _, secondError) = await postgres.ExportAsync(2025, 11);
        Assert.Null(secondError);
        Assert.Equal(csv, System.Text.Encoding.UTF8.GetString(second!.Bytes));
        var row = await postgres.ReloadExportAsync(2025, 11);
        Assert.Equal(postgres.StaffId, row.ExportedBy);
        Assert.NotEqual(default, row.ExportedAtUtc);
    }

    [Fact]
    public async Task Confirm_is_read_only_after_the_month_is_exported()
    {
        await using var postgres = await PostgresShop.OpenAsync(AdminConnection);
        var (run, _, _) = await postgres.LockAndReloadAsync(
            new DateOnly(2025, 11, 1),
            new DateOnly(2025, 11, 30),
            enabled: true);
        await postgres.ConfirmAsync(run!.Run.Id, 2_030m, 500m, 200m);
        await postgres.ExportAsync(2025, 11);

        var conflict = await postgres.ConfirmConflictAsync(run.Run.Id, 1m, 1m, 1m);
        Assert.Equal(PayrollRemittanceService.ExportedMonthConfirmMessage, conflict);
        var line = await postgres.ReloadLineAsync(run.Run.Id);
        Assert.Equal(2_030m, line.EmployerSss);
        Assert.Equal(500m, line.EmployerPhilHealth);
        Assert.Equal(200m, line.EmployerPagIbig);
    }

    [Fact]
    public async Task Void_clears_a_review_and_is_refused_after_export()
    {
        await using var postgres = await PostgresShop.OpenAsync(AdminConnection);
        var (run, _, _) = await postgres.LockAndReloadAsync(
            new DateOnly(2025, 11, 1),
            new DateOnly(2025, 11, 30),
            enabled: true);
        await postgres.ConfirmAsync(run!.Run.Id, 2_030m, 500m, 200m);

        var (voided, voidError) = await postgres.VoidAsync(run.Run.Id);
        Assert.Null(voidError);
        Assert.NotNull(voided);
        var cleared = await postgres.ReloadLineAsync(run.Run.Id);
        var stored = await postgres.ReloadRunAsync(run.Run.Id);
        Assert.Null(cleared.EmployerSss);
        Assert.Null(cleared.EmployerPhilHealth);
        Assert.Null(cleared.EmployerPagIbig);
        Assert.Null(stored.EmployerSharesReviewedAtUtc);
        Assert.Null(stored.EmployerSharesReviewedBy);

        var (again, _, _) = await postgres.LockAndReloadAsync(
            new DateOnly(2025, 12, 1),
            new DateOnly(2025, 12, 31),
            enabled: true);
        await postgres.ConfirmAsync(again!.Run.Id, 2_030m, 500m, 200m);
        await postgres.ExportAsync(2025, 12);
        var (blocked, blockError) = await postgres.VoidAsync(again.Run.Id);
        Assert.Null(blocked);
        Assert.Equal(PayrollRemittanceService.ExportedMonthVoidMessage, blockError);
        var kept = await postgres.ReloadLineAsync(again.Run.Id);
        Assert.Equal(2_030m, kept.EmployerSss);
    }

    private sealed class PostgresShop : IAsyncDisposable
    {
        private readonly string databaseName;
        private readonly string adminConnection;
        private readonly TestHrDb db;
        private readonly HrCoreFeatures features;
        private readonly PayrollRunService runs;
        private readonly PayrollRemittanceService remittance;
        private readonly Guid staffId;

        private PostgresShop(
            string databaseName,
            string adminConnection,
            TestHrDb db,
            HrCoreFeatures features,
            PayrollRunService runs,
            PayrollRemittanceService remittance,
            Guid staffId)
        {
            this.databaseName = databaseName;
            this.adminConnection = adminConnection;
            this.db = db;
            this.features = features;
            this.runs = runs;
            this.remittance = remittance;
            this.staffId = staffId;
        }

        public Guid StaffId => staffId;

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
            var staffId = await SeedAsync(db);
            var payroll = new PayrollService(db, features);
            var remittance = new PayrollRemittanceService(db, features, payroll, new SettingsChangeLogService(db));
            var runs = new PayrollRunService(db, new HrChangeLogService(db), payroll, remittance);
            return new PostgresShop(databaseName, adminConnection, db, features, runs, remittance, staffId);
        }

        public async Task<(PayrollRunDetailDto? Run, string? Error, PayrollRunLine Line)> LockAndReloadAsync(
            DateOnly from,
            DateOnly to,
            bool enabled)
        {
            var branch = await db.Branches.SingleAsync();
            if (enabled && !await db.BranchRemittanceSettings().AnyAsync(row => row.BranchId == branch.Id))
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

        public async Task<EmployerShareReviewDto?> ConfirmAsync(
            Guid runId,
            decimal sss,
            decimal philHealth,
            decimal pagIbig)
        {
            var branch = await db.Branches.SingleAsync();
            var (saved, errors, conflict) = await remittance.ConfirmAsync(
                [branch],
                runId,
                Share(sss, philHealth, pagIbig),
                staffId,
                false,
                CancellationToken.None);
            Assert.Null(errors);
            Assert.Null(conflict);
            return saved;
        }

        public async Task<string?> ConfirmConflictAsync(
            Guid runId,
            decimal sss,
            decimal philHealth,
            decimal pagIbig)
        {
            var branch = await db.Branches.SingleAsync();
            var (_, errors, conflict) = await remittance.ConfirmAsync(
                [branch],
                runId,
                Share(sss, philHealth, pagIbig),
                staffId,
                false,
                CancellationToken.None);
            Assert.Null(errors);
            return conflict;
        }

        public async Task<(PayrollCsvFile? File, IReadOnlyList<RemittanceRunRefDto>? Blocking, string? Error)> ExportAsync(
            int year,
            int month)
        {
            var branch = await db.Branches.SingleAsync();
            return await remittance.ExportAsync([branch], branch.Id, year, month, staffId, CancellationToken.None);
        }

        public Task<(PayrollRunDetailDto? Run, string? Error)> VoidAsync(Guid runId)
        {
            return runs.VoidAsync(db.Branches.ToList(), runId, staffId, CancellationToken.None);
        }

        public Task<PayrollRun> ReloadRunAsync(Guid runId) =>
            db.PayrollRuns.AsNoTracking().SingleAsync(run => run.Id == runId);

        public Task<PayrollRunLine> ReloadLineAsync(Guid runId) =>
            db.PayrollRunLines.AsNoTracking().SingleAsync(line => line.RunId == runId);

        public Task<BranchRemittanceExport> ReloadExportAsync(int year, int month) =>
            db.BranchRemittanceExports().AsNoTracking().SingleAsync(row => row.Year == year && row.Month == month);

        private ConfirmEmployerSharesRequest Share(decimal sss, decimal philHealth, decimal pagIbig)
        {
            return new ConfirmEmployerSharesRequest
            {
                Lines =
                [
                    new ConfirmEmployerShareLine
                    {
                        StaffId = staffId,
                        EmployerSss = sss,
                        EmployerPhilHealth = philHealth,
                        EmployerPagIbig = pagIbig
                    }
                ]
            };
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

        private static async Task<Guid> SeedAsync(TestHrDb db)
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
                Id = staffId,
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
            return staffId;
        }
    }
}
