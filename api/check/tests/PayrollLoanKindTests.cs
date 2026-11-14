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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HrCore.Tests;

public sealed class PayrollLoanKindTests(LoanKindApp app) : IClassFixture<LoanKindApp>
{
    [Fact]
    public async Task Cut_off_1_only_skips_cut_off_2_and_a_free_text_loan_still_deducts()
    {
        await app.ResetAsync();
        await SaveCutoffs();
        var pagibig = await CreateKind("Pag-IBIG loan", 1);
        var sss = await CreateKind("SSS loan", null);

        await CreateLoan(pagibig, "ignored", 200m);
        await CreateLoan(sss, "ignored", 50m);
        await CreateLoan(null, "Cash advance", 100m);

        var cutoff2 = await Register("2026-11-11", "2026-11-25");
        var second = Amounts(cutoff2);
        Assert.False(second.ContainsKey("Pag-IBIG loan"), "Cut-off 1 only takes nothing on cut-off 2.");
        Assert.Equal(50m, second["SSS loan"]);
        Assert.Equal(100m, second["Cash advance"]);
        Assert.Equal(0m, cutoff2.GetProperty("pagIbig").GetDecimal());
        Assert.Equal(0m, cutoff2.GetProperty("sss").GetDecimal());
        Assert.Equal(0m, cutoff2.GetProperty("philHealth").GetDecimal());
    }

    [Fact]
    public async Task Cut_off_1_payslip_lists_each_kind_apart_from_contributions()
    {
        await app.ResetAsync();
        await SaveCutoffs();
        var pagibig = await CreateKind("Pag-IBIG loan", 1);
        var sss = await CreateKind("SSS loan", null);
        await CreateLoan(pagibig, "ignored", 200m);
        await CreateLoan(sss, "ignored", 50m);
        await CreateLoan(null, "Cash advance", 100m);

        var issued = await ReadOk(await app.SendAsync(
            LoanKindApp.OwnerId,
            HttpMethod.Post,
            "/api/ops/payroll/payslips/issue",
            new
            {
                staffId = LoanKindApp.WorkerStaffId,
                from = "2026-10-26",
                to = "2026-11-10"
            }));
        Assert.Equal("2026-10-26", issued.GetProperty("from").GetString());
        Assert.Equal("2026-11-10", issued.GetProperty("to").GetString());
        Assert.Equal(0m, issued.GetProperty("pagIbig").GetDecimal());
        Assert.Equal(0m, issued.GetProperty("sss").GetDecimal());
        Assert.Equal(0m, issued.GetProperty("philHealth").GetDecimal());

        var lines = issued.GetProperty("loanItems").EnumerateArray()
            .Select(item => (Label: item.GetProperty("label").GetString(), Amount: item.GetProperty("instalment").GetDecimal()))
            .ToList();
        Assert.Contains(lines, line => line.Label == "Pag-IBIG loan" && line.Amount == 200m);
        Assert.Contains(lines, line => line.Label == "SSS loan" && line.Amount == 50m);
        Assert.Contains(lines, line => line.Label == "Cash advance" && line.Amount == 100m);
        Assert.DoesNotContain(lines, line => line.Label is "SSS" or "PhilHealth" or "Pag-IBIG");
    }

    [Fact]
    public async Task Duplicate_name_is_409_problem_details_and_a_blank_name_is_400()
    {
        await app.ResetAsync();
        await CreateKind("Pag-IBIG loan", 1);

        var duplicate = await ReadProblem(
            await app.SendAsync(LoanKindApp.OwnerId, HttpMethod.Post, "/api/ops/payroll/loan-kinds", KindBody("pag-ibig loan", 2)),
            StatusCodes.Status409Conflict);
        Assert.Equal("application/problem+json", duplicate.Item2);
        Assert.Contains("already exists", duplicate.Item1.GetProperty("detail").GetString());

        var spaced = await ReadProblem(
            await app.SendAsync(LoanKindApp.OwnerId, HttpMethod.Post, "/api/ops/payroll/loan-kinds", KindBody("  Pag-IBIG loan  ", null)),
            StatusCodes.Status409Conflict);
        Assert.Contains("already exists", spaced.Item1.GetProperty("detail").GetString());

        var blank = await ReadProblem(
            await app.SendAsync(LoanKindApp.OwnerId, HttpMethod.Post, "/api/ops/payroll/loan-kinds", KindBody("  ", null)),
            StatusCodes.Status400BadRequest);
        Assert.Contains("required", blank.Item1.GetProperty("errors").GetProperty("name")[0].GetString());
    }

