using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HrCore.Data;
using HrCore.Domain;
using HrCore.Features.Payroll;
using HrCore.Features.SettingsLog;
using HrCore.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HrCore.Tests;

public sealed class CutoffApp : IAsyncLifetime
{
    public const string OwnerId = "alex-rivera";
    public const string AdminId = "sam-lee";
    public const string WorkerId = "casey-diaz";
    public static readonly Guid BranchId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    private WebApplication app = null!;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = "HrCore.Tests"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot();
        builder.Services.AddDbContext<TestHrDb>(options =>
            options.UseInMemoryDatabase(databaseName, databaseRoot));
        builder.Services.AddHrCore<TestHrDb>();
        builder.Services.AddSingleton(new DemoBranchOptions { Enabled = false });
        builder.Services.AddSingleton<IStaffInvite, NoStaffInvite>();
        builder.Services.AddSingleton<IFileStore, NoFileStore>();
        builder.Services.AddSingleton<IHrFileStore, NoHrFileStore>();

        app = builder.Build();
        app.MapHrCore();
        await app.StartAsync();
        Client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
        await SeedAsync();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await app.StopAsync();
        await app.DisposeAsync();
    }

    public async Task ResetAsync()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestHrDb>();
        db.BranchPayrollCutoffs().RemoveRange(db.BranchPayrollCutoffs());
        db.BranchDeductionSchedules().RemoveRange(db.BranchDeductionSchedules());
        db.PayrollCatchUps().RemoveRange(db.PayrollCatchUps());
        db.SettingsChangeLogs.RemoveRange(db.SettingsChangeLogs);
        db.PayrollRuns.RemoveRange(db.PayrollRuns);
        await db.SaveChangesAsync();
    }

    public Task<HttpResponseMessage> SendAsync(string userId, HttpMethod method, string url, object? body = null)
    {
        var message = new HttpRequestMessage(method, url);
        message.Headers.TryAddWithoutValidation(StaffAuthorizationFilter.TestAuthUserHeader, userId);
        message.Headers.TryAddWithoutValidation(BranchResolver.HeaderName, "north-desk");
        if (body is not null)
        {
            message.Content = JsonContent.Create(body);
        }

        return Client.SendAsync(message);
    }

    public async Task<TestHrDb> ReadAsync(Func<TestHrDb, Task> read)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestHrDb>();
        await read(db);
        return db;
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
        AddPerson(db, OwnerId, "Alex Rivera", StaffRole.Owner, null, now);
        AddPerson(db, AdminId, "Sam Lee", StaffRole.Admin, BranchId, now);
        AddPerson(db, WorkerId, "Casey Diaz", StaffRole.Worker, BranchId, now);
        await db.SaveChangesAsync();
    }

    private sealed class NoStaffInvite : IStaffInvite
    {
        public Task<StaffInviteResult> InviteAsync(string staffEmail, string displayName, CancellationToken cancellationToken) =>
            Task.FromResult(new StaffInviteResult(true, null, false, null));
    }

    private sealed class NoFileStore : IFileStore
    {
        public Task<string> SaveAsync(string storagePath, Stream content, string contentType, CancellationToken cancellationToken) =>
            Task.FromResult(storagePath);

        public Task<Stream?> OpenAsync(string storagePath, CancellationToken cancellationToken) =>
            Task.FromResult<Stream?>(null);
    }

    private sealed class NoHrFileStore : IHrFileStore
    {
        public Task<string> SaveAsync(string storagePath, Stream content, string contentType, CancellationToken cancellationToken) =>
            Task.FromResult(storagePath);

        public Task<Stream?> OpenAsync(string storagePath, CancellationToken cancellationToken) =>
            Task.FromResult<Stream?>(null);
    }

    private static void AddPerson(TestHrDb db, string authUserId, string name, StaffRole role, Guid? branchId, DateTime now)
    {
        var profile = new Profile
        {
            Id = Guid.NewGuid(),
            AuthUserId = authUserId,
            Kind = ProfileKind.Staff,
            DisplayName = name,
            Email = $"{authUserId}@example.com",
            CreatedAtUtc = now
        };
        db.Profiles.Add(profile);
        db.Staff.Add(new Staff
        {
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            BranchId = branchId,
            Role = role,
            Active = true,
            CreatedAtUtc = now
        });
    }
}

