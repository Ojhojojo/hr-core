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
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HrCore.Tests;

public sealed class PayrollStatutoryRateTests
{
    private static readonly DateOnly November2024 = new(2024, 11, 1);
    private static readonly DateOnly November2025 = new(2025, 11, 1);
    private static readonly DateOnly January2024 = new(2024, 1, 1);
    private static readonly DateOnly February2024 = new(2024, 2, 1);

    public static TheoryData<DateOnly, decimal> PackCases()
    {
        var data = new TheoryData<DateOnly, decimal>();
        decimal[] bases =
        [
            0m, 1_500m, 4_000m, 4_250m, 4_750m, 5_249.99m, 5_250m,
            8_000m, 14_500m, 15_000m, 19_749.99m, 19_750m, 19_999m,
            20_000m, 20_001m, 20_249.99m, 20_250m, 30_000m,
            34_749.99m, 34_750m, 120_000m
        ];
        foreach (var month in new[] { January2024, February2024, November2024, November2025 })
        {
            foreach (var amount in bases)
            {
                data.Add(month, amount);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PackCases))]
    public void Seeded_rows_match_the_pack_for_2024_and_2025(DateOnly month, decimal baseAmount)
    {
        var tables = FromSeed(month);
        Assert.True(tables.CoversSuggestions);

        Assert.Equal(StatutoryPack.SssMsc(month, baseAmount), StatutoryMath.SssMsc(tables.Sss!, baseAmount));
        Assert.Equal(
            StatutoryPack.SssEmployeeShare(month, baseAmount),
            StatutoryMath.SssEmployeeShare(tables.Sss!, baseAmount));
        Assert.Equal(
            StatutoryPack.SssEmployerShare(month, baseAmount),
            StatutoryMath.SssEmployerShare(tables.Sss!, baseAmount));
        Assert.Equal(
            StatutoryPack.PhilHealthEmployeeShare(month, baseAmount),
            StatutoryMath.PhilHealthEmployeeShare(tables.PhilHealth!, baseAmount));
        Assert.Equal(
            StatutoryPack.PhilHealthEmployerShare(month, baseAmount),
            StatutoryMath.PhilHealthEmployerShare(tables.PhilHealth!, baseAmount));
        Assert.Equal(
            StatutoryPack.PagIbigEmployeeShare(month, baseAmount),
            StatutoryMath.PagIbigEmployeeShare(tables.PagIbig!, baseAmount));
        Assert.Equal(
            StatutoryPack.PagIbigEmployerShare(month, baseAmount),
            StatutoryMath.PagIbigEmployerShare(tables.PagIbig!, baseAmount));

        var sss = StatutoryMath.SssEmployeeShare(tables.Sss!, baseAmount);
        var phil = StatutoryMath.PhilHealthEmployeeShare(tables.PhilHealth!, baseAmount);
        var pag = StatutoryMath.PagIbigEmployeeShare(tables.PagIbig!, baseAmount);
        var taxable = baseAmount - sss - phil - pag;
        Assert.Equal(
            StatutoryPack.MonthlyWithholdingTax(taxable),
            StatutoryMath.MonthlyWithholdingTax(tables.Wht!, taxable));
    }

    [Fact]
    public void A_later_row_is_used_from_its_effective_date_only()
    {
        var sss2025 = FromSeed(November2025).Sss!;
        var sss2027 = sss2025 with { EmployeeRate = 0.06m };
        var tables2026 = FromSeed(new DateOnly(2026, 11, 1));
        var tables2027 = tables2026 with { Sss = sss2027 };

        Assert.Equal(1_500m, StatutoryMath.SssEmployeeShare(tables2026.Sss!, 30_000m));
        Assert.Equal(1_800m, StatutoryMath.SssEmployeeShare(tables2027.Sss!, 30_000m));
        Assert.Equal(1_500m, StatutoryMath.SssEmployeeShare(FromSeed(November2025).Sss!, 30_000m));
    }

