using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HrCore.Data;
using HrCore.Domain;
using HrCore.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HrCore.Tests;

public sealed class PayrollSchedulesFlagTests
{
    private static readonly Type[] ScheduleTypes =
    [
        typeof(BranchPayrollCutoff),
        typeof(BranchDeductionSchedule),
        typeof(PayrollCatchUp)
    ];

    [Fact]
    public void Flag_off_omits_schedule_entities_from_the_model()
    {
        var options = new DbContextOptionsBuilder<TestHrDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new TestHrDb(options);
        var types = db.Model.GetEntityTypes().Select(entity => entity.ClrType).ToHashSet();

        Assert.DoesNotContain(typeof(BranchPayrollCutoff), types);
        Assert.DoesNotContain(typeof(BranchDeductionSchedule), types);
        Assert.DoesNotContain(typeof(PayrollCatchUp), types);
        Assert.DoesNotContain(typeof(BranchRemittanceSetting), types);
        Assert.DoesNotContain(typeof(BranchRemittanceExport), types);
        Assert.DoesNotContain(typeof(LoanKind), types);
        var loan = db.Model.FindEntityType(typeof(StaffLoan));
        Assert.NotNull(loan);
        Assert.Null(loan.FindProperty(nameof(StaffLoan.LoanKindId)));
        Assert.Contains(typeof(Staff), types);
    }

    [Fact]
    public void Flag_on_includes_schedule_entities_in_the_model()
    {
        var built = new DbContextOptionsBuilder<TestHrDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .UseHrCoreFeatures(new HrCoreFeatures { PayrollSchedules = true });
        using var db = new TestHrDb((DbContextOptions<TestHrDb>)built.Options);
        var types = db.Model.GetEntityTypes().Select(entity => entity.ClrType).ToHashSet();

        foreach (var scheduleType in ScheduleTypes)
        {
            Assert.Contains(scheduleType, types);
        }

        Assert.DoesNotContain(typeof(BranchRemittanceSetting), types);
        var line = db.Model.FindEntityType(typeof(PayrollRunLine));
        Assert.NotNull(line);
        Assert.Null(line.FindProperty(nameof(PayrollRunLine.EmployerSss)));
        Assert.Null(line.FindProperty(nameof(PayrollRunLine.EmployerPhilHealth)));
        Assert.Null(line.FindProperty(nameof(PayrollRunLine.EmployerPagIbig)));
        var run = db.Model.FindEntityType(typeof(PayrollRun));
        Assert.NotNull(run);
        Assert.Null(run.FindProperty(nameof(PayrollRun.EmployerSharesReviewedAtUtc)));
        Assert.DoesNotContain(typeof(BranchRemittanceExport), types);
        Assert.DoesNotContain(typeof(LoanKind), types);
        var loan = db.Model.FindEntityType(typeof(StaffLoan));
        Assert.NotNull(loan);
        Assert.Null(loan.FindProperty(nameof(StaffLoan.LoanKindId)));
    }

    [Fact]
    public void Loan_kinds_flag_on_maps_the_table_and_a_nullable_fk()
    {
        var built = new DbContextOptionsBuilder<TestHrDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .UseHrCoreFeatures(new HrCoreFeatures { LoanKinds = true });
        using var db = new TestHrDb((DbContextOptions<TestHrDb>)built.Options);
        var types = db.Model.GetEntityTypes().Select(entity => entity.ClrType).ToHashSet();

        Assert.Contains(typeof(LoanKind), types);
        Assert.DoesNotContain(typeof(BranchPayrollCutoff), types);
        var kind = db.Model.FindEntityType(typeof(LoanKind));
        Assert.NotNull(kind);
        Assert.Equal(80, kind.FindProperty(nameof(LoanKind.Name))!.GetMaxLength());
        Assert.True(kind.FindProperty(nameof(LoanKind.CutoffOrdinal))!.IsNullable);
        var loan = db.Model.FindEntityType(typeof(StaffLoan));
        Assert.NotNull(loan);
        var link = loan.FindProperty(nameof(StaffLoan.LoanKindId));
        Assert.NotNull(link);
        Assert.True(link.IsNullable);
        Assert.Equal(12, loan.FindProperty(nameof(StaffLoan.Principal))!.GetPrecision());
        Assert.Equal(2, loan.FindProperty(nameof(StaffLoan.Principal))!.GetScale());
    }