public sealed class PayrollCutoffEndpointTests(CutoffApp app) : IClassFixture<CutoffApp>
{
    private static object Schedule() => new
    {
        mode = "schedule",
        cutoffs = new object[]
        {
            new { ordinal = 1, startDay = 26, endDay = 10 },
            new { ordinal = 2, startDay = 11, endDay = 25 }
        }
    };

    [Fact]
    public async Task Get_with_no_rows_is_free_dates()
    {
        await app.ResetAsync();

        var body = await ReadOk(await app.SendAsync(
            CutoffApp.OwnerId, HttpMethod.Get, "/api/ops/payroll/cutoff-schedule?month=2026-11"));

        Assert.Equal("free", body.GetProperty("mode").GetString());
        Assert.Equal("2026-11", body.GetProperty("month").GetString());
        Assert.Equal(0, body.GetProperty("cutoffs").GetArrayLength());
        Assert.Equal(0, body.GetProperty("periods").GetArrayLength());
        Assert.Equal(CutoffApp.BranchId, body.GetProperty("branchId").GetGuid());
    }

    [Fact]
    public async Task Owner_can_save_a_schedule_and_read_november_periods()
    {
        await app.ResetAsync();

        var saved = await ReadOk(await app.SendAsync(
            CutoffApp.OwnerId, HttpMethod.Put, "/api/ops/payroll/cutoff-schedule?month=2026-11", Schedule()));

        Assert.Equal("schedule", saved.GetProperty("mode").GetString());
        Assert.Equal("2026-10-26", saved.GetProperty("periods")[0].GetProperty("from").GetString());
        Assert.Equal("2026-11-10", saved.GetProperty("periods")[0].GetProperty("to").GetString());
        Assert.Equal("2026-11-11", saved.GetProperty("periods")[1].GetProperty("from").GetString());
        Assert.Equal("2026-11-25", saved.GetProperty("periods")[1].GetProperty("to").GetString());

        var february = await ReadOk(await app.SendAsync(
            CutoffApp.OwnerId, HttpMethod.Get, "/api/ops/payroll/cutoff-schedule?month=2026-02"));
        Assert.Equal("2026-01-26", february.GetProperty("periods")[0].GetProperty("from").GetString());
        Assert.Equal("2026-02-10", february.GetProperty("periods")[0].GetProperty("to").GetString());
        Assert.Equal("2026-02-11", february.GetProperty("periods")[1].GetProperty("from").GetString());
        Assert.Equal("2026-02-25", february.GetProperty("periods")[1].GetProperty("to").GetString());

        await app.ReadAsync(async db =>
        {
            var log = await db.SettingsChangeLogs.SingleAsync();
            Assert.Equal(SettingsArea.PayrollCutoff, log.Area);
            Assert.Equal("schedule", log.Field);
            Assert.Equal(PayrollCutoffMode.Free, log.OldValue);
            Assert.Equal("1:26-10;2:11-25", log.NewValue);
            Assert.Equal(CutoffApp.BranchId, log.BranchId);
        });
    }

