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

public class RemittanceApp : IAsyncLifetime
{
    public const string OwnerId = "alex-rivera";
    public const string AdminId = "sam-lee";
    public const string WorkerId = "casey-diaz";
    public static readonly Guid BranchId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid WorkerStaffId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private WebApplication app = null!;

    public HttpClient Client { get; private set; } = null!;

    protected virtual void ConfigureAccess(IServiceCollection services)
    {
    }

    protected virtual void ConfigureReview(IServiceCollection services)
    {
    }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = "HrCore.Tests"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        var features = new HrCoreFeatures { EmployerRemittance = true };
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot();
        builder.Services.AddDbContext<TestHrDb>(options =>
            options.UseInMemoryDatabase(databaseName, databaseRoot).UseHrCoreFeatures(features));
        ConfigureAccess(builder.Services);
        ConfigureReview(builder.Services);
        builder.Services.AddSingleton(new HrCoreOptions { AllowTestAuthHeader = true });
        builder.Services.AddHrCore<TestHrDb>(features);
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
        await MutateAsync(async db =>
        {
            db.PayrollRunLines.RemoveRange(db.PayrollRunLines);
            db.PayrollRuns.RemoveRange(db.PayrollRuns);
            db.BranchRemittanceSettings().RemoveRange(db.BranchRemittanceSettings());
            db.SettingsChangeLogs.RemoveRange(db.SettingsChangeLogs);
            await Task.CompletedTask;
        });
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

    public async Task MutateAsync(Func<TestHrDb, Task> mutate)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestHrDb>();
        await mutate(db);
        await db.SaveChangesAsync();
    }

    public async Task<Guid> SeedRunAsync(
        PayrollRunStatus status,
        DateOnly from,
        DateOnly to,
        decimal sss,
        decimal philHealth,
        decimal pagIbig,
        decimal? employerSss,
        decimal? employerPhilHealth,
        decimal? employerPagIbig,
        bool reviewed = true)
    {
        var runId = Guid.NewGuid();
        await MutateAsync(db =>
        {
            db.PayrollRuns.Add(new PayrollRun
            {
                Id = runId,
                BranchId = BranchId,
                PeriodFrom = from,
                PeriodTo = to,
                Status = status,
                TotalGross = 20_000m,
                TotalDeductions = sss + philHealth + pagIbig,
                TotalNet = 20_000m - sss - philHealth - pagIbig,
                CreatedAtUtc = new DateTime(2026, 11, 28, 0, 0, 0, DateTimeKind.Utc),
                LockedAtUtc = new DateTime(2026, 11, 28, 0, 0, 0, DateTimeKind.Utc),
                EmployerSharesReviewedAtUtc = reviewed
                    ? new DateTime(2026, 11, 28, 1, 0, 0, DateTimeKind.Utc)
                    : null,
                EmployerSharesReviewedBy = reviewed ? WorkerStaffId : null
            });
            db.PayrollRunLines.Add(new PayrollRunLine
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                StaffId = WorkerStaffId,
                StaffName = "Casey Diaz",
                BranchId = BranchId,
                Gross = 20_000m,
                Sss = sss,
                PhilHealth = philHealth,
                PagIbig = pagIbig,
                Deductions = sss + philHealth + pagIbig,
                Net = 20_000m - sss - philHealth - pagIbig,
                EmployerSss = employerSss,
                EmployerPhilHealth = employerPhilHealth,
                EmployerPagIbig = employerPagIbig
            });
            return Task.CompletedTask;
        });
        return runId;
    }

    private async Task SeedAsync()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestHrDb>();
        db.Database.EnsureCreated();
        var now = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
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
        AddPerson(db, OwnerId, "Alex Rivera", StaffRole.Owner, null, Guid.NewGuid(), now);
        AddPerson(db, AdminId, "Sam Lee", StaffRole.Admin, BranchId, Guid.NewGuid(), now);
        AddPerson(db, WorkerId, "Casey Diaz", StaffRole.Worker, BranchId, WorkerStaffId, now, 20_000m);
        await db.SaveChangesAsync();
    }

    private static void AddPerson(
        TestHrDb db,
        string authUserId,
        string name,
        StaffRole role,
        Guid? branchId,
        Guid staffId,
        DateTime now,
        decimal? rate = null)
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
            Id = staffId,
            ProfileId = profile.Id,
            BranchId = branchId,
            Role = role,
            Active = true,
            PayBasis = rate is null ? null : PayBasis.Monthly,
            RatePhp = rate,
            MustClock = false,
            CreatedAtUtc = now
        });
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

