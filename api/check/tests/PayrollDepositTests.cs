using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HrCore.Data;
using HrCore.Domain;
using HrCore.Features.Payroll;
using HrCore.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HrCore.Tests;

/// <summary>
/// Deposited amount is recordkeeping on a locked line. Net 10050.00 minus a deposit of
/// 10000.00 is a discrepancy of 50.00. The locked amounts stay as they were.
/// </summary>
public sealed class PayrollDepositTests(DepositApp app) : IClassFixture<DepositApp>
{
    private const string OctoberFrom = "2026-10-01";
    private const string OctoberTo = "2026-10-31";

    [Fact]
    public async Task No_deposit_leaves_the_locked_line_and_the_payslip_unchanged()
    {
        await app.ResetAsync();
        var runId = await app.AddRunAsync(PayrollRunStatus.Locked);

        var run = await ReadOk(await app.SendAsync(
            DepositApp.OwnerId,
            HttpMethod.Get,
            "/api/ops/payroll/runs/" + runId));
        Assert.False(run.TryGetProperty("discrepancies", out _));
        foreach (var line in run.GetProperty("lines").EnumerateArray())
        {
            Assert.False(line.TryGetProperty("depositedAmount", out _));
            Assert.False(line.TryGetProperty("discrepancy", out _));
        }

        var casey = Line(run, "Casey Diaz");
        Assert.Equal(12000.00m, casey.GetProperty("amount").GetDecimal());
        Assert.Equal(1950.00m, casey.GetProperty("deductions").GetDecimal());
        Assert.Equal(10050.00m, casey.GetProperty("net").GetDecimal());

        var register = await ReadOk(await app.SendAsync(
            DepositApp.OwnerId,
            HttpMethod.Get,
            "/api/ops/payroll?from=" + OctoberFrom + "&to=" + OctoberTo));
        Assert.Equal("locked", register.GetProperty("source").GetString());
        foreach (var row in register.GetProperty("rows").EnumerateArray())
        {
            Assert.False(row.TryGetProperty("depositedAmount", out _));
            Assert.False(row.TryGetProperty("discrepancy", out _));
        }

        var payslip = await ReadOk(await app.SendAsync(
            DepositApp.OwnerId,
            HttpMethod.Post,
            "/api/ops/payroll/payslips/issue",
            new { staffId = DepositApp.CaseyId, from = OctoberFrom, to = OctoberTo }));
        Assert.Equal(10050.00m, payslip.GetProperty("net").GetDecimal());
        Assert.Equal(12000.00m, payslip.GetProperty("gross").GetDecimal());
        Assert.False(payslip.TryGetProperty("depositedAmount", out _));
        Assert.False(payslip.TryGetProperty("discrepancy", out _));
    }