    [Fact]
    public void Remittance_flag_on_includes_the_setting_and_export_tables()
    {
        var built = new DbContextOptionsBuilder<TestHrDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .UseHrCoreFeatures(new HrCoreFeatures { EmployerRemittance = true });
        using var db = new TestHrDb((DbContextOptions<TestHrDb>)built.Options);
        var types = db.Model.GetEntityTypes().Select(entity => entity.ClrType).ToHashSet();

        Assert.Contains(typeof(BranchRemittanceSetting), types);
        Assert.Contains(typeof(BranchRemittanceExport), types);
        Assert.DoesNotContain(typeof(BranchPayrollCutoff), types);
        var run = db.Model.FindEntityType(typeof(PayrollRun));
        Assert.NotNull(run);
        Assert.NotNull(run.FindProperty(nameof(PayrollRun.EmployerSharesReviewedAtUtc)));
        Assert.NotNull(run.FindProperty(nameof(PayrollRun.EmployerSharesReviewedBy)));
    }

    [Fact]
    public async Task Flag_off_remittance_routes_are_404()
    {
        await using var app = await FlagApp.StartAsync(payrollSchedules: false);
        foreach (var url in new[]
        {
            "/api/ops/payroll/remittance",
            "/api/ops/payroll/remittance?month=2026-11",
            "/api/ops/payroll/remittance/export?month=2026-11"
        })
        {
            var response = await app.SendAsync(HttpMethod.Get, url);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        var put = await app.SendAsync(HttpMethod.Put, "/api/ops/payroll/remittance");
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }

    [Fact]
    public async Task Flag_off_schedule_routes_are_404()
    {
        await using var app = await FlagApp.StartAsync(payrollSchedules: false);
        foreach (var (method, url) in ScheduleRoutes())
        {
            var response = await app.SendAsync(method, url);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task Empty_schedule_tables_keep_the_same_register_totals_with_the_flag_off_or_on()
    {
        await using var off = await FlagApp.StartAsync(payrollSchedules: false);
        await using var on = await FlagApp.StartAsync(payrollSchedules: true);

        var offBody = await RegisterAsync(off);
        var onBody = await RegisterAsync(on);

        Assert.True(offBody.TotalAmount > 0, "The seeded monthly rate should produce a register total.");
        Assert.Equal(offBody.TotalAmount, onBody.TotalAmount);
        Assert.Equal(offBody.TotalNet, onBody.TotalNet);
        Assert.True(offBody.TotalNet < offBody.TotalAmount, "The loan instalment should come off both totals.");
    }

    private static async Task<(decimal TotalAmount, decimal TotalNet)> RegisterAsync(FlagApp app)
    {
        var response = await app.SendAsync(
            HttpMethod.Get,
            "/api/ops/payroll?from=2026-11-01&to=2026-11-30");
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, json);
        var body = JsonSerializer.Deserialize<JsonElement>(json);
        return (body.GetProperty("totalAmount").GetDecimal(), body.GetProperty("totalNet").GetDecimal());
    }

    private static IEnumerable<(HttpMethod Method, string Url)> ScheduleRoutes()
    {
        yield return (HttpMethod.Get, "/api/ops/payroll/cutoff-schedule");
        yield return (HttpMethod.Put, "/api/ops/payroll/cutoff-schedule");
        yield return (HttpMethod.Get, "/api/ops/payroll/deduction-schedule");
        yield return (HttpMethod.Put, "/api/ops/payroll/deduction-schedule");
        yield return (HttpMethod.Put, "/api/ops/payroll/catch-ups");
        yield return (HttpMethod.Get, "/api/ops/payroll/loan-kinds");
        yield return (HttpMethod.Post, "/api/ops/payroll/loan-kinds");
        yield return (HttpMethod.Put, "/api/ops/payroll/loan-kinds/" + Guid.Empty);
        yield return (HttpMethod.Delete, "/api/ops/payroll/loan-kinds/" + Guid.Empty);
    }

    private sealed class FlagApp : IAsyncDisposable
    {
        public const string OwnerId = "alex-rivera";
        public static readonly Guid BranchId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        public static readonly Guid WorkerStaffId = Guid.Parse("22222222-2222-4222-8222-222222222222");

        private readonly WebApplication app;

        private FlagApp(WebApplication app, HttpClient client)
        {
            this.app = app;
            Client = client;
        }

        private HttpClient Client { get; }

        public static async Task<FlagApp> StartAsync(bool payrollSchedules)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Testing",
                ApplicationName = "HrCore.Tests"
            });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            var features = new HrCoreFeatures { PayrollSchedules = payrollSchedules };
            var databaseName = Guid.NewGuid().ToString();
            var databaseRoot = new InMemoryDatabaseRoot();
            builder.Services.AddDbContext<TestHrDb>(options =>
                options.UseInMemoryDatabase(databaseName, databaseRoot).UseHrCoreFeatures(features));
            builder.Services.AddHrCore<TestHrDb>(features);
            builder.Services.AddSingleton(new DemoBranchOptions { Enabled = false });
            builder.Services.AddSingleton<IStaffInvite, NoStaffInvite>();
            builder.Services.AddSingleton<IFileStore, NoFileStore>();
            builder.Services.AddSingleton<IHrFileStore, NoHrFileStore>();

            var app = builder.Build();
            app.MapHrCore();
            await app.StartAsync();
            var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
            var host = new FlagApp(app, client);
            await host.SeedAsync();
            return host;
        }