    [Fact]
    public void Months_before_the_first_row_have_no_tables()
    {
        var tables = FromSeed(new DateOnly(2023, 12, 1));
        Assert.False(tables.CoversSuggestions);
        Assert.Null(tables.Sss);
    }

    public static StatutoryTables FromSeed(DateOnly month)
    {
        var rates = StatutoryRateSeed.Rates();
        StatutoryRate? Pick(string kind) => rates
            .Where(row => row.Kind == kind && row.EffectiveFrom <= month)
            .OrderByDescending(row => row.EffectiveFrom)
            .FirstOrDefault();

        var sss = Pick(StatutoryRateKind.Sss);
        var phil = Pick(StatutoryRateKind.PhilHealth);
        var pag = Pick(StatutoryRateKind.PagIbig);
        var wht = Pick(StatutoryRateKind.Wht);
        IReadOnlyList<WhtRateBracket> brackets = [];
        if (wht is not null)
        {
            brackets = StatutoryRateSeed.Brackets()
                .Where(row => row.StatutoryRateId == wht.Id)
                .OrderBy(row => row.SortOrder)
                .Select(row => new WhtRateBracket(row.UpTo, row.BaseTax, row.Rate, row.ExcessOver))
                .ToList();
        }

        return new StatutoryTables(
            sss is null
                ? null
                : new SssRateTable(
                    sss.MscFloor!.Value,
                    sss.MscCeiling!.Value,
                    sss.MscStep!.Value,
                    sss.EmployeeRate!.Value,
                    sss.EmployerRate!.Value,
                    sss.EcLow!.Value,
                    sss.EcHigh!.Value,
                    sss.EcMscCeilingForLow!.Value),
            phil is null
                ? null
                : new PhilHealthRateTable(
                    phil.Floor!.Value,
                    phil.Ceiling!.Value,
                    phil.EmployeeRate!.Value,
                    phil.EmployerRate!.Value),
            pag is null
                ? null
                : new PagIbigRateTable(
                    pag.SmallSalary!.Value,
                    pag.RateSmall!.Value,
                    pag.Rate!.Value,
                    pag.Cap!.Value,
                    pag.EmployerRate!.Value),
            wht is null ? null : new WhtRateTable(wht.ExemptMonthly!.Value, brackets));
    }
}

public sealed class PayrollStatutoryRateEndpointTests(StatutoryRateApp app) : IClassFixture<StatutoryRateApp>
{
    [Fact]
    public async Task Seeded_suggestions_match_the_pack_for_2024_and_2025()
    {
        await app.ResetAsync();
        foreach (var month in new[] { "2024-01", "2024-02", "2024-11", "2025-11" })
        {
            var body = await ReadOk(await app.SendAsync(
                StatutoryRateApp.OwnerId,
                HttpMethod.Get,
                SuggestionUrl(month)));
            var start = DateOnly.Parse(month + "-01");
            var baseAmount = body.GetProperty("base").GetDecimal();
            Assert.True(baseAmount > 0m);
            Assert.Equal(StatutoryPack.SssEmployeeShare(start, baseAmount), body.GetProperty("sss").GetDecimal());
            Assert.Equal(
                StatutoryPack.PhilHealthEmployeeShare(start, baseAmount),
                body.GetProperty("philHealth").GetDecimal());
            Assert.Equal(
                StatutoryPack.PagIbigEmployeeShare(start, baseAmount),
                body.GetProperty("pagIbig").GetDecimal());
            var taxable = baseAmount
                - body.GetProperty("sss").GetDecimal()
                - body.GetProperty("philHealth").GetDecimal()
                - body.GetProperty("pagIbig").GetDecimal();
            Assert.Equal(StatutoryPack.MonthlyWithholdingTax(taxable), body.GetProperty("wht").GetDecimal());
        }
    }