    [Fact]
    public async Task Locked_line_deposit_of_10000_against_net_10050_shows_a_50_discrepancy()
    {
        await app.ResetAsync();
        var runId = await app.AddRunAsync(PayrollRunStatus.Locked);

        var saved = await ReadOk(await app.SendAsync(
            DepositApp.OwnerId,
            HttpMethod.Put,
            DepositUrl(runId),
            new { staffId = DepositApp.CaseyId, depositedAmount = 10000.00m }));
        Assert.Equal(10050.00m, saved.GetProperty("net").GetDecimal());
        Assert.Equal(10000.00m, saved.GetProperty("depositedAmount").GetDecimal());
        Assert.Equal(50.00m, saved.GetProperty("discrepancy").GetDecimal());

        var run = await ReadOk(await app.SendAsync(
            DepositApp.OwnerId,
            HttpMethod.Get,
            "/api/ops/payroll/runs/" + runId));
        var casey = Line(run, "Casey Diaz");
        var dana = Line(run, "Dana Ruiz");
        Assert.Equal(12000.00m, casey.GetProperty("amount").GetDecimal());
        Assert.Equal(1950.00m, casey.GetProperty("deductions").GetDecimal());
        Assert.Equal(10050.00m, casey.GetProperty("net").GetDecimal());
        Assert.Equal(10000.00m, casey.GetProperty("depositedAmount").GetDecimal());
        Assert.Equal(50.00m, casey.GetProperty("discrepancy").GetDecimal());
        Assert.False(dana.TryGetProperty("depositedAmount", out _));
        Assert.False(dana.TryGetProperty("discrepancy", out _));
        Assert.Equal(8000.00m, dana.GetProperty("net").GetDecimal());

        var listed = run.GetProperty("discrepancies");
        Assert.Equal(1, listed.GetArrayLength());
        Assert.Equal(DepositApp.CaseyId, listed[0].GetProperty("staffId").GetGuid());
        Assert.Equal("Casey Diaz", listed[0].GetProperty("staffName").GetString());
        Assert.Equal(10050.00m, listed[0].GetProperty("net").GetDecimal());
        Assert.Equal(10000.00m, listed[0].GetProperty("depositedAmount").GetDecimal());
        Assert.Equal(50.00m, listed[0].GetProperty("discrepancy").GetDecimal());

        var register = await ReadOk(await app.SendAsync(
            DepositApp.OwnerId,
            HttpMethod.Get,
            "/api/ops/payroll?from=" + OctoberFrom + "&to=" + OctoberTo));
        var caseyRow = register.GetProperty("rows").EnumerateArray()
            .Single(row => row.GetProperty("staffName").GetString() == "Casey Diaz");
        var danaRow = register.GetProperty("rows").EnumerateArray()
            .Single(row => row.GetProperty("staffName").GetString() == "Dana Ruiz");
        Assert.Equal(50.00m, caseyRow.GetProperty("discrepancy").GetDecimal());
        Assert.Equal(10000.00m, caseyRow.GetProperty("depositedAmount").GetDecimal());
        Assert.Equal(10050.00m, caseyRow.GetProperty("net").GetDecimal());
        Assert.False(danaRow.TryGetProperty("discrepancy", out _));

        var payslip = await ReadOk(await app.SendAsync(
            DepositApp.OwnerId,
            HttpMethod.Post,
            "/api/ops/payroll/payslips/issue",
            new { staffId = DepositApp.CaseyId, from = OctoberFrom, to = OctoberTo }));
        Assert.Equal(10050.00m, payslip.GetProperty("net").GetDecimal());
        Assert.Equal(12000.00m, payslip.GetProperty("gross").GetDecimal());
        Assert.Equal(1950.00m, payslip.GetProperty("deductions").GetDecimal());
        Assert.False(payslip.TryGetProperty("depositedAmount", out _));
        Assert.False(payslip.TryGetProperty("discrepancy", out _));

        var mine = await ReadOk(await app.SendAsync(
            DepositApp.WorkerId,
            HttpMethod.Get,
            "/api/ops/payroll/me/payslips"));
        var detail = await ReadOk(await app.SendAsync(
            DepositApp.WorkerId,
            HttpMethod.Get,
            "/api/ops/payroll/me/payslips/" + mine[0].GetProperty("lineId").GetGuid()));
        Assert.Equal(10050.00m, detail.GetProperty("net").GetDecimal());
        Assert.False(detail.TryGetProperty("depositedAmount", out _));
        Assert.False(detail.TryGetProperty("discrepancy", out _));

        var log = await ReadOk(await app.SendAsync(
            DepositApp.OwnerId,
            HttpMethod.Get,
            "/api/hr/change-log?staffId=" + DepositApp.CaseyId + "&field=payroll.deposit"));
        var row = log.GetProperty("rows").EnumerateArray().Single();
        Assert.Equal("payroll.deposit", row.GetProperty("field").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("oldValue").ValueKind);
        Assert.Equal("10000.00", row.GetProperty("newValue").GetString());
        Assert.Equal(OctoberFrom, row.GetProperty("periodFrom").GetString());
        Assert.Equal(OctoberTo, row.GetProperty("periodTo").GetString());

        var matched = await ReadOk(await app.SendAsync(
            DepositApp.OwnerId,
            HttpMethod.Put,
            DepositUrl(runId),
            new { staffId = DepositApp.CaseyId, depositedAmount = 10050.00m }));
        Assert.Equal(0.00m, matched.GetProperty("discrepancy").GetDecimal());
        Assert.Equal(10050.00m, matched.GetProperty("net").GetDecimal());
        var exact = await ReadOk(await app.SendAsync(
            DepositApp.OwnerId,
            HttpMethod.Get,
            "/api/ops/payroll/runs/" + runId));
        Assert.False(exact.TryGetProperty("discrepancies", out _));
        Assert.Equal(0.00m, Line(exact, "Casey Diaz").GetProperty("discrepancy").GetDecimal());
        Assert.Equal(10050.00m, Line(exact, "Casey Diaz").GetProperty("net").GetDecimal());

        var over = await ReadOk(await app.SendAsync(
            DepositApp.OwnerId,
            HttpMethod.Put,
            DepositUrl(runId),
            new { staffId = DepositApp.CaseyId, depositedAmount = 10100.00m }));
        Assert.Equal(-50.00m, over.GetProperty("discrepancy").GetDecimal());
        var overRun = await ReadOk(await app.SendAsync(
            DepositApp.OwnerId,
            HttpMethod.Get,
            "/api/ops/payroll/runs/" + runId));
        Assert.Equal(-50.00m, overRun.GetProperty("discrepancies")[0].GetProperty("discrepancy").GetDecimal());
        Assert.Equal(10050.00m, Line(overRun, "Casey Diaz").GetProperty("net").GetDecimal());
        Assert.Equal(12000.00m, Line(overRun, "Casey Diaz").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task A_negative_or_finer_than_two_decimals_is_400_a_worker_is_403_and_draft_or_void_is_409()
    {
        await app.ResetAsync();
        var runId = await app.AddRunAsync(PayrollRunStatus.Locked);

        foreach (var amount in new decimal[] { -1m, -0.01m, 10.001m, 10000.001m })
        {
            var problem = await ReadProblem(
                await app.SendAsync(
                    DepositApp.OwnerId,
                    HttpMethod.Put,
                    DepositUrl(runId),
                    new { staffId = DepositApp.CaseyId, depositedAmount = amount }),
                StatusCodes.Status400BadRequest);
            Assert.Equal("application/problem+json", problem.Item2);
            Assert.Equal(
                "Amount must be zero or more, with at most 2 decimal places.",
                problem.Item1.GetProperty("errors").GetProperty("depositedAmount")[0].GetString());
        }

        var untouched = await ReadOk(await app.SendAsync(
            DepositApp.OwnerId,
            HttpMethod.Get,
            "/api/ops/payroll/runs/" + runId));
        Assert.False(Line(untouched, "Casey Diaz").TryGetProperty("depositedAmount", out _));

        var worker = await ReadProblem(
            await app.SendAsync(
                DepositApp.WorkerId,
                HttpMethod.Put,
                DepositUrl(runId),
                new { staffId = DepositApp.CaseyId, depositedAmount = 10000.00m }),
            StatusCodes.Status403Forbidden);
        Assert.Equal("application/problem+json", worker.Item2);
        Assert.Contains("not available", worker.Item1.GetProperty("detail").GetString());

        var admin = await ReadOk(await app.SendAsync(
            DepositApp.AdminId,
            HttpMethod.Put,
            DepositUrl(runId),
            new { staffId = DepositApp.DanaId, depositedAmount = 8000.00m }));
        Assert.Equal(0.00m, admin.GetProperty("discrepancy").GetDecimal());
        Assert.Equal(8000.00m, admin.GetProperty("net").GetDecimal());

        await app.ResetAsync();
        var draftId = await app.AddRunAsync(PayrollRunStatus.Draft);
        var draft = await ReadProblem(
            await app.SendAsync(
                DepositApp.OwnerId,
                HttpMethod.Put,
                DepositUrl(draftId),
                new { staffId = DepositApp.CaseyId, depositedAmount = 10000.00m }),
            StatusCodes.Status409Conflict);
        Assert.Equal("application/problem+json", draft.Item2);
        Assert.Contains("locked", draft.Item1.GetProperty("detail").GetString());

        await app.ResetAsync();
        var voidId = await app.AddRunAsync(PayrollRunStatus.Locked);
        var voided = await app.SendAsync(DepositApp.OwnerId, HttpMethod.Post, "/api/ops/payroll/runs/" + voidId + "/void");
        Assert.Equal(HttpStatusCode.OK, voided.StatusCode);
        voided.Dispose();
        var conflict = await ReadProblem(
            await app.SendAsync(
                DepositApp.OwnerId,
                HttpMethod.Put,
                DepositUrl(voidId),
                new { staffId = DepositApp.CaseyId, depositedAmount = 10000.00m }),
            StatusCodes.Status409Conflict);
        Assert.Equal("application/problem+json", conflict.Item2);
        Assert.Contains("locked", conflict.Item1.GetProperty("detail").GetString());
        var still = await ReadOk(await app.SendAsync(
            DepositApp.OwnerId,
            HttpMethod.Get,
            "/api/ops/payroll/runs/" + voidId));
        Assert.False(Line(still, "Casey Diaz").TryGetProperty("depositedAmount", out _));
        Assert.Equal(10050.00m, Line(still, "Casey Diaz").GetProperty("net").GetDecimal());
    }

    [Fact]
    public async Task Flag_off_lock_matches_flag_on_and_the_deposit_route_is_absent()
    {
        await app.ResetAsync();
        await using var off = await DepositApp.StartFlagOffAsync();
        var on = await LockNovember(app);
        var offRun = await LockNovember(off);
        Assert.True(on.GetProperty("run").GetProperty("totalNet").GetDecimal() > 0);
        Assert.Equal(
            offRun.GetProperty("run").GetProperty("totalNet").GetDecimal(),
            on.GetProperty("run").GetProperty("totalNet").GetDecimal());
        Assert.False(on.TryGetProperty("discrepancies", out _));
        Assert.False(offRun.TryGetProperty("discrepancies", out _));
        foreach (var line in on.GetProperty("lines").EnumerateArray())
        {
            Assert.False(line.TryGetProperty("depositedAmount", out _));
            Assert.False(line.TryGetProperty("discrepancy", out _));
        }

        var missing = await off.SendAsync(
            DepositApp.OwnerId,
            HttpMethod.Put,
            DepositUrl(offRun.GetProperty("run").GetProperty("id").GetGuid()),
            new { staffId = DepositApp.CaseyId, depositedAmount = 1.00m });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        missing.Dispose();
    }

    private static string DepositUrl(Guid runId) => "/api/ops/payroll/runs/" + runId + "/deposit";

    private static async Task<JsonElement> LockNovember(DepositApp host) =>
        await ReadOk(await host.SendAsync(
            DepositApp.OwnerId,
            HttpMethod.Post,
            "/api/ops/payroll/runs",
            new { branchId = DepositApp.BranchId, from = "2026-11-01", to = "2026-11-30" }));

    private static JsonElement Line(JsonElement run, string name) =>
        run.GetProperty("lines").EnumerateArray().Single(line => line.GetProperty("staffName").GetString() == name);

    private static async Task<JsonElement> ReadOk(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.IsSuccessStatusCode,
            (int)response.StatusCode + " " + (text.Length == 0 ? "(empty)" : text[..Math.Min(text.Length, 1500)]));
        using var _ = response;
        return JsonSerializer.Deserialize<JsonElement>(text);
    }

    private static async Task<(JsonElement Body, string? MediaType)> ReadProblem(HttpResponseMessage response, int status)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == status, text);
        var media = response.Content.Headers.ContentType?.MediaType;
        using var _ = response;
        return (JsonSerializer.Deserialize<JsonElement>(text), media);
    }
}