public sealed class PayrollRemittanceEndpointTests(RemittanceApp app) : IClassFixture<RemittanceApp>
{
    [Fact]
    public async Task The_report_is_off_until_the_shop_turns_it_on_and_the_save_is_logged_once()
    {
        await app.ResetAsync();

        var off = await ReadOk(await app.SendAsync(
            RemittanceApp.OwnerId, HttpMethod.Get, "/api/ops/payroll/remittance?month=2026-11"));
        Assert.False(off.GetProperty("enabled").GetBoolean());
        Assert.Equal(0, off.GetProperty("agencies").GetArrayLength());
        Assert.False(off.TryGetProperty("emptyMessage", out _));

        var saved = await ReadOk(await app.SendAsync(
            RemittanceApp.OwnerId, HttpMethod.Put, "/api/ops/payroll/remittance", new { enabled = true }));
        Assert.True(saved.GetProperty("enabled").GetBoolean());
        Assert.Equal(RemittanceApp.BranchId, saved.GetProperty("branchId").GetGuid());

        var again = await app.SendAsync(
            RemittanceApp.OwnerId, HttpMethod.Put, "/api/ops/payroll/remittance", new { enabled = true });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        again.Dispose();

        await app.MutateAsync(async db =>
        {
            var logs = await db.SettingsChangeLogs.ToListAsync();
            var log = Assert.Single(logs);
            Assert.Equal(SettingsArea.PayrollRemittance, log.Area);
            Assert.Equal("enabled", log.Field);
            Assert.Equal("off", log.OldValue);
            Assert.Equal("on", log.NewValue);
        });
    }

