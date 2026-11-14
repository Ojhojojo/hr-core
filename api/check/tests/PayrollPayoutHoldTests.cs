using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HrCore.Data;
using HrCore.Domain;
using HrCore.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HrCore.Tests;

public sealed class PayrollPayoutHoldTests(PayoutHoldApp app) : IClassFixture<PayoutHoldApp>
{
    private const string PeriodFrom = "2026-11-01";
    private const string PeriodTo = "2026-11-30";

    [Fact]
    public async Task No_hold_matches_a_flag_off_register_and_the_locked_run_omits_hold_fields()
    {
        await app.ResetAsync();
        await using var off = await PayoutHoldApp.StartFlagOffAsync();

        var live = await Register(app);
        var liveOff = await Register(off);
        Assert.True(live.TotalNet > 0);
        Assert.Equal(liveOff.TotalAmount, live.TotalAmount);
        Assert.Equal(liveOff.TotalNet, live.TotalNet);
        Assert.False(live.Body.TryGetProperty("totalHeld", out _));
        Assert.False(liveOff.Body.TryGetProperty("totalHeld", out _));
        AssertNoHold(live.Body);
        AssertNoHold(liveOff.Body);

        var locked = await Lock(app);
        var lockedOff = await Lock(off);
        Assert.Equal(
            lockedOff.GetProperty("run").GetProperty("totalNet").GetDecimal(),
            locked.GetProperty("run").GetProperty("totalNet").GetDecimal());
        Assert.False(locked.GetProperty("run").TryGetProperty("totalHeld", out _));
        Assert.False(lockedOff.GetProperty("run").TryGetProperty("totalHeld", out _));
        foreach (var line in locked.GetProperty("lines").EnumerateArray())
        {
            Assert.False(line.TryGetProperty("payoutStatus", out _));
            Assert.False(line.TryGetProperty("releasedOn", out _));
        }

        var payslip = await ReadOk(await app.SendAsync(
            PayoutHoldApp.OwnerId,
            HttpMethod.Post,
            "/api/ops/payroll/payslips/issue",
            new { staffId = PayoutHoldApp.CaseyId, from = PeriodFrom, to = PeriodTo }));
        Assert.False(payslip.TryGetProperty("payoutStatus", out _));
        Assert.Equal(Net(live.Body, "Casey Diaz"), payslip.GetProperty("net").GetDecimal());
    }