        public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url)
        {
            var message = new HttpRequestMessage(method, url);
            message.Headers.TryAddWithoutValidation(StaffAuthorizationFilter.TestAuthUserHeader, OwnerId);
            message.Headers.TryAddWithoutValidation(BranchResolver.HeaderName, "north-desk");
            if (method == HttpMethod.Put)
            {
                message.Content = JsonContent.Create(new { });
            }

            return Client.SendAsync(message);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }

        private async Task SeedAsync()
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TestHrDb>();
            db.Database.EnsureCreated();
            var now = DateTime.UtcNow;
            db.Organizations.Add(new Organization
            {
                Id = PlatformIds.OrganizationId,
                LegalName = "North Desk Co",
                TradingName = "North Desk",
                CreatedAtUtc = now
            });
            db.Branches.Add(new Branch
            {
                Id = BranchId,
                OrganizationId = PlatformIds.OrganizationId,
                Code = "north-desk",
                Name = "North Desk",
                IsLive = true,
                Kind = BranchKind.Owned,
                CreatedAtUtc = now
            });
            var ownerProfile = new Profile
            {
                Id = Guid.NewGuid(),
                AuthUserId = OwnerId,
                Kind = ProfileKind.Staff,
                DisplayName = "Alex Rivera",
                Email = "alex@example.com",
                CreatedAtUtc = now
            };
            db.Profiles.Add(ownerProfile);
            db.Staff.Add(new Staff
            {
                Id = Guid.NewGuid(),
                ProfileId = ownerProfile.Id,
                Role = StaffRole.Owner,
                Active = true,
                CreatedAtUtc = now
            });
            var workerProfile = new Profile
            {
                Id = Guid.NewGuid(),
                AuthUserId = "casey-diaz",
                Kind = ProfileKind.Staff,
                DisplayName = "Casey Diaz",
                Email = "casey@example.com",
                CreatedAtUtc = now
            };
            db.Profiles.Add(workerProfile);
            db.Staff.Add(new Staff
            {
                Id = WorkerStaffId,
                ProfileId = workerProfile.Id,
                BranchId = BranchId,
                Role = StaffRole.Worker,
                Active = true,
                PayBasis = PayBasis.Monthly,
                RatePhp = 30000m,
                MustClock = false,
                CreatedAtUtc = now
            });
            db.StaffLoans.Add(new StaffLoan
            {
                Id = Guid.NewGuid(),
                StaffId = WorkerStaffId,
                Kind = "Cash advance",
                Principal = 1000m,
                PerPeriodAmount = 100m,
                StartPeriod = new DateOnly(2026, 1, 31),
                CreatedAtUtc = now
            });
            await db.SaveChangesAsync();
        }

        private sealed class NoStaffInvite : IStaffInvite
        {
            public Task<StaffInviteResult> InviteAsync(
                string staffEmail,
                string displayName,
                CancellationToken cancellationToken) =>
                Task.FromResult(new StaffInviteResult(true, null, false, null));
        }

        private sealed class NoFileStore : IFileStore
        {
            public Task<string> SaveAsync(
                string storagePath,
                Stream content,
                string contentType,
                CancellationToken cancellationToken) =>
                Task.FromResult(storagePath);

            public Task<Stream?> OpenAsync(string storagePath, CancellationToken cancellationToken) =>
                Task.FromResult<Stream?>(null);
        }

        private sealed class NoHrFileStore : IHrFileStore
        {
            public Task<string> SaveAsync(
                string storagePath,
                Stream content,
                string contentType,
                CancellationToken cancellationToken) =>
                Task.FromResult(storagePath);

            public Task<Stream?> OpenAsync(string storagePath, CancellationToken cancellationToken) =>
                Task.FromResult<Stream?>(null);
        }
    }
}