    [Fact]
    public async Task November_totals_include_runs_that_end_in_the_month_and_skip_voided_runs()
    {
        await app.ResetAsync();
        await app.SendAsync(
            RemittanceApp.OwnerId, HttpMethod.Put, "/api/ops/payroll/remittance", new { enabled = true });
        await app.SeedRunAsync(
            PayrollRunStatus.Locked,
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 30),
            1_000m, 500m, 200m, 2_030m, 500m, 200m);
        await app.SeedRunAsync(
            PayrollRunStatus.Locked,
            new DateOnly(2026, 10, 26),
            new DateOnly(2026, 11, 10),
            500m, 250m, 100m, 1_015m, 250m, 100m);
        await app.SeedRunAsync(
            PayrollRunStatus.Locked,
            new DateOnly(2026, 11, 11),
            new DateOnly(2026, 11, 25),
            10m, 0m, 0m, null, null, null);
        await app.SeedRunAsync(
            PayrollRunStatus.Void,
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 15),
            9_999m, 9_999m, 9_999m, 9_999m, 9_999m, 9_999m);
        await app.SeedRunAsync(
            PayrollRunStatus.Locked,
            new DateOnly(2026, 11, 26),
            new DateOnly(2026, 12, 10),
            8_888m, 8_888m, 8_888m, 8_888m, 8_888m, 8_888m);

        var body = await ReadOk(await app.SendAsync(
            RemittanceApp.AdminId, HttpMethod.Get, "/api/ops/payroll/remittance?month=2026-11"));

        Assert.True(body.GetProperty("enabled").GetBoolean());
        Assert.Equal("2026-11", body.GetProperty("month").GetString());
        Assert.False(body.TryGetProperty("emptyMessage", out _));
        AssertAgency(body, "sss", 1_510m, 3_045m, 4_555m);
        AssertAgency(body, "philhealth", 750m, 750m, 1_500m);
        AssertAgency(body, "pagibig", 300m, 300m, 600m);
        Assert.Equal(2_560m, body.GetProperty("employeeShare").GetDecimal());
        Assert.Equal(4_095m, body.GetProperty("employerShare").GetDecimal());
        Assert.Equal(6_655m, body.GetProperty("total").GetDecimal());

        var csvResponse = await app.SendAsync(
            RemittanceApp.OwnerId, HttpMethod.Get, "/api/ops/payroll/remittance/export?month=2026-11");
        var csv = await csvResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, csvResponse.StatusCode);
        Assert.Equal("text/csv", csvResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains("payroll-remittance-2026-11.csv", csvResponse.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal(
            "Agency,Employee share,Employer share,Total\n" +
            "SSS,1510.00,3045.00,4555.00\n" +
            "PhilHealth,750.00,750.00,1500.00\n" +
            "Pag-IBIG,300.00,300.00,600.00\n" +
            "Total,2560.00,4095.00,6655.00\n",
            csv);
        csvResponse.Dispose();
    }

    [Fact]
    public async Task A_month_with_no_locked_run_says_there_is_nothing_to_remit()
    {
        await app.ResetAsync();
        await app.SendAsync(
            RemittanceApp.OwnerId, HttpMethod.Put, "/api/ops/payroll/remittance", new { enabled = true });
        await app.SeedRunAsync(
            PayrollRunStatus.Void,
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            100m, 100m, 100m, 100m, 100m, 100m);

        var body = await ReadOk(await app.SendAsync(
            RemittanceApp.OwnerId, HttpMethod.Get, "/api/ops/payroll/remittance?month=2026-09"));

        Assert.Equal(PayrollRemittance.EmptyMessage, body.GetProperty("emptyMessage").GetString());
        AssertAgency(body, "sss", 0m, 0m, 0m);
        Assert.Equal(0m, body.GetProperty("total").GetDecimal());

        var csvResponse = await app.SendAsync(
            RemittanceApp.OwnerId, HttpMethod.Get, "/api/ops/payroll/remittance/export?month=2026-09");
        var csv = await csvResponse.Content.ReadAsStringAsync();
        csvResponse.Dispose();
        Assert.Contains("SSS,0.00,0.00,0.00", csv);
        Assert.Contains("Total,0.00,0.00,0.00", csv);
    }

    [Fact]
    public async Task A_worker_gets_403_problem_details()
    {
        await app.ResetAsync();

        var get = await ReadProblem(
            await app.SendAsync(RemittanceApp.WorkerId, HttpMethod.Get, "/api/ops/payroll/remittance?month=2026-11"),
            StatusCodes.Status403Forbidden);
        Assert.Contains("not available for your role", get.GetProperty("detail").GetString());

        var put = await ReadProblem(
            await app.SendAsync(
                RemittanceApp.WorkerId, HttpMethod.Put, "/api/ops/payroll/remittance", new { enabled = true }),
            StatusCodes.Status403Forbidden);
        Assert.Equal(StatusCodes.Status403Forbidden, put.GetProperty("status").GetInt32());

        var export = await ReadProblem(
            await app.SendAsync(
                RemittanceApp.WorkerId, HttpMethod.Get, "/api/ops/payroll/remittance/export?month=2026-11"),
            StatusCodes.Status403Forbidden);
        Assert.Equal(StatusCodes.Status403Forbidden, export.GetProperty("status").GetInt32());

        await app.MutateAsync(async db =>
            Assert.Empty(await db.BranchRemittanceSettings().ToListAsync()));
    }

    [Fact]
    public async Task A_bad_month_is_400_and_a_missing_month_uses_Manila_today()
    {
        await app.ResetAsync();

        var bad = await ReadProblem(
            await app.SendAsync(RemittanceApp.OwnerId, HttpMethod.Get, "/api/ops/payroll/remittance?month=2026-1"),
            StatusCodes.Status400BadRequest);
        Assert.Contains("yyyy-MM", bad.GetProperty("errors").GetProperty("month")[0].GetString());

        var outOfRange = await ReadProblem(
            await app.SendAsync(RemittanceApp.OwnerId, HttpMethod.Get, "/api/ops/payroll/remittance?month=2026-13"),
            StatusCodes.Status400BadRequest);
        Assert.Equal(StatusCodes.Status400BadRequest, outOfRange.GetProperty("status").GetInt32());

        var missing = await ReadProblem(
            await app.SendAsync(
                RemittanceApp.OwnerId, HttpMethod.Put, "/api/ops/payroll/remittance", new { }),
            StatusCodes.Status400BadRequest);
        Assert.Contains("whether remittance is on", missing.GetProperty("errors").GetProperty("enabled")[0].GetString());

        var today = ManilaTime.Today();
        var open = await ReadOk(await app.SendAsync(
            RemittanceApp.OwnerId, HttpMethod.Get, "/api/ops/payroll/remittance"));
        Assert.Equal($"{today.Year:0000}-{today.Month:00}", open.GetProperty("month").GetString());
    }

    [Fact]
    public async Task Locked_lines_omit_employer_shares_when_they_were_not_stored_and_payslips_never_show_them()
    {
        await app.ResetAsync();
        var plain = await app.SeedRunAsync(
            PayrollRunStatus.Locked,
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 30),
            100m, 50m, 20m, null, null, null);
        var stamped = await app.SeedRunAsync(
            PayrollRunStatus.Locked,
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 31),
            100m, 50m, 20m, 203m, 50m, 40m);

        var without = await ReadOk(await app.SendAsync(
            RemittanceApp.OwnerId, HttpMethod.Get, $"/api/ops/payroll/runs/{plain}"));
        var plainLine = without.GetProperty("lines")[0];
        Assert.False(plainLine.TryGetProperty("employerSss", out _));
        Assert.False(plainLine.TryGetProperty("employerPhilHealth", out _));
        Assert.False(plainLine.TryGetProperty("employerPagIbig", out _));
        Assert.Equal(100m, plainLine.GetProperty("sss").GetDecimal());

        var withShares = await ReadOk(await app.SendAsync(
            RemittanceApp.OwnerId, HttpMethod.Get, $"/api/ops/payroll/runs/{stamped}"));
        var stampedLine = withShares.GetProperty("lines")[0];
        Assert.Equal(203m, stampedLine.GetProperty("employerSss").GetDecimal());
        Assert.Equal(50m, stampedLine.GetProperty("employerPhilHealth").GetDecimal());
        Assert.Equal(40m, stampedLine.GetProperty("employerPagIbig").GetDecimal());

        var payslip = await ReadOk(await app.SendAsync(
            RemittanceApp.OwnerId,
            HttpMethod.Post,
            "/api/ops/payroll/payslips/issue",
            new { staffId = RemittanceApp.WorkerStaffId, from = "2026-11-01", to = "2026-11-30" }));
        Assert.False(payslip.TryGetProperty("employerSss", out _));
        Assert.False(payslip.TryGetProperty("employerPhilHealth", out _));
        Assert.False(payslip.TryGetProperty("employerPagIbig", out _));
        Assert.True(payslip.TryGetProperty("sss", out _));
        Assert.True(payslip.TryGetProperty("philHealth", out _));
        Assert.True(payslip.TryGetProperty("pagIbig", out _));
    }

    private static void AssertAgency(
        JsonElement body,
        string key,
        decimal employee,
        decimal employer,
        decimal total)
    {
        var agency = body.GetProperty("agencies").EnumerateArray()
            .Single(row => row.GetProperty("key").GetString() == key);
        Assert.Equal(employee, agency.GetProperty("employeeShare").GetDecimal());
        Assert.Equal(employer, agency.GetProperty("employerShare").GetDecimal());
        Assert.Equal(total, agency.GetProperty("total").GetDecimal());
    }

    internal static async Task<JsonElement> ReadOk(HttpResponseMessage response)
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

