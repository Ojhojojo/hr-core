using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HrCore.Data;
using HrCore.Domain;
using HrCore.Features.SettingsLog;
using HrCore.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HrCore.Tests;

public sealed class HoursPerDayApp : IAsyncLifetime
{
    public const string OwnerId = "alex-rivera";
    public const string AdminId = "sam-lee";
    public const string WorkerId = "casey-diaz";
    public static readonly Guid BranchId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid WorkerStaffId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public static readonly DateOnly LeaveDay = new(2026, 11, 4);

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

    public async Task ResetHoursAsync()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestHrDb>();
        var branch = await db.Branches.SingleAsync();
        branch.HoursPerDay = Branch.DefaultHoursPerDay;
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

    public async Task ReadAsync(Func<TestHrDb, Task> read)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestHrDb>();
        await read(db);
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
        var worker = AddPerson(db, WorkerId, "Casey Diaz", StaffRole.Worker, BranchId, now, WorkerStaffId);
        worker.PayBasis = PayBasis.Hourly;
        worker.RatePhp = 100m;
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = Guid.NewGuid(),
            StaffId = WorkerStaffId,
            BranchId = BranchId,
            LeaveType = LeaveType.Vacation,
            StartDate = LeaveDay,
            EndDate = LeaveDay,
            Status = LeaveRequestStatus.Approved,
            CreatedAtUtc = now,
            DecidedAtUtc = now
        });
        await db.SaveChangesAsync();
    }

    private static Staff AddPerson(
        TestHrDb db,
        string authUserId,
        string name,
        StaffRole role,
        Guid? branchId,
        DateTime now,
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
        var staff = new Staff
        {
            Id = staffId ?? Guid.NewGuid(),
            ProfileId = profile.Id,
            BranchId = branchId,
            Role = role,
            Active = true,
            CreatedAtUtc = now
        };
        db.Staff.Add(staff);
        return staff;
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

public sealed class HoursPerDayEndpointTests(HoursPerDayApp app) : IClassFixture<HoursPerDayApp>
{
    [Fact]
    public async Task Default_eight_leaves_hourly_leave_pay_unchanged()
    {
        await app.ResetHoursAsync();

        var settings = await ReadOk(await app.SendAsync(
            HoursPerDayApp.OwnerId, HttpMethod.Get, "/api/ops/payroll/settings"));
        Assert.Equal(8m, settings.GetProperty("hoursPerDay").GetDecimal());

        Assert.Equal(800m, await CaseyLeaveAmount());
    }

    [Fact]
    public async Task Nine_hours_pays_nine_hours_of_hourly_leave()
    {
        await app.ResetHoursAsync();

        var saved = await ReadOk(await app.SendAsync(
            HoursPerDayApp.OwnerId, HttpMethod.Put, "/api/ops/payroll/settings", SettingsBody(9m)));
        Assert.Equal(9m, saved.GetProperty("hoursPerDay").GetDecimal());
        Assert.Equal(900m, await CaseyLeaveAmount());

        await app.ReadAsync(async db =>
        {
            var log = await db.SettingsChangeLogs.SingleAsync(row => row.Field == "payroll_hours_per_day");
            Assert.Equal(SettingsArea.ShopDesk, log.Area);
            Assert.Equal("8", log.OldValue);
            Assert.Equal("9", log.NewValue);
            Assert.Equal(HoursPerDayApp.BranchId, log.BranchId);
        });
    }

    [Fact]
    public async Task Admin_can_save_hours_per_day()
    {
        await app.ResetHoursAsync();

        var saved = await ReadOk(await app.SendAsync(
            HoursPerDayApp.AdminId, HttpMethod.Put, "/api/ops/payroll/settings", SettingsBody(9.5m)));
        Assert.Equal(9.5m, saved.GetProperty("hoursPerDay").GetDecimal());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(8.25)]
    [InlineData(25)]
    public async Task Invalid_hours_are_400_problem_details(decimal hours)
    {
        await app.ResetHoursAsync();

        var problem = await ReadProblem(
            await app.SendAsync(
                HoursPerDayApp.OwnerId, HttpMethod.Put, "/api/ops/payroll/settings", SettingsBody(hours)),
            StatusCodes.Status400BadRequest);
        var message = problem.GetProperty("errors").GetProperty("hoursPerDay")[0].GetString();
        Assert.Contains("1 through 24", message);

        await app.ReadAsync(async db =>
        {
            var branch = await db.Branches.SingleAsync();
            Assert.Equal(8m, branch.HoursPerDay);
            Assert.Empty(await db.SettingsChangeLogs.ToListAsync());
        });
    }

    [Fact]
    public async Task Omitting_hours_per_day_keeps_eight()
    {
        await app.ResetHoursAsync();

        var saved = await ReadOk(await app.SendAsync(
            HoursPerDayApp.OwnerId, HttpMethod.Put, "/api/ops/payroll/settings", SettingsBody(null)));
        Assert.Equal(8m, saved.GetProperty("hoursPerDay").GetDecimal());
        Assert.Equal(800m, await CaseyLeaveAmount());
    }

    [Fact]
    public async Task Worker_gets_403()
    {
        await app.ResetHoursAsync();

        var get = await app.SendAsync(HoursPerDayApp.WorkerId, HttpMethod.Get, "/api/ops/payroll/settings");
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);

        var put = await app.SendAsync(
            HoursPerDayApp.WorkerId, HttpMethod.Put, "/api/ops/payroll/settings", SettingsBody(9m));
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        await app.ReadAsync(async db =>
        {
            var branch = await db.Branches.SingleAsync();
            Assert.Equal(8m, branch.HoursPerDay);
        });
    }

    private async Task<decimal> CaseyLeaveAmount()
    {
        var register = await ReadOk(await app.SendAsync(
            HoursPerDayApp.OwnerId,
            HttpMethod.Get,
            $"/api/ops/payroll?from={HoursPerDayApp.LeaveDay:yyyy-MM-dd}&to={HoursPerDayApp.LeaveDay:yyyy-MM-dd}"));
        var row = register.GetProperty("rows").EnumerateArray()
            .Single(item => item.GetProperty("staffId").GetGuid() == HoursPerDayApp.WorkerStaffId);
        return row.GetProperty("amount").GetDecimal();
    }

    private static object SettingsBody(decimal? hoursPerDay)
    {
        if (hoursPerDay is decimal hours)
        {
            return new
            {
                regularHolidayPercent = 200,
                specialDayPercent = 130,
                restDayPercent = 130,
                holidayPremiumEnabled = true,
                restDayPremiumEnabled = true,
                nightDiffEnabled = false,
                nightDiffPercent = 10,
                nightStart = "22:00:00",
                nightEnd = "06:00:00",
                shiftEnd = "17:00:00",
                otGraceMinutes = 30,
                adminsCanApproveOt = false,
                otPremiumPercent = 125,
                otMonthlyDayDivisor = 313,
                hoursPerDay = hours
            };
        }

        return new
        {
            regularHolidayPercent = 200,
            specialDayPercent = 130,
            restDayPercent = 130,
            holidayPremiumEnabled = true,
            restDayPremiumEnabled = true,
            nightDiffEnabled = false,
            nightDiffPercent = 10,
            nightStart = "22:00:00",
            nightEnd = "06:00:00",
            shiftEnd = "17:00:00",
            otGraceMinutes = 30,
            adminsCanApproveOt = false,
            otPremiumPercent = 125,
            otMonthlyDayDivisor = 313
        };
    }

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