    [Fact]
    public async Task Kind_in_use_cannot_be_deleted_and_can_be_deactivated()
    {
        await app.ResetAsync();
        var id = await CreateKind("Company cash advance", 2);
        await CreateLoan(id, "ignored", 150m);

        var blocked = await ReadProblem(
            await app.SendAsync(LoanKindApp.OwnerId, HttpMethod.Delete, "/api/ops/payroll/loan-kinds/" + id),
            StatusCodes.Status409Conflict);
        Assert.Equal("application/problem+json", blocked.Item2);
        Assert.Contains("Deactivate", blocked.Item1.GetProperty("detail").GetString());

        var updated = await ReadOk(await app.SendAsync(
            LoanKindApp.OwnerId,
            HttpMethod.Put,
            "/api/ops/payroll/loan-kinds/" + id,
            KindBody("Company cash advance", 2, active: false)));
        Assert.False(updated.GetProperty("active").GetBoolean());

        var stillBlocked = await ReadProblem(
            await app.SendAsync(LoanKindApp.OwnerId, HttpMethod.Delete, "/api/ops/payroll/loan-kinds/" + id),
            StatusCodes.Status409Conflict);
        Assert.Equal(409, stillBlocked.Item1.GetProperty("status").GetInt32());

        var inactive = await ReadProblem(
            await app.SendAsync(
                LoanKindApp.OwnerId,
                HttpMethod.Post,
                "/api/ops/payroll/loans",
                LoanBody(id, "Company cash advance", 150m)),
            StatusCodes.Status400BadRequest);
        Assert.Contains("not active", inactive.Item1.GetProperty("error").GetString());

        var removed = await app.SendAsync(LoanKindApp.OwnerId, HttpMethod.Delete, "/api/ops/payroll/loan-kinds/" + Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
    }

    [Fact]
    public async Task Worker_gets_403_and_admin_can_add_a_kind()
    {
        await app.ResetAsync();
        var body = KindBody("SSS loan", 1);
        foreach (var (method, url) in new (HttpMethod Method, string Url)[]
        {
            (HttpMethod.Get, "/api/ops/payroll/loan-kinds"),
            (HttpMethod.Post, "/api/ops/payroll/loan-kinds"),
            (HttpMethod.Put, "/api/ops/payroll/loan-kinds/" + Guid.Empty),
            (HttpMethod.Delete, "/api/ops/payroll/loan-kinds/" + Guid.Empty)
        })
        {
            var problem = await ReadProblem(
                await app.SendAsync(LoanKindApp.WorkerId, method, url, method == HttpMethod.Get || method == HttpMethod.Delete ? null : body),
                StatusCodes.Status403Forbidden);
            Assert.Equal("application/problem+json", problem.Item2);
            Assert.Contains("not available", problem.Item1.GetProperty("detail").GetString());
        }

        var saved = await ReadOk(await app.SendAsync(
            LoanKindApp.AdminId, HttpMethod.Post, "/api/ops/payroll/loan-kinds", body));
        Assert.Equal("SSS loan", saved.GetProperty("name").GetString());
        Assert.Equal(1, saved.GetProperty("cutoffOrdinal").GetInt32());
        Assert.True(saved.GetProperty("active").GetBoolean());
    }

    private async Task SaveCutoffs()
    {
        var response = await app.SendAsync(
            LoanKindApp.OwnerId,
            HttpMethod.Put,
            "/api/ops/payroll/cutoff-schedule?month=2026-11",
            new
            {
                mode = "schedule",
                cutoffs = new object[]
                {
                    new { ordinal = 1, startDay = 26, endDay = 10 },
                    new { ordinal = 2, startDay = 11, endDay = 25 }
                }
            });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        response.Dispose();
    }

    private async Task<Guid> CreateKind(string name, int? cutoffOrdinal)
    {
        var saved = await ReadOk(await app.SendAsync(
            LoanKindApp.OwnerId,
            HttpMethod.Post,
            "/api/ops/payroll/loan-kinds",
            KindBody(name, cutoffOrdinal)));
        return saved.GetProperty("id").GetGuid();
    }

    private async Task CreateLoan(Guid? loanKindId, string kind, decimal perPeriod)
    {
        var saved = await ReadOk(await app.SendAsync(
            LoanKindApp.OwnerId,
            HttpMethod.Post,
            "/api/ops/payroll/loans",
            LoanBody(loanKindId, kind, perPeriod)));
        if (loanKindId is Guid linked)
        {
            Assert.Equal(linked, saved.GetProperty("loanKindId").GetGuid());
        }
        else
        {
            Assert.False(saved.TryGetProperty("loanKindId", out _));
        }
    }

    private async Task<JsonElement> Register(string from, string to)
    {
        var body = await ReadOk(await app.SendAsync(
            LoanKindApp.OwnerId,
            HttpMethod.Get,
            "/api/ops/payroll?from=" + from + "&to=" + to));
        return body.GetProperty("rows").EnumerateArray()
            .Single(row => row.GetProperty("staffId").GetGuid() == LoanKindApp.WorkerStaffId);
    }

    private static Dictionary<string, decimal> Amounts(JsonElement row)
    {
        if (!row.TryGetProperty("loanItems", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return items.EnumerateArray().ToDictionary(
            item => item.GetProperty("kind").GetString()!,
            item => item.GetProperty("amount").GetDecimal());
    }

    private static object KindBody(string name, int? cutoffOrdinal, bool active = true) =>
        new { name, active, cutoffOrdinal };

    private static object LoanBody(Guid? loanKindId, string kind, decimal perPeriod) => new
    {
        staffId = LoanKindApp.WorkerStaffId,
        kind,
        principal = 5000m,
        perPeriodAmount = perPeriod,
        startPeriod = "2026-01-01",
        loanKindId
    };

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

public sealed class LoanKindApp : IAsyncLifetime
{
    public const string OwnerId = "alex-rivera";
    public const string AdminId = "sam-lee";
    public const string WorkerId = "casey-diaz";
    public static readonly Guid BranchId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid WorkerStaffId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private WebApplication app = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = "HrCore.Tests"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        var features = new HrCoreFeatures { PayrollSchedules = true, LoanKinds = true };
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot();
        builder.Services.AddDbContext<TestHrDb>(options =>
            options.UseInMemoryDatabase(databaseName, databaseRoot).UseHrCoreFeatures(features));
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

    private HttpClient Client { get; set; } = null!;

    public async Task ResetAsync()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestHrDb>();
        db.StaffLoanPayments.RemoveRange(db.StaffLoanPayments);
        db.StaffLoans.RemoveRange(db.StaffLoans);
        db.LoanKinds().RemoveRange(db.LoanKinds());
        db.BranchPayrollCutoffs().RemoveRange(db.BranchPayrollCutoffs());
        db.BranchDeductionSchedules().RemoveRange(db.BranchDeductionSchedules());
        db.SettingsChangeLogs.RemoveRange(db.SettingsChangeLogs);
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
        AddPerson(db, WorkerId, "Casey Diaz", StaffRole.Worker, BranchId, now, pay: true, WorkerStaffId);
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
        Guid? staffId = null)
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
            RatePhp = pay ? 30000m : null,
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