/// <summary>Host capability that refuses every role. Neutral: it does not name a host role.</summary>
public sealed class OpenReviewRemittanceApp : RemittanceApp
{
    protected override void ConfigureReview(IServiceCollection services)
    {
        services.AddSingleton<IHrEmployerShareReview>(new AllowReview());
    }

    /// <summary>Neutral stub: every caller may review. It does not name a host role.</summary>
    private sealed class AllowReview : IHrEmployerShareReview
    {
        public bool CanReview(StaffRole role) => true;
    }
}

public sealed class EmployerShareReviewTests(OpenReviewRemittanceApp app) : IClassFixture<OpenReviewRemittanceApp>
{
    [Fact]
    public async Task The_default_capability_refuses_confirm()
    {
        await using var closed = new RemittanceApp();
        await closed.InitializeAsync();
        try
        {
            var response = await closed.SendAsync(
                RemittanceApp.OwnerId,
                HttpMethod.Put,
                "/api/ops/payroll/remittance/runs/" + Guid.NewGuid(),
                new { lines = Array.Empty<object>() });
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            response.Dispose();
        }
        finally
        {
            await closed.DisposeAsync();
        }
    }

    [Fact]
    public async Task An_allowed_caller_can_confirm_edited_shares()
    {
        await app.ResetAsync();
        await app.SendAsync(
            RemittanceApp.OwnerId, HttpMethod.Put, "/api/ops/payroll/remittance", new { enabled = true });
        var runId = await app.SeedRunAsync(
            PayrollRunStatus.Locked,
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 30),
            1_000m, 500m, 200m, null, null, null, reviewed: false);

