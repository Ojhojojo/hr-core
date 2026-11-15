using System.Net;
using System.Text.Json;
using HrCore.Domain;
using HrCore.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Tests;

/// <summary>
/// Shares one seeded app across this class. The seed adds people, leave, punches and change-log rows
/// on top of the common fixture, so the paging counts are known.
/// </summary>
public sealed class HrPagingFixture : IAsyncLifetime
{
    public const int PagingWorkers = 7;
    public const int PendingLeave = 4;
    public const int DecidedLeave = 2;
    public const int PendingPunches = 3;
    public const int ApprovedPunches = 2;
    public const int RateChanges = 3;
    public const int ActiveChanges = 2;

    public HoursPerDayApp App { get; } = new();

    public async Task InitializeAsync()
    {
        await App.InitializeAsync();
        await App.ReadAsync(SeedAsync);
    }

    public Task DisposeAsync() => App.DisposeAsync();

    private static async Task SeedAsync(TestHrDb db)
    {
        var now = DateTime.UtcNow;
        var people = new List<Staff>();
        for (var i = 1; i <= PagingWorkers; i++)
        {
            people.Add(AddPerson(db, $"paging-{i:00}", $"Paging Worker {i:00}", now));
        }

        people.Add(AddPerson(db, "paging-inactive", "Paging Worker Gone", now, active: false));
        await db.SaveChangesAsync();

        var worker = HoursPerDayApp.WorkerStaffId;
        for (var i = 0; i < PendingLeave; i++)
        {
            db.LeaveRequests.Add(Leave(people[i].Id, new DateOnly(2026, 12, 1).AddDays(i * 2), LeaveRequestStatus.Pending, now));
        }

        for (var i = 0; i < DecidedLeave; i++)
        {
            db.LeaveRequests.Add(Leave(people[i].Id, new DateOnly(2026, 12, 20).AddDays(i * 2), LeaveRequestStatus.Approved, now));
        }

        for (var i = 0; i < PendingPunches; i++)
        {
            db.AttendancePunches.Add(Punch(people[i].Id, new DateTime(2026, 11, 2 + i, 1, 0, 0, DateTimeKind.Utc), PunchApprovalStatus.Pending));
        }

        for (var i = 0; i < ApprovedPunches; i++)
        {
            db.AttendancePunches.Add(Punch(people[PendingPunches + i].Id, new DateTime(2026, 11, 10 + i, 1, 0, 0, DateTimeKind.Utc), PunchApprovalStatus.Approved));
        }

        for (var i = 0; i < RateChanges; i++)
        {
            db.HrChangeLogs.Add(Change(people[i].Id, "staff.rate", now.AddMinutes(i)));
        }

        for (var i = 0; i < ActiveChanges; i++)
        {
            db.HrChangeLogs.Add(Change(worker, "staff.active", now.AddMinutes(10 + i)));
        }

        await db.SaveChangesAsync();
    }

    private static Staff AddPerson(TestHrDb db, string authUserId, string name, DateTime now, bool active = true)
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
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            BranchId = HoursPerDayApp.BranchId,
            Role = StaffRole.Worker,
            Active = active,
            CreatedAtUtc = now
        };
        db.Staff.Add(staff);
        return staff;
    }

    private static LeaveRequest Leave(Guid staffId, DateOnly day, LeaveRequestStatus status, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        StaffId = staffId,
        BranchId = HoursPerDayApp.BranchId,
        LeaveType = LeaveType.Vacation,
        StartDate = day,
        EndDate = day,
        Status = status,
        CreatedAtUtc = now,
        DecidedAtUtc = status == LeaveRequestStatus.Pending ? null : now
    };

    private static AttendancePunch Punch(Guid staffId, DateTime inAtUtc, PunchApprovalStatus approval) => new()
    {
        Id = Guid.NewGuid(),
        StaffId = staffId,
        BranchId = HoursPerDayApp.BranchId,
        InAtUtc = inAtUtc,
        OutAtUtc = inAtUtc.AddHours(8),
        ApprovalStatus = approval
    };

    private static HrChangeLog Change(Guid subjectStaffId, string field, DateTime atUtc) => new()
    {
        Id = Guid.NewGuid(),
        SubjectStaffId = subjectStaffId,
        AtUtc = atUtc,
        Field = field,
        OldValue = "1.00",
        NewValue = "2.00"
    };
}

public sealed class HrPagingTests(HrPagingFixture fixture) : IClassFixture<HrPagingFixture>
{
    private HoursPerDayApp App => fixture.App;

    [Fact]
    public async Task Directory_without_page_keeps_the_old_shape()
    {
        var body = await GetJsonAsync("/api/employee-directory");

        Assert.Equal(JsonValueKind.Object, body.ValueKind);
        Assert.True(body.TryGetProperty("headcount", out _));
        Assert.Equal(JsonValueKind.Array, body.GetProperty("items").ValueKind);
        Assert.True(body.TryGetProperty("configHealth", out _));
        Assert.False(body.TryGetProperty("total", out _));
        Assert.False(body.TryGetProperty("pageNumber", out _));
    }