public sealed class PayrollDepositMathTests
{
    [Fact]
    public void Discrepancy_is_net_minus_the_deposited_amount()
    {
        Assert.Null(PayrollDepositMath.Discrepancy(10050.00m, null));
        Assert.Equal(50.00m, PayrollDepositMath.Discrepancy(10050.00m, 10000.00m));
        Assert.Equal(0m, PayrollDepositMath.Discrepancy(10050.00m, 10050.00m));
        Assert.Equal(-50.00m, PayrollDepositMath.Discrepancy(10050.00m, 10100.00m));
        Assert.Null(PayrollDepositMath.NonZero([]));
    }
}

public sealed class DepositApp : IAsyncLifetime
{
    public const string OwnerId = "alex-rivera";
    public const string AdminId = "sam-lee";
    public const string WorkerId = "casey-diaz";
    public static readonly Guid BranchId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid CaseyId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public static readonly Guid DanaId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    private readonly bool deposits;
    private WebApplication app = null!;

    public DepositApp()
        : this(true)
    {
    }

    private DepositApp(bool deposits) => this.deposits = deposits;

    public static async Task<DepositApp> StartFlagOffAsync()
    {
        var app = new DepositApp(false);
        await app.InitializeAsync();
        return app;
    }

    private HttpClient Client { get; set; } = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = "HrCore.Tests"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        var features = new HrCoreFeatures { Deposits = deposits };
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot();
        builder.Services.AddDbContext<TestHrDb>(options =>
            options.UseInMemoryDatabase(databaseName, databaseRoot)
                .UseHrCoreFeatures(features)
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
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
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestHrDb>();
        db.PayrollRunLineItems.RemoveRange(db.PayrollRunLineItems);
        db.PayrollRunLines.RemoveRange(db.PayrollRunLines);
        db.PayrollRuns.RemoveRange(db.PayrollRuns);
        db.HrChangeLogs.RemoveRange(db.HrChangeLogs);
        await db.SaveChangesAsync();
    }