        var review = await PayrollRemittanceEndpointTests.ReadOk(await app.SendAsync(
            RemittanceApp.OwnerId,
            HttpMethod.Get,
            "/api/ops/payroll/remittance/runs/" + runId));
        Assert.False(review.GetProperty("readOnly").GetBoolean());
        Assert.False(review.TryGetProperty("reviewedAtUtc", out _));

        var saved = await PayrollRemittanceEndpointTests.ReadOk(await app.SendAsync(
            RemittanceApp.OwnerId,
            HttpMethod.Put,
            "/api/ops/payroll/remittance/runs/" + runId,
            new
            {
                lines = new[]
                {
                    new
                    {
                        staffId = RemittanceApp.WorkerStaffId,
                        employerSss = 2_030m,
                        employerPhilHealth = 500m,
                        employerPagIbig = 200m
                    }
                }
            }));
        Assert.True(saved.TryGetProperty("reviewedAtUtc", out _));
        Assert.Equal(2_030m, saved.GetProperty("lines")[0].GetProperty("employerSss").GetDecimal());
    }
}

public sealed class DeniedRemittanceApp : RemittanceApp
{
    protected override void ConfigureAccess(IServiceCollection services)
    {
        services.AddSingleton<IHrRemittanceAccess>(new NoRemittanceAccess());
    }

    private sealed class NoRemittanceAccess : IHrRemittanceAccess
    {
        public bool CanView(StaffRole role) => false;
    }
}

public sealed class RemittanceAccessTests(DeniedRemittanceApp app) : IClassFixture<DeniedRemittanceApp>
{
    [Fact]
    public async Task A_caller_the_host_capability_refuses_gets_403_on_the_report_and_the_export()
    {
        var report = await app.SendAsync(
            RemittanceApp.OwnerId, HttpMethod.Get, "/api/ops/payroll/remittance?month=2026-11");
        Assert.Equal(HttpStatusCode.Forbidden, report.StatusCode);
        report.Dispose();

        var export = await app.SendAsync(
            RemittanceApp.OwnerId, HttpMethod.Get, "/api/ops/payroll/remittance/export?month=2026-11");
        Assert.Equal(HttpStatusCode.Forbidden, export.StatusCode);
        export.Dispose();
    }
}
