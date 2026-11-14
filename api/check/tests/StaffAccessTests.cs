using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
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

/// <summary>
/// A Testing-environment host with its own in-memory database. The signed-in caller comes from either
/// the test header (honoured only when the host opts in) or an <c>X-Test-Sub</c> header, which a test
/// middleware turns into a token-style <c>sub</c> claim.
/// </summary>
public sealed class StaffAccessApp : IAsyncDisposable
{
    public const string OwnerSub = "access-owner-sub";
    public const string AdminSub = "access-admin-sub";
    public const string NewHireSub = "access-new-hire-sub";
    public const string NewHireEmail = "new-hire@example.com";
    public const string InviteeEmail = "invitee@example.com";

    public static readonly Guid BranchId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    public static readonly Guid OwnerStaffId = Guid.Parse("44444444-4444-4444-8444-444444444444");
    public static readonly Guid AdminStaffId = Guid.Parse("55555555-5555-4555-8555-555555555555");
    public static readonly Guid NewHireStaffId = Guid.Parse("66666666-6666-4666-8666-666666666666");
    public static readonly Guid InviteeStaffId = Guid.Parse("77777777-7777-4777-8777-777777777777");

    private WebApplication app = null!;

    private StaffAccessApp()
    {
    }

    public FakeStaffInvite Invite { get; } = new();

    public HttpClient Client { get; private set; } = null!;

    /// <param name="options">Host options to register. Null means the defaults.</param>
    /// <param name="registerHostServices">Host registrations made before <c>AddHrCore</c>.</param>
    public static async Task<StaffAccessApp> StartAsync(
        HrCoreOptions? options = null,
        Action<IServiceCollection>? registerHostServices = null)
    {
        var host = new StaffAccessApp();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = "HrCore.Tests"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        // One name for the whole host. The options lambda runs for every context, so a new name there
        // would give each request an empty database.
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot();
        builder.Services.AddDbContext<TestHrDb>(options =>
            options.UseInMemoryDatabase(databaseName, databaseRoot));
        if (options is not null)
        {
            builder.Services.AddSingleton(options);
        }

        registerHostServices?.Invoke(builder.Services);
        builder.Services.AddHrCore<TestHrDb>();
        builder.Services.AddSingleton(new DemoBranchOptions { Enabled = false });
        builder.Services.AddSingleton<IStaffInvite>(host.Invite);
        builder.Services.AddSingleton<IFileStore, NoFileStore>();
        builder.Services.AddSingleton<IHrFileStore, NoHrFileStore>();

        host.app = builder.Build();
        host.app.Use(async (context, next) =>
        {
            var sub = context.Request.Headers["X-Test-Sub"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(sub))
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", sub) }, "test-token"));
            }

            await next();
        });
        host.app.MapHrCore();
        await host.app.StartAsync();
        host.Client = new HttpClient { BaseAddress = new Uri(host.app.Urls.First()) };
        await host.SeedAsync();
        return host;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.StopAsync();
        await app.DisposeAsync();
    }

    /// <param name="testAuthUserId">Sent as the test header (<c>X-Test-Auth-User-Id</c>).</param>
    /// <param name="sub">Sent as <c>X-Test-Sub</c>, which becomes a token <c>sub</c> claim.</param>
    public Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        string? testAuthUserId = null,
        string? sub = null)
    {
        var message = new HttpRequestMessage(method, url);
        if (testAuthUserId is not null)
        {
            message.Headers.TryAddWithoutValidation(StaffAuthorizationFilter.TestAuthUserHeader, testAuthUserId);
        }

        if (sub is not null)
        {
            message.Headers.TryAddWithoutValidation("X-Test-Sub", sub);
        }

        message.Headers.TryAddWithoutValidation(BranchResolver.HeaderName, "north-desk");
        return Client.SendAsync(message);
    }

    public async Task ReadAsync(Func<TestHrDb, Task> read)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await read(scope.ServiceProvider.GetRequiredService<TestHrDb>());
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
            LegalName = "Access Co",
            TradingName = "Access Co",
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
        // The company owner has no branch, so covers every shop.
        AddPerson(db, OwnerStaffId, OwnerSub, "owner@example.com", "Alex Owner", StaffRole.Owner, null, now);
        AddPerson(db, AdminStaffId, AdminSub, "admin@example.com", "Sam Admin", StaffRole.Admin, BranchId, now);
        // Linked by the host's resolver only, so no auth id yet.
        AddPerson(db, NewHireStaffId, null, NewHireEmail, "New Hire", StaffRole.Worker, BranchId, now);
        // Invited by the owner in the invite tests. No auth id yet.
        AddPerson(db, InviteeStaffId, null, InviteeEmail, "Invitee", StaffRole.Worker, BranchId, now);
        await db.SaveChangesAsync();
    }

    private static void AddPerson(
        TestHrDb db,
        Guid staffId,
        string? authUserId,
        string email,
        string name,
        StaffRole role,
        Guid? branchId,
        DateTime now)
    {
        var profile = new Profile
        {
            Id = Guid.NewGuid(),
            AuthUserId = authUserId,
            Kind = ProfileKind.Staff,
            DisplayName = name,
            Email = email,
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
            CreatedAtUtc = now
        });
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

