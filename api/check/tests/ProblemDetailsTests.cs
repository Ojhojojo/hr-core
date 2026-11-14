using System.Net;
using System.Text.Json;

namespace HrCore.Tests;

/// <summary>
/// hr-core errors are RFC 9457 problem details. The old <c>error</c> field stays, with the same text,
/// so clients that still read it keep working.
/// </summary>
public sealed class ProblemDetailsTests(HoursPerDayApp app) : IClassFixture<HoursPerDayApp>
{
    [Fact]
    public async Task Not_found_is_a_problem_with_detail_and_the_same_text_in_error()
    {
        using var response = await app.SendAsync(
            HoursPerDayApp.OwnerId,
            HttpMethod.Put,
            $"/api/announcements/{Guid.NewGuid()}",
            new { title = "Closed early", body = "The shop closes at noon.", startDate = "2026-11-14", endDate = "2026-11-14", active = true });

        var problem = await AssertProblemAsync(response, HttpStatusCode.NotFound);
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task Conflict_is_a_problem_with_the_exact_message_in_detail_and_error()
    {
        using var response = await app.SendAsync(
            HoursPerDayApp.OwnerId,
            HttpMethod.Post,
            "/api/staff",
            new { email = HoursPerDayApp.OwnerId + "@example.com", displayName = "Alex Rivera", branchId = HoursPerDayApp.BranchId });

        var problem = await AssertProblemAsync(response, HttpStatusCode.Conflict);
        Assert.Equal("That email is already on staff.", problem.GetProperty("detail").GetString());
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement.Clone();
        Assert.Equal((int)status, root.GetProperty("status").GetInt32());
        Assert.Equal(root.GetProperty("detail").GetString(), root.GetProperty("error").GetString());
        return root;
    }
}