    [Fact]
    public async Task Lock_marks_the_line_held_and_release_records_the_date_and_who()
    {
        await app.ResetAsync();
        var before = await Register(app);
        var caseyNet = Net(before.Body, "Casey Diaz");
        var danaNet = Net(before.Body, "Dana Ruiz");
        Assert.True(caseyNet > 0);
        Assert.True(danaNet > 0);
        Assert.NotEqual(caseyNet, danaNet);

        var saved = await ReadOk(await app.SendAsync(
            PayoutHoldApp.OwnerId,
            HttpMethod.Post,
            "/api/ops/payroll/payout-holds",
            new { staffId = PayoutHoldApp.CaseyId, reason = "Clearance pending" }));
        Assert.Equal("Casey Diaz", saved.GetProperty("staffName").GetString());
        Assert.Equal("Clearance pending", saved.GetProperty("reason").GetString());
        Assert.Equal("Alex Rivera", saved.GetProperty("heldByName").GetString());
        Assert.True(saved.GetProperty("active").GetBoolean());
        Assert.False(saved.TryGetProperty("releasedOn", out _));

        var held = await Register(app);
        Assert.Equal(before.TotalAmount, held.TotalAmount);
        Assert.Equal(before.TotalNet, held.TotalNet);
        Assert.Equal(caseyNet, Net(held.Body, "Casey Diaz"));
        Assert.Equal(danaNet, Net(held.Body, "Dana Ruiz"));
        Assert.Equal("held", Row(held.Body, "Casey Diaz").GetProperty("payoutStatus").GetString());
        Assert.Equal("Clearance pending", Row(held.Body, "Casey Diaz").GetProperty("holdReason").GetString());
        Assert.False(Row(held.Body, "Dana Ruiz").TryGetProperty("payoutStatus", out _));
        Assert.Equal(caseyNet, held.Body.GetProperty("totalHeld").GetDecimal());

        var locked = await Lock(app);
        var run = locked.GetProperty("run");
        Assert.Equal(before.TotalNet, run.GetProperty("totalNet").GetDecimal());
        Assert.Equal(caseyNet, run.GetProperty("totalHeld").GetDecimal());
        var caseyLine = Line(locked, "Casey Diaz");
        var danaLine = Line(locked, "Dana Ruiz");
        Assert.Equal("held", caseyLine.GetProperty("payoutStatus").GetString());
        Assert.Equal("Clearance pending", caseyLine.GetProperty("holdReason").GetString());
        Assert.Equal(Amount(before.Body, "Casey Diaz"), caseyLine.GetProperty("amount").GetDecimal());
        Assert.Equal(caseyNet, caseyLine.GetProperty("net").GetDecimal());
        Assert.False(caseyLine.TryGetProperty("releasedOn", out _));
        Assert.False(danaLine.TryGetProperty("payoutStatus", out _));
        Assert.Equal(danaNet, danaLine.GetProperty("net").GetDecimal());

        var payslip = await ReadOk(await app.SendAsync(
            PayoutHoldApp.OwnerId,
            HttpMethod.Post,
            "/api/ops/payroll/payslips/issue",
            new { staffId = PayoutHoldApp.CaseyId, from = PeriodFrom, to = PeriodTo }));
        Assert.Equal("held", payslip.GetProperty("payoutStatus").GetString());
        Assert.Equal(caseyNet, payslip.GetProperty("net").GetDecimal());

        var worker = await ReadProblem(
            await app.SendAsync(PayoutHoldApp.WorkerId, HttpMethod.Post, ReleaseUrl(PayoutHoldApp.CaseyId)),
            StatusCodes.Status403Forbidden);
        Assert.Equal("application/problem+json", worker.Item2);

        var released = await ReadOk(await app.SendAsync(
            PayoutHoldApp.OwnerId,
            HttpMethod.Post,
            ReleaseUrl(PayoutHoldApp.CaseyId)));
        var today = ManilaTime.Today().ToString("yyyy-MM-dd");
        Assert.Equal(today, released.GetProperty("releasedOn").GetString());
        Assert.Equal("Alex Rivera", released.GetProperty("releasedByName").GetString());
        Assert.False(released.GetProperty("active").GetBoolean());

        var again = await ReadOk(await app.SendAsync(
            PayoutHoldApp.OwnerId,
            HttpMethod.Get,
            "/api/ops/payroll/runs/" + run.GetProperty("id").GetGuid()));
        Assert.False(again.GetProperty("run").TryGetProperty("totalHeld", out _));
        Assert.Equal(before.TotalNet, again.GetProperty("run").GetProperty("totalNet").GetDecimal());
        var releasedLine = Line(again, "Casey Diaz");
        Assert.Equal("released", releasedLine.GetProperty("payoutStatus").GetString());
        Assert.Equal(today, releasedLine.GetProperty("releasedOn").GetString());
        Assert.Equal("Alex Rivera", releasedLine.GetProperty("releasedByName").GetString());
        Assert.Equal(caseyNet, releasedLine.GetProperty("net").GetDecimal());
        Assert.Equal(danaNet, Line(again, "Dana Ruiz").GetProperty("net").GetDecimal());

        var after = await ReadOk(await app.SendAsync(
            PayoutHoldApp.OwnerId,
            HttpMethod.Post,
            "/api/ops/payroll/payslips/issue",
            new { staffId = PayoutHoldApp.CaseyId, from = PeriodFrom, to = PeriodTo }));
        Assert.Equal("released", after.GetProperty("payoutStatus").GetString());
        Assert.Equal(today, after.GetProperty("releasedOn").GetString());
        Assert.Equal(caseyNet, after.GetProperty("net").GetDecimal());

        var mine = await ReadOk(await app.SendAsync(
            PayoutHoldApp.WorkerId,
            HttpMethod.Get,
            "/api/ops/payroll/me/payslips"));
        var detail = await ReadOk(await app.SendAsync(
            PayoutHoldApp.WorkerId,
            HttpMethod.Get,
            "/api/ops/payroll/me/payslips/" + mine[0].GetProperty("lineId").GetGuid()));
        Assert.Equal("released", detail.GetProperty("payoutStatus").GetString());
        Assert.Equal(today, detail.GetProperty("releasedOn").GetString());
        Assert.Equal("Alex Rivera", detail.GetProperty("releasedByName").GetString());
        Assert.Equal(caseyNet, detail.GetProperty("net").GetDecimal());

        var log = await ReadOk(await app.SendAsync(
            PayoutHoldApp.OwnerId,
            HttpMethod.Get,
            "/api/hr/change-log?staffId=" + PayoutHoldApp.CaseyId + "&field=payroll.payout_hold"));
        Assert.Contains(
            log.GetProperty("rows").EnumerateArray(),
            row => row.GetProperty("newValue").GetString() == "released " + today
                && row.GetProperty("field").GetString() == "payroll.payout_hold");
    }