public sealed class FakeStaffInvite : IStaffInvite
{
    public StaffInviteResult Result { get; set; } = new(true, null, true, null);

    public Task<StaffInviteResult> InviteAsync(string staffEmail, string displayName, CancellationToken cancellationToken) =>
        Task.FromResult(Result);
}

/// <summary>A host resolver that links a person by verified email, as Qurio's directory does.</summary>
public sealed class EmailStaffResolver(IHrDb db) : IHrStaffResolver
{
    public async Task<Staff?> FindAsync(HttpContext http, CancellationToken cancellationToken)
    {
        if (http.User.FindFirstValue("sub") != StaffAccessApp.NewHireSub)
        {
            return null;
        }

        return await db.Staff.AsNoTracking()
            .Include(s => s.Profile)
            .FirstOrDefaultAsync(
                s => s.Profile.Email == StaffAccessApp.NewHireEmail && s.Active,
                cancellationToken);
    }
}

public sealed class StaffAccessTests
{
    [Fact]
    public async Task Testing_env_without_option_ignores_test_header_and_returns_401()
    {
        await using var app = await StaffAccessApp.StartAsync();

        var response = await app.SendAsync(HttpMethod.Get, "/api/employee-directory", testAuthUserId: StaffAccessApp.AdminSub);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Testing_env_without_option_and_no_token_returns_401()
    {
        await using var app = await StaffAccessApp.StartAsync();

        var list = await app.SendAsync(HttpMethod.Get, "/api/employee-directory");
        var detail = await app.SendAsync(HttpMethod.Get, $"/api/employee-directory/{StaffAccessApp.OwnerStaffId}");

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, detail.StatusCode);
    }

    [Fact]
    public async Task Testing_env_with_option_honours_test_header()
    {
        await using var app = await StaffAccessApp.StartAsync(
            new HrCoreOptions { AllowTestAuthHeader = true });

        var response = await app.SendAsync(HttpMethod.Get, "/api/employee-directory", testAuthUserId: StaffAccessApp.AdminSub);

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Default_resolver_matches_sign_in_id_only()
    {
        await using var app = await StaffAccessApp.StartAsync();

        // The new hire has an email but no auth id yet, so the default lookup finds no staff row.
        var response = await app.SendAsync(
            HttpMethod.Get,
            $"/api/employee-directory/{StaffAccessApp.NewHireStaffId}",
            sub: StaffAccessApp.NewHireSub);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Host_resolver_registered_before_AddHrCore_is_used()
    {
        await using var app = await StaffAccessApp.StartAsync(
            registerHostServices: services => services.AddScoped<IHrStaffResolver, EmailStaffResolver>());

        var response = await app.SendAsync(
            HttpMethod.Get,
            $"/api/employee-directory/{StaffAccessApp.NewHireStaffId}",
            sub: StaffAccessApp.NewHireSub);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Invite_with_unconfirmed_account_does_not_link_auth_id()
    {
        await using var app = await StaffAccessApp.StartAsync();
        app.Invite.Result = new StaffInviteResult(true, "invitee-sub", true, null) { AccountConfirmed = false };

        var response = await app.SendAsync(
            HttpMethod.Post,
            $"/api/staff/{StaffAccessApp.InviteeStaffId}/resend-invite",
            sub: StaffAccessApp.OwnerSub);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await app.ReadAsync(async db =>
        {
            var profile = await db.Profiles.SingleAsync(p => p.Email == StaffAccessApp.InviteeEmail);
            Assert.Null(profile.AuthUserId);
        });
    }

    [Fact]
    public async Task Invite_with_confirmed_account_links_auth_id()
    {
        await using var app = await StaffAccessApp.StartAsync();
        app.Invite.Result = new StaffInviteResult(true, "invitee-sub", true, null) { AccountConfirmed = true };

        var response = await app.SendAsync(
            HttpMethod.Post,
            $"/api/staff/{StaffAccessApp.InviteeStaffId}/resend-invite",
            sub: StaffAccessApp.OwnerSub);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await app.ReadAsync(async db =>
        {
            var profile = await db.Profiles.SingleAsync(p => p.Email == StaffAccessApp.InviteeEmail);
            Assert.Equal("invitee-sub", profile.AuthUserId);
        });
    }

    [Fact]
    public async Task Owner_without_branch_is_listed_in_a_shop_and_opens_from_it()
    {
        await using var app = await StaffAccessApp.StartAsync(
            new HrCoreOptions { AllowTestAuthHeader = true });

        var list = await app.SendAsync(
            HttpMethod.Get,
            "/api/employee-directory",
            testAuthUserId: StaffAccessApp.AdminSub);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var body = await list.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();
        Assert.Contains(StaffAccessApp.OwnerStaffId, ids);
        Assert.Contains(StaffAccessApp.AdminStaffId, ids);

        var detail = await app.SendAsync(
            HttpMethod.Get,
            $"/api/employee-directory/{StaffAccessApp.OwnerStaffId}",
            testAuthUserId: StaffAccessApp.AdminSub);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
    }
}