    [Fact]
    public async Task A_2027_row_applies_from_then_and_earlier_months_keep_the_pack()
    {
        await app.ResetAsync();
        var created = await ReadOk(await app.SendAsync(
            StatutoryRateApp.OwnerId,
            HttpMethod.Post,
            "/api/ops/payroll/statutory-rates",
            SssBody("2027-01-01", employeeRate: 0.06m)));
        Assert.Equal("sss", created.GetProperty("kind").GetString());
        Assert.Equal("2027-01-01", created.GetProperty("effectiveFrom").GetString());

        var before = await ReadOk(await app.SendAsync(
            StatutoryRateApp.OwnerId,
            HttpMethod.Get,
            SuggestionUrl("2026-11")));
        var after = await ReadOk(await app.SendAsync(
            StatutoryRateApp.OwnerId,
            HttpMethod.Get,
            SuggestionUrl("2027-01")));
        var beforeBase = before.GetProperty("base").GetDecimal();
        var afterBase = after.GetProperty("base").GetDecimal();
        Assert.Equal(
            StatutoryPack.SssEmployeeShare(new DateOnly(2026, 11, 1), beforeBase),
            before.GetProperty("sss").GetDecimal());
        Assert.NotEqual(before.GetProperty("sss").GetDecimal(), after.GetProperty("sss").GetDecimal());
        Assert.Equal(
            StatutoryMath.SssEmployeeShare(
                PayrollStatutoryRateTests.FromSeed(new DateOnly(2025, 1, 1)).Sss! with { EmployeeRate = 0.06m },
                afterBase),
            after.GetProperty("sss").GetDecimal());
    }

    [Fact]
    public async Task Overlapping_or_invalid_rows_are_400_problem_details()
    {
        await app.ResetAsync();
        var overlap = await ReadProblem(
            await app.SendAsync(
                StatutoryRateApp.OwnerId,
                HttpMethod.Post,
                "/api/ops/payroll/statutory-rates",
                SssBody("2025-01-01", employeeRate: 0.05m)),
            StatusCodes.Status400BadRequest);
        Assert.Equal("application/problem+json", overlap.Item2);
        Assert.Contains(
            "already exists",
            overlap.Item1.GetProperty("errors").GetProperty("effectiveFrom")[0].GetString());

        var floor = await ReadProblem(
            await app.SendAsync(
                StatutoryRateApp.OwnerId,
                HttpMethod.Post,
                "/api/ops/payroll/statutory-rates",
                SssBody("2028-01-01", employeeRate: 0.05m, mscFloor: 40_000m, mscCeiling: 35_000m)),
            StatusCodes.Status400BadRequest);
        Assert.Contains("floor", floor.Item1.GetProperty("errors").GetProperty("mscFloor")[0].GetString());

        var rate = await ReadProblem(
            await app.SendAsync(
                StatutoryRateApp.OwnerId,
                HttpMethod.Post,
                "/api/ops/payroll/statutory-rates",
                SssBody("2028-01-01", employeeRate: 1.5m)),
            StatusCodes.Status400BadRequest);
        Assert.Contains("0 and 1", rate.Item1.GetProperty("errors").GetProperty("employeeRate")[0].GetString());

        var brackets = await ReadProblem(
            await app.SendAsync(
                StatutoryRateApp.OwnerId,
                HttpMethod.Post,
                "/api/ops/payroll/statutory-rates",
                new
                {
                    kind = "wht",
                    effectiveFrom = "2028-01-01",
                    exemptMonthly = 20_833m,
                    brackets = new[]
                    {
                        new { upTo = 50_000m, baseTax = 0m, rate = 0.2m, excessOver = 20_833m },
                        new { upTo = 40_000m, baseTax = 1_000m, rate = 0.25m, excessOver = 50_000m }
                    }
                }),
            StatusCodes.Status400BadRequest);
        Assert.Contains("overlap", brackets.Item1.GetProperty("errors").GetProperty("brackets[1].upTo")[0].GetString());
    }