    [Fact]
    public async Task Directory_page_returns_the_page_with_total_and_the_same_people_in_the_same_order()
    {
        var all = await GetJsonAsync("/api/employee-directory");
        var legacyIds = Ids(all.GetProperty("items"));

        var body = await GetJsonAsync("/api/employee-directory?page=1&pageSize=2");

        Assert.Equal(2, body.GetProperty("items").GetArrayLength());
        Assert.Equal(legacyIds.Count, body.GetProperty("total").GetInt32());
        Assert.Equal(1, body.GetProperty("pageNumber").GetInt32());
        Assert.Equal(2, body.GetProperty("pageSize").GetInt32());
        Assert.Equal(legacyIds.Take(2), Ids(body.GetProperty("items")));
        Assert.Equal(all.GetProperty("headcount").GetInt32(), body.GetProperty("headcount").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("items")[0].GetProperty("displayName").GetString()));
    }

    [Fact]
    public async Task Directory_page_past_the_end_is_empty_with_the_total()
    {
        var total = (await GetJsonAsync("/api/employee-directory")).GetProperty("items").GetArrayLength();

        var body = await GetJsonAsync("/api/employee-directory?page=99&pageSize=2");

        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.Equal(total, body.GetProperty("total").GetInt32());
        Assert.Equal(99, body.GetProperty("pageNumber").GetInt32());
    }

    [Fact]
    public async Task Directory_pages_cover_every_person_once()
    {
        var total = (await GetJsonAsync("/api/employee-directory")).GetProperty("items").GetArrayLength();
        var seen = new List<string>();
        for (var page = 1; ; page++)
        {
            var body = await GetJsonAsync($"/api/employee-directory?page={page}&pageSize=4");
            var ids = Ids(body.GetProperty("items"));
            if (ids.Count == 0)
            {
                break;
            }

            seen.AddRange(ids);
        }

        Assert.Equal(total, seen.Count);
        Assert.Equal(total, seen.Distinct().Count());
    }

    [Fact]
    public async Task Directory_search_and_inactive_filter_apply_before_paging()
    {
        var body = await GetJsonAsync("/api/employee-directory?search=paging%20worker%200&page=1&pageSize=2");

        Assert.Equal(HrPagingFixture.PagingWorkers, body.GetProperty("total").GetInt32());
        Assert.Equal(2, body.GetProperty("items").GetArrayLength());
        Assert.All(body.GetProperty("items").EnumerateArray(), item =>
            Assert.Contains("Paging Worker 0", item.GetProperty("displayName").GetString()));

        var withInactive = await GetJsonAsync("/api/employee-directory?search=paging%20worker&includeInactive=true&page=1&pageSize=2");
        Assert.Equal(HrPagingFixture.PagingWorkers + 1, withInactive.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("page=0&pageSize=500", 1, 200)]
    [InlineData("page=-3&pageSize=0", 1, HrPaging.DefaultPageSize)]
    [InlineData("page=2", 2, HrPaging.DefaultPageSize)]
    public async Task Directory_page_values_are_clamped_like_the_shared_rules(string query, int pageNumber, int pageSize)
    {
        var body = await GetJsonAsync($"/api/employee-directory?{query}");

        Assert.Equal(pageNumber, body.GetProperty("pageNumber").GetInt32());
        Assert.Equal(pageSize, body.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Register_without_page_keeps_the_rows_array()
    {
        var body = await GetJsonAsync("/api/ops/attendance/register?from=2026-11-01&to=2026-11-30");

        Assert.Equal(JsonValueKind.Array, body.GetProperty("rows").ValueKind);
        Assert.False(body.TryGetProperty("items", out _));
        Assert.False(body.TryGetProperty("total", out _));
    }

    [Fact]
    public async Task Register_page_returns_items_total_and_the_period()
    {
        var total = HrPagingFixture.PendingPunches + HrPagingFixture.ApprovedPunches;

        var body = await GetJsonAsync("/api/ops/attendance/register?from=2026-11-01&to=2026-11-30&page=1&pageSize=2");

        Assert.Equal(2, body.GetProperty("items").GetArrayLength());
        Assert.Equal(total, body.GetProperty("total").GetInt32());
        Assert.Equal(1, body.GetProperty("pageNumber").GetInt32());
        Assert.Equal(2, body.GetProperty("pageSize").GetInt32());
        Assert.Equal("2026-11-01", body.GetProperty("from").GetString());
        Assert.Equal("2026-11-30", body.GetProperty("to").GetString());
        Assert.Equal(HrPagingFixture.PendingPunches, body.GetProperty("pendingTotal").GetInt32());
    }

    [Fact]
    public async Task Register_approval_filter_applies_before_paging()
    {
        // approval=pending: 0 is Pending in the enum's JSON number form, but the filter is by name.
        var body = await GetJsonAsync("/api/ops/attendance/register?from=2026-11-01&to=2026-11-30&approval=pending&page=1&pageSize=2");

        Assert.Equal(HrPagingFixture.PendingPunches, body.GetProperty("total").GetInt32());
        Assert.Equal(2, body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Leave_without_page_is_still_an_array()
    {
        var body = await GetJsonAsync("/api/leave?year=2026");

        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(HrPagingFixture.PendingLeave + HrPagingFixture.DecidedLeave + 1, body.GetArrayLength());
    }

    [Fact]
    public async Task Leave_page_returns_items_and_total()
    {
        var body = await GetJsonAsync("/api/leave?year=2026&page=2&pageSize=3");

        Assert.Equal(3, body.GetProperty("items").GetArrayLength());
        Assert.Equal(HrPagingFixture.PendingLeave + HrPagingFixture.DecidedLeave + 1, body.GetProperty("total").GetInt32());
        Assert.Equal(2, body.GetProperty("pageNumber").GetInt32());
        Assert.Equal(3, body.GetProperty("pageSize").GetInt32());
        Assert.True(body.GetProperty("items")[0].TryGetProperty("displayName", out _));
    }

    [Fact]
    public async Task Leave_status_filter_applies_before_paging()
    {
        var pending = await GetJsonAsync("/api/leave?year=2026&status=pending&page=1&pageSize=2");
        Assert.Equal(HrPagingFixture.PendingLeave, pending.GetProperty("total").GetInt32());
        Assert.Equal(2, pending.GetProperty("items").GetArrayLength());
        Assert.All(pending.GetProperty("items").EnumerateArray(), item =>
            Assert.Equal((int)LeaveRequestStatus.Pending, item.GetProperty("status").GetInt32()));

        var decided = await GetJsonAsync("/api/leave?year=2026&status=decided&page=1&pageSize=50");
        Assert.Equal(HrPagingFixture.DecidedLeave + 1, decided.GetProperty("total").GetInt32());
        Assert.All(decided.GetProperty("items").EnumerateArray(), item =>
            Assert.NotEqual((int)LeaveRequestStatus.Pending, item.GetProperty("status").GetInt32()));

        // The legacy array honours the same filter, so the two modes agree.
        var legacyDecided = await GetJsonAsync("/api/leave?year=2026&status=decided");
        Assert.Equal(decided.GetProperty("total").GetInt32(), legacyDecided.GetArrayLength());
    }

    [Fact]
    public async Task Leave_unknown_status_is_a_problem()
    {
        using var response = await App.SendAsync(HoursPerDayApp.OwnerId, HttpMethod.Get, "/api/leave?year=2026&status=bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Status must be pending or decided.", await DetailAsync(response));
    }

    [Fact]
    public async Task Change_log_without_page_keeps_the_rows_object()
    {
        var body = await GetJsonAsync("/api/hr/change-log");

        Assert.Equal(JsonValueKind.Array, body.GetProperty("rows").ValueKind);
        Assert.False(body.TryGetProperty("items", out _));
        Assert.False(body.TryGetProperty("total", out _));
    }

    [Fact]
    public async Task Change_log_page_returns_items_and_total()
    {
        var total = HrPagingFixture.RateChanges + HrPagingFixture.ActiveChanges;

        var body = await GetJsonAsync("/api/hr/change-log?page=1&pageSize=2");

        Assert.Equal(2, body.GetProperty("items").GetArrayLength());
        Assert.Equal(total, body.GetProperty("total").GetInt32());
        Assert.Equal(1, body.GetProperty("pageNumber").GetInt32());
        Assert.Equal(2, body.GetProperty("pageSize").GetInt32());
        Assert.True(body.GetProperty("items")[0].TryGetProperty("subjectName", out _));
    }

    [Fact]
    public async Task Change_log_field_filter_applies_before_paging_and_past_the_end_is_empty()
    {
        var body = await GetJsonAsync("/api/hr/change-log?field=staff.rate&page=1&pageSize=2");

        Assert.Equal(HrPagingFixture.RateChanges, body.GetProperty("total").GetInt32());
        Assert.Equal(2, body.GetProperty("items").GetArrayLength());
        Assert.All(body.GetProperty("items").EnumerateArray(), item =>
            Assert.Equal("staff.rate", item.GetProperty("field").GetString()));

        var past = await GetJsonAsync("/api/hr/change-log?field=staff.rate&page=5&pageSize=2");
        Assert.Empty(past.GetProperty("items").EnumerateArray());
        Assert.Equal(HrPagingFixture.RateChanges, past.GetProperty("total").GetInt32());
    }

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        using var response = await App.SendAsync(HoursPerDayApp.OwnerId, HttpMethod.Get, url);
        Assert.True(response.IsSuccessStatusCode, $"GET {url} returned {(int)response.StatusCode}");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<string?> DetailAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("detail").GetString();
    }

    private static List<string> Ids(JsonElement items) =>
        items.EnumerateArray().Select(item => item.GetProperty("id").GetString()!).ToList();
}