    [Fact]
    public async Task A_blank_reason_is_400_and_a_worker_is_403()
    {
        await app.ResetAsync();
        foreach (var reason in new string?[] { null, "", "   " })
        {
            var problem = await ReadProblem(
                await app.SendAsync(
                    PayoutHoldApp.OwnerId,
                    HttpMethod.Post,
                    "/api/ops/payroll/payout-holds",
                    new { staffId = PayoutHoldApp.CaseyId, reason }),
                StatusCodes.Status400BadRequest);
            Assert.Equal("application/problem+json", problem.Item2);
            Assert.Contains("required", problem.Item1.GetProperty("errors").GetProperty("reason")[0].GetString());
        }

        var missing = await app.SendAsync(
            PayoutHoldApp.OwnerId,
            HttpMethod.Post,
            "/api/ops/payroll/payout-holds",
            new { staffId = Guid.NewGuid(), reason = "Clearance pending" });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        missing.Dispose();

        foreach (var (method, url, body) in new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Get, "/api/ops/payroll/payout-holds", null),
            (HttpMethod.Post, "/api/ops/payroll/payout-holds", new { staffId = PayoutHoldApp.CaseyId, reason = "Clearance pending" }),
            (HttpMethod.Post, ReleaseUrl(PayoutHoldApp.CaseyId), null)
        })
        {
            var problem = await ReadProblem(
                await app.SendAsync(PayoutHoldApp.WorkerId, method, url, body),
                StatusCodes.Status403Forbidden);
            Assert.Equal("application/problem+json", problem.Item2);
            Assert.Contains("not available", problem.Item1.GetProperty("detail").GetString());
        }

        var admin = await ReadOk(await app.SendAsync(
            PayoutHoldApp.AdminId,
            HttpMethod.Post,
            "/api/ops/payroll/payout-holds",
            new { staffId = PayoutHoldApp.DanaId, reason = "Clearance pending" }));
        Assert.Equal("Dana Ruiz", admin.GetProperty("staffName").GetString());
        Assert.True(admin.GetProperty("active").GetBoolean());

        var notHeld = await app.SendAsync(
            PayoutHoldApp.OwnerId,
            HttpMethod.Post,
            ReleaseUrl(PayoutHoldApp.CaseyId));
        Assert.Equal(HttpStatusCode.NotFound, notHeld.StatusCode);
        notHeld.Dispose();
    }

    private static string ReleaseUrl(Guid staffId) =>
        "/api/ops/payroll/payout-holds/" + staffId + "/release";

    private static async Task<(JsonElement Body, decimal TotalAmount, decimal TotalNet)> Register(PayoutHoldApp host)
    {
        var body = await ReadOk(await host.SendAsync(
            PayoutHoldApp.OwnerId,
            HttpMethod.Get,
            "/api/ops/payroll?from=" + PeriodFrom + "&to=" + PeriodTo));
        return (
            body,
            body.GetProperty("totalAmount").GetDecimal(),
            body.GetProperty("totalNet").GetDecimal());
    }

    private static async Task<JsonElement> Lock(PayoutHoldApp host) =>
        await ReadOk(await host.SendAsync(
            PayoutHoldApp.OwnerId,
            HttpMethod.Post,
            "/api/ops/payroll/runs",
            new { branchId = PayoutHoldApp.BranchId, from = PeriodFrom, to = PeriodTo }));

    private static void AssertNoHold(JsonElement register)
    {
        foreach (var row in register.GetProperty("rows").EnumerateArray())
        {
            Assert.False(row.TryGetProperty("payoutStatus", out _));
            Assert.False(row.TryGetProperty("holdReason", out _));
            Assert.False(row.TryGetProperty("releasedOn", out _));
        }
    }

    private static JsonElement Row(JsonElement register, string name) =>
        register.GetProperty("rows").EnumerateArray().Single(row => row.GetProperty("staffName").GetString() == name);

    private static JsonElement Line(JsonElement run, string name) =>
        run.GetProperty("lines").EnumerateArray().Single(line => line.GetProperty("staffName").GetString() == name);

    private static decimal Net(JsonElement register, string name) =>
        Row(register, name).GetProperty("net").GetDecimal();

    private static decimal Amount(JsonElement register, string name) =>
        Row(register, name).GetProperty("amount").GetDecimal();

    private static async Task<JsonElement> ReadOk(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, text.Length == 0 ? "(empty)" : text[..Math.Min(text.Length, 1500)]);
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

public sealed class PayoutHoldApp : IAsyncLifetime
{
    public const string OwnerId = "alex-rivera";
    public const string AdminId = "sam-lee";
    public const string WorkerId = "casey-diaz";
    public static readonly Guid BranchId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid CaseyId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public static readonly Guid DanaId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    private readonly bool payoutHold;
    private WebApplication app = null!;

    public PayoutHoldApp()
        : this(true)
    {
    }

    private PayoutHoldApp(bool payoutHold) => this.payoutHold = payoutHold;

    public static async Task<PayoutHoldApp> StartFlagOffAsync()
    {
        var app = new PayoutHoldApp(false);
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
        var features = new HrCoreFeatures { PayoutHold = payoutHold };
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
        if (payoutHold)
        {
            db.StaffPayoutHolds().RemoveRange(db.StaffPayoutHolds());
        }

        db.HrChangeLogs.RemoveRange(db.HrChangeLogs);
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