    [Fact]
    public async Task Admin_can_save()
    {
        await app.ResetAsync();

        var response = await app.SendAsync(
            CutoffApp.AdminId, HttpMethod.Put, "/api/ops/payroll/cutoff-schedule?month=2026-11", Schedule());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Overlap_is_400_and_stores_nothing()
    {
        await app.ResetAsync();
        var body = new
        {
            mode = "schedule",
            cutoffs = new object[]
            {
                new { ordinal = 1, startDay = 26, endDay = 10 },
                new { ordinal = 2, startDay = 10, endDay = 25 }
            }
        };

        var problem = await ReadProblem(
            await app.SendAsync(CutoffApp.OwnerId, HttpMethod.Put, "/api/ops/payroll/cutoff-schedule", body),
            StatusCodes.Status400BadRequest);

        Assert.Contains(
            "Day 10 is already in cut-off 1.",
            problem.GetProperty("errors").GetProperty("cutoffs[1].startDay").EnumerateArray().Select(item => item.GetString()));
        await app.ReadAsync(async db =>
        {
            Assert.Empty(await db.BranchPayrollCutoffs().ToListAsync());
            Assert.Empty(await db.SettingsChangeLogs.ToListAsync());
        });
    }

    [Fact]
    public async Task Gap_is_400_with_a_field_error()
    {
        await app.ResetAsync();
        var body = new
        {
            mode = "schedule",
            cutoffs = new object[] { new { ordinal = 1, startDay = 11, endDay = 25 } }
        };

        var problem = await ReadProblem(
            await app.SendAsync(CutoffApp.OwnerId, HttpMethod.Put, "/api/ops/payroll/cutoff-schedule", body),
            StatusCodes.Status400BadRequest);

        var message = problem.GetProperty("errors").GetProperty("cutoffs")[0].GetString();
        Assert.Contains("Days not covered: 1-10, 26-31", message);
    }

    [Fact]
    public async Task Unknown_mode_and_month_are_400()
    {
        await app.ResetAsync();

        var mode = await ReadProblem(
            await app.SendAsync(
                CutoffApp.OwnerId,
                HttpMethod.Put,
                "/api/ops/payroll/cutoff-schedule",
                new { mode = "weekly" }),
            StatusCodes.Status400BadRequest);
        Assert.Contains("Mode must be free or schedule.", Field(mode, "mode"));

        var month = await ReadProblem(
            await app.SendAsync(CutoffApp.OwnerId, HttpMethod.Get, "/api/ops/payroll/cutoff-schedule?month=2026-13"),
            StatusCodes.Status400BadRequest);
        Assert.Contains("Month must be yyyy-MM.", Field(month, "month"));
    }

    [Fact]
    public async Task Month_must_be_strict_yyyy_MM_and_defaults_when_missing()
    {
        await app.ResetAsync();
        foreach (var bad in new[] { "2026-1", "2026-13", "26-01", "nope" })
        {
            var problem = await ReadProblem(
                await app.SendAsync(
                    CutoffApp.OwnerId,
                    HttpMethod.Get,
                    "/api/ops/payroll/cutoff-schedule?month=" + Uri.EscapeDataString(bad)),
                StatusCodes.Status400BadRequest);
            Assert.Contains("Month must be yyyy-MM.", Field(problem, "month"));
        }

        var open = await ReadOk(await app.SendAsync(
            CutoffApp.OwnerId, HttpMethod.Get, "/api/ops/payroll/cutoff-schedule"));
        var today = ManilaTime.Today();
        Assert.Equal($"{today.Year:0000}-{today.Month:00}", open.GetProperty("month").GetString());
        Assert.Equal("free", open.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task Register_returns_each_end_month_when_the_range_crosses_months()
    {
        await app.ResetAsync();
        Assert.Equal(HttpStatusCode.OK, (await app.SendAsync(
            CutoffApp.OwnerId,
            HttpMethod.Put,
            "/api/ops/payroll/cutoff-schedule?month=2026-11",
            Schedule())).StatusCode);

        var crossed = await ReadOk(await app.SendAsync(
            CutoffApp.OwnerId,
            HttpMethod.Get,
            "/api/ops/payroll?from=2026-12-26&to=2027-01-10"));
        var periods = crossed.GetProperty("cutoffPeriods");
        Assert.Equal(4, periods.GetArrayLength());
        AssertPeriod(periods[0], 1, "2026-11-26", "2026-12-10");
        AssertPeriod(periods[1], 2, "2026-12-11", "2026-12-25");
        AssertPeriod(periods[2], 1, "2026-12-26", "2027-01-10");
        AssertPeriod(periods[3], 2, "2027-01-11", "2027-01-25");

        var sameMonth = await ReadOk(await app.SendAsync(
            CutoffApp.OwnerId,
            HttpMethod.Get,
            "/api/ops/payroll?from=2026-11-11&to=2026-11-25"));
        Assert.Equal(2, sameMonth.GetProperty("cutoffPeriods").GetArrayLength());
    }

    private static void AssertPeriod(JsonElement period, int ordinal, string from, string to)
    {
        Assert.Equal(ordinal, period.GetProperty("ordinal").GetInt32());
        Assert.Equal(from, period.GetProperty("from").GetString());
        Assert.Equal(to, period.GetProperty("to").GetString());
    }

    [Fact]
    public async Task Worker_gets_403_problem_details()
    {
        await app.ResetAsync();

        var get = await ReadProblem(
            await app.SendAsync(CutoffApp.WorkerId, HttpMethod.Get, "/api/ops/payroll/cutoff-schedule"),
            StatusCodes.Status403Forbidden);
        Assert.Contains("not available for your role", get.GetProperty("detail").GetString());

        var put = await ReadProblem(
            await app.SendAsync(CutoffApp.WorkerId, HttpMethod.Put, "/api/ops/payroll/cutoff-schedule", Schedule()),
            StatusCodes.Status403Forbidden);
        Assert.Equal(StatusCodes.Status403Forbidden, put.GetProperty("status").GetInt32());
        await app.ReadAsync(async db => Assert.Empty(await db.BranchPayrollCutoffs().ToListAsync()));
    }

    [Fact]
    public async Task Saving_free_dates_clears_a_schedule()
    {
        await app.ResetAsync();
        Assert.Equal(HttpStatusCode.OK, (await app.SendAsync(
            CutoffApp.OwnerId, HttpMethod.Put, "/api/ops/payroll/cutoff-schedule?month=2026-11", Schedule())).StatusCode);

        var cleared = await ReadOk(await app.SendAsync(
            CutoffApp.OwnerId, HttpMethod.Put, "/api/ops/payroll/cutoff-schedule?month=2026-11", new { mode = "free" }));

        Assert.Equal("free", cleared.GetProperty("mode").GetString());
        Assert.Equal(0, cleared.GetProperty("periods").GetArrayLength());
        await app.ReadAsync(async db => Assert.Empty(await db.BranchPayrollCutoffs().ToListAsync()));
    }

    [Fact]
    public async Task A_second_identical_save_does_not_add_another_log_row()
    {
        await app.ResetAsync();
        await app.SendAsync(CutoffApp.OwnerId, HttpMethod.Put, "/api/ops/payroll/cutoff-schedule", Schedule());
        await app.SendAsync(CutoffApp.OwnerId, HttpMethod.Put, "/api/ops/payroll/cutoff-schedule", Schedule());

        await app.ReadAsync(async db => Assert.Equal(1, await db.SettingsChangeLogs.CountAsync()));
    }

    [Fact]
    public async Task Saving_a_schedule_does_not_change_a_locked_run()
    {
        await app.ResetAsync();
        var runId = Guid.NewGuid();
        await app.ReadAsync(async db =>
        {
            db.PayrollRuns.Add(new PayrollRun
            {
                Id = runId,
                BranchId = CutoffApp.BranchId,
                PeriodFrom = new DateOnly(2026, 10, 26),
                PeriodTo = new DateOnly(2026, 11, 10),
                Status = PayrollRunStatus.Locked,
                TotalGross = 1000m,
                TotalDeductions = 100m,
                TotalNet = 900m,
                Note = "Frozen",
                CreatedAtUtc = new DateTime(2026, 11, 10, 1, 0, 0, DateTimeKind.Utc),
                LockedAtUtc = new DateTime(2026, 11, 10, 2, 0, 0, DateTimeKind.Utc)
            });
            await db.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.OK, (await app.SendAsync(
            CutoffApp.OwnerId, HttpMethod.Put, "/api/ops/payroll/cutoff-schedule?month=2026-11", Schedule())).StatusCode);

        var register = await ReadOk(await app.SendAsync(
            CutoffApp.OwnerId,
            HttpMethod.Get,
            "/api/ops/payroll?from=2026-10-26&to=2026-11-10"));
        Assert.Equal("locked", register.GetProperty("source").GetString());
        Assert.Equal(runId, register.GetProperty("runId").GetGuid());
        Assert.Equal("schedule", register.GetProperty("cutoffMode").GetString());

        await app.ReadAsync(async db =>
        {
            var run = await db.PayrollRuns.SingleAsync();
            Assert.Equal(runId, run.Id);
            Assert.Equal(new DateOnly(2026, 10, 26), run.PeriodFrom);
            Assert.Equal(new DateOnly(2026, 11, 10), run.PeriodTo);
            Assert.Equal(PayrollRunStatus.Locked, run.Status);
            Assert.Equal(1000m, run.TotalGross);
            Assert.Equal(100m, run.TotalDeductions);
            Assert.Equal(900m, run.TotalNet);
            Assert.Equal("Frozen", run.Note);
            Assert.Equal(new DateTime(2026, 11, 10, 2, 0, 0, DateTimeKind.Utc), run.LockedAtUtc);
        });
    }

    [Fact]
    public async Task Register_stays_on_the_requested_dates_and_reports_the_mode()
    {
        await app.ResetAsync();

        var free = await ReadOk(await app.SendAsync(
            CutoffApp.OwnerId, HttpMethod.Get, "/api/ops/payroll?from=2026-11-02&to=2026-11-08"));
        Assert.Equal("2026-11-02", free.GetProperty("from").GetString());
        Assert.Equal("2026-11-08", free.GetProperty("to").GetString());
        Assert.Equal("free", free.GetProperty("cutoffMode").GetString());
        Assert.Equal(0, free.GetProperty("cutoffPeriods").GetArrayLength());

        await app.SendAsync(
            CutoffApp.OwnerId, HttpMethod.Put, "/api/ops/payroll/cutoff-schedule?month=2026-11", Schedule());

        var scheduled = await ReadOk(await app.SendAsync(
            CutoffApp.OwnerId, HttpMethod.Get, "/api/ops/payroll?from=2026-11-11&to=2026-11-25"));
        Assert.Equal("2026-11-11", scheduled.GetProperty("from").GetString());
        Assert.Equal("2026-11-25", scheduled.GetProperty("to").GetString());
        Assert.Equal("live", scheduled.GetProperty("source").GetString());
        Assert.Equal("schedule", scheduled.GetProperty("cutoffMode").GetString());
        Assert.Equal("2026-10-26", scheduled.GetProperty("cutoffPeriods")[0].GetProperty("from").GetString());
        Assert.Equal("2026-11-10", scheduled.GetProperty("cutoffPeriods")[0].GetProperty("to").GetString());
        Assert.Equal("2026-11-11", scheduled.GetProperty("cutoffPeriods")[1].GetProperty("from").GetString());
        Assert.Equal("2026-11-25", scheduled.GetProperty("cutoffPeriods")[1].GetProperty("to").GetString());
    }

    private static IEnumerable<string?> Field(JsonElement problem, string name) =>
        problem.GetProperty("errors").GetProperty(name).EnumerateArray().Select(item => item.GetString());

    private static async Task<JsonElement> ReadOk(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.IsSuccessStatusCode,
            text.Length == 0 ? "(empty body)" : text[..Math.Min(text.Length, 2000)]);
        using var _ = response;
        return JsonSerializer.Deserialize<JsonElement>(text);
    }

    private static async Task<JsonElement> ReadProblem(HttpResponseMessage response, int status)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == (HttpStatusCode)status, text);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var _ = response;
        return JsonSerializer.Deserialize<JsonElement>(text);
    }
}