    [Fact]
    public async Task Admin_and_worker_get_403()
    {
        await app.ResetAsync();
        var body = SssBody("2028-06-01", employeeRate: 0.05m);
        foreach (var user in new[] { StatutoryRateApp.AdminId, StatutoryRateApp.WorkerId })
        {
            foreach (var (method, url) in new (HttpMethod Method, string Url)[]
            {
                (HttpMethod.Get, "/api/ops/payroll/statutory-rates"),
                (HttpMethod.Post, "/api/ops/payroll/statutory-rates"),
                (HttpMethod.Put, "/api/ops/payroll/statutory-rates/" + Guid.Empty),
                (HttpMethod.Delete, "/api/ops/payroll/statutory-rates/" + Guid.Empty)
            })
            {
                var response = await app.SendAsync(
                    user,
                    method,
                    url,
                    method == HttpMethod.Get || method == HttpMethod.Delete ? null : body);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
        }
    }

    [Fact]
    public async Task Owner_can_update_and_delete_a_row()
    {
        await app.ResetAsync();
        var created = await ReadOk(await app.SendAsync(
            StatutoryRateApp.OwnerId,
            HttpMethod.Post,
            "/api/ops/payroll/statutory-rates",
            SssBody("2029-01-01", employeeRate: 0.055m)));
        var id = created.GetProperty("id").GetGuid();
        var updated = await ReadOk(await app.SendAsync(
            StatutoryRateApp.OwnerId,
            HttpMethod.Put,
            "/api/ops/payroll/statutory-rates/" + id,
            SssBody("2029-01-01", employeeRate: 0.07m)));
        Assert.Equal(0.07m, updated.GetProperty("employeeRate").GetDecimal());

        var deleted = await app.SendAsync(
            StatutoryRateApp.OwnerId,
            HttpMethod.Delete,
            "/api/ops/payroll/statutory-rates/" + id);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var missing = await app.SendAsync(
            StatutoryRateApp.OwnerId,
            HttpMethod.Delete,
            "/api/ops/payroll/statutory-rates/" + id);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static string SuggestionUrl(string month) =>
        $"/api/ops/payroll/statutory-suggested?staffId={StatutoryRateApp.WorkerStaffId}&month={month}";

    private static object SssBody(
        string effectiveFrom,
        decimal employeeRate,
        decimal mscFloor = 5_000m,
        decimal mscCeiling = 35_000m) =>
        new
        {
            kind = "sss",
            effectiveFrom,
            mscFloor,
            mscCeiling,
            mscStep = 500m,
            employeeRate,
            employerRate = 0.10m,
            ecLow = 10m,
            ecHigh = 30m,
            ecMscCeilingForLow = 14_500m
        };

    private static async Task<JsonElement> ReadOk(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, text);
        using var _ = response;
        return JsonSerializer.Deserialize<JsonElement>(text);
    }

    private static async Task<(JsonElement Body, string? MediaType)> ReadProblem(
        HttpResponseMessage response,
        int status)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == status, text);
        var media = response.Content.Headers.ContentType?.MediaType;
        using var _ = response;
        return (JsonSerializer.Deserialize<JsonElement>(text), media);
    }
}

public sealed class StatutoryRateApp : IAsyncLifetime
{
    public const string OwnerId = "alex-rivera";
    public const string AdminId = "sam-lee";
    public const string WorkerId = "casey-diaz";
    public static readonly Guid BranchId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid WorkerStaffId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private static readonly HashSet<Guid> SeedRateIds =
    [
        StatutoryRateSeed.Sss2024Id,
        StatutoryRateSeed.Sss2025Id,
        StatutoryRateSeed.PhilHealth2024Id,
        StatutoryRateSeed.PagIbig2024JanId,
        StatutoryRateSeed.PagIbig2024FebId,
        StatutoryRateSeed.Wht2024Id
    ];

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
        var features = new HrCoreFeatures { StatutoryRates = true };
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
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
        var extra = db.StatutoryRates().Where(row => !SeedRateIds.Contains(row.Id)).ToList();
        db.StatutoryRates().RemoveRange(extra);
        db.SettingsChangeLogs.RemoveRange(db.SettingsChangeLogs);
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
            RatePhp = pay ? 30_000m : null,
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
