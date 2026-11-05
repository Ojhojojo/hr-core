using System.Net;
using System.Text.Json;
using HrCore.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Tests;

public sealed class PayrollDeductionScheduleEndpointTests(CutoffApp app) : IClassFixture<CutoffApp>
{
    [Fact]
    public async Task No_rules_means_prorate()
    {
        await app.ResetAsync();
        var body = await ReadOk(await app.SendAsync(
            CutoffApp.OwnerId, HttpMethod.Get, "/api/ops/payroll/deduction-schedule"));
        Assert.Equal(0, body.GetProperty("cutoffCount").GetInt32());
        Assert.Equal(0, body.GetProperty("rules").GetArrayLength());
    }

    [Fact]
    public async Task Owner_can_save_sss_on_cut_off_2()
    {
        await app.ResetAsync();
        await SaveCutoffs();

        var saved = await ReadOk(await app.SendAsync(
            CutoffApp.OwnerId,
            HttpMethod.Put,
            "/api/ops/payroll/deduction-schedule",
            Rule("sss", "cutoff", 2)));
        var rule = saved.GetProperty("rules").EnumerateArray().Single();
        Assert.Equal("sss", rule.GetProperty("deductionKey").GetString());
        Assert.Equal("cutoff", rule.GetProperty("mode").GetString());
        Assert.Equal(2, rule.GetProperty("cutoffOrdinal").GetInt32());
    }

    [Fact]
    public async Task Cut_off_3_is_400_when_only_two_exist()
    {
        await app.ResetAsync();
        await SaveCutoffs();

        var problem = await ReadProblem(
            await app.SendAsync(
                CutoffApp.OwnerId,
                HttpMethod.Put,
                "/api/ops/payroll/deduction-schedule",
                Rule("sss", "cutoff", 3)),
            StatusCodes.Status400BadRequest);
        Assert.Contains("does not exist", problem.Item1.GetProperty("errors").GetProperty("rules[0].cutoffOrdinal")[0].GetString());
    }

    [Fact]
    public async Task Worker_gets_403_problem_details()
    {
        await app.ResetAsync();
        var get = await ReadProblem(
            await app.SendAsync(CutoffApp.WorkerId, HttpMethod.Get, "/api/ops/payroll/deduction-schedule"),
            StatusCodes.Status403Forbidden);
        Assert.Equal("application/problem+json", get.Item2);
        Assert.Contains("not available", get.Item1.GetProperty("detail").GetString());

        var put = await ReadProblem(
            await app.SendAsync(CutoffApp.WorkerId, HttpMethod.Put, "/api/ops/payroll/deduction-schedule", Rule("sss", "cutoff", 1)),
            StatusCodes.Status403Forbidden);
        Assert.Equal(403, put.Item1.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Catch_up_requires_a_note_and_stores_the_period_dates()
    {
        await app.ResetAsync();
        Guid staffId = Guid.Empty;
        await app.ReadAsync(async db =>
        {
            staffId = await db.Staff.Where(row => row.Role == StaffRole.Owner).Select(row => row.Id).SingleAsync();
        });

        var missing = await ReadProblem(
            await app.SendAsync(
                CutoffApp.OwnerId,
                HttpMethod.Put,
                "/api/ops/payroll/catch-ups",
                new
                {
                    staffId,
                    periodFrom = "2026-11-11",
                    periodTo = "2026-11-25",
                    deductionKey = "sss",
                    amount = 150.50m,
                    note = ""
                }),
            StatusCodes.Status400BadRequest);
        Assert.Contains("needs a note", missing.Item1.GetProperty("errors").GetProperty("note")[0].GetString());

        var saved = await ReadOk(await app.SendAsync(
            CutoffApp.OwnerId,
            HttpMethod.Put,
            "/api/ops/payroll/catch-ups",
            new
            {
                staffId,
                periodFrom = "2026-11-11",
                periodTo = "2026-11-25",
                deductionKey = "sss",
                amount = 150.50m,
                note = "Missed the earlier period"
            }));
        Assert.Equal("2026-11-11", saved.GetProperty("periodFrom").GetString());
        Assert.Equal("2026-11-25", saved.GetProperty("periodTo").GetString());
        Assert.Equal(150.50m, saved.GetProperty("amount").GetDecimal());
        Assert.False(saved.TryGetProperty("cutoffOrdinal", out _));

        await app.ReadAsync(async db =>
        {
            var row = await db.PayrollCatchUps.SingleAsync();
            Assert.Equal(new DateOnly(2026, 11, 11), row.PeriodFrom);
            Assert.Equal(new DateOnly(2026, 11, 25), row.PeriodTo);
            Assert.Equal("Missed the earlier period", row.Note);
        });
    }

    private static object Rule(string key, string mode, int? ordinal) => new
    {
        rules = new[]
        {
            new { deductionKey = key, mode, cutoffOrdinal = ordinal }
        }
    };

    private async Task SaveCutoffs()
    {
        var response = await app.SendAsync(
            CutoffApp.OwnerId,
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
    }

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