    public async Task<Guid> AddRunAsync(PayrollRunStatus status)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestHrDb>();
        var runId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId,
            BranchId = BranchId,
            PeriodFrom = new DateOnly(2026, 10, 1),
            PeriodTo = new DateOnly(2026, 10, 31),
            Status = status,
            TotalGross = 20000.00m,
            TotalDeductions = 1950.00m,
            TotalNet = 18050.00m,
            CreatedAtUtc = now,
            LockedAtUtc = status == PayrollRunStatus.Locked ? now : null
        });
        db.PayrollRunLines.Add(Line(runId, CaseyId, "Casey Diaz", 12000.00m, 1950.00m, 10050.00m));
        db.PayrollRunLines.Add(Line(runId, DanaId, "Dana Ruiz", 8000.00m, 0m, 8000.00m));
        await db.SaveChangesAsync();
        return runId;
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

    private static PayrollRunLine Line(Guid runId, Guid staffId, string name, decimal gross, decimal deductions, decimal net) =>
        new()
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            StaffId = staffId,
            StaffName = name,
            BranchId = BranchId,
            Gross = gross,
            Deductions = deductions,
            Net = net,
            Sss = 0m,
            PhilHealth = 0m,
            PagIbig = 0m,
            Wht = 0m,
            Other = 0m
        };

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
        AddPerson(db, OwnerId, "Alex Rivera", StaffRole.Owner, null, now, pay: false);
        AddPerson(db, AdminId, "Sam Lee", StaffRole.Admin, BranchId, now, pay: false);
        AddPerson(db, WorkerId, "Casey Diaz", StaffRole.Worker, BranchId, now, pay: true, CaseyId, 30000m);
        AddPerson(db, "dana-ruiz", "Dana Ruiz", StaffRole.Worker, BranchId, now, pay: true, DanaId, 15000m);
        await db.SaveChangesAsync();
    }

    private static void AddPerson(
        TestHrDb db,
        string authUserId,
        string name,
        StaffRole role,
        Guid? branchId,
        DateTime now,
        bool pay,
        Guid? staffId = null,
        decimal rate = 30000m)
    {
        var profile = new Profile
        {
            Id = Guid.NewGuid(),
            AuthUserId = authUserId,
            Kind = ProfileKind.Staff,
            DisplayName = name,
            Email = authUserId + "@example.com",
            CreatedAtUtc = now
        };
        db.Profiles.Add(profile);
        db.Staff.Add(new Staff
        {
            Id = staffId ?? Guid.NewGuid(),
            ProfileId = profile.Id,
            BranchId = branchId,
            Role = role,
            Active = true,
            PayBasis = pay ? PayBasis.Monthly : null,
            RatePhp = pay ? rate : null,
            MustClock = false,
            CreatedAtUtc = now
        });
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
}
