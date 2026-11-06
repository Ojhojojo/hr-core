using HrCore.Features.Payroll;

namespace HrCore.Tests;

public sealed class PayrollCutoffCalendarTests
{
    private static PayrollCutoffRule Rule(int ordinal, int start, int end) => new(ordinal, start, end);

    private static IReadOnlyList<PayrollCutoffRule> SemiMonthly() =>
    [
        Rule(1, 26, 10),
        Rule(2, 11, 25)
    ];

    [Fact]
    public void November_periods_match_the_saved_days()
    {
        var periods = PayrollCutoffCalendar.PeriodsForMonth(2026, 11, SemiMonthly());

        Assert.Equal(2, periods.Count);
        Assert.Equal(new DateOnly(2026, 10, 26), periods[0].From);
        Assert.Equal(new DateOnly(2026, 11, 10), periods[0].To);
        Assert.Equal(new DateOnly(2026, 11, 11), periods[1].From);
        Assert.Equal(new DateOnly(2026, 11, 25), periods[1].To);
    }

    [Fact]
    public void End_day_31_clamps_in_a_30_day_month()
    {
        var rules = new[] { Rule(1, 1, 25), Rule(2, 26, 31) };
        Assert.True(PayrollCutoffCalendar.TryValidate(rules, out _));

        var april = PayrollCutoffCalendar.PeriodsForMonth(2026, 4, rules);

        Assert.Equal(new DateOnly(2026, 4, 26), april[1].From);
        Assert.Equal(new DateOnly(2026, 4, 30), april[1].To);
    }

    [Fact]
    public void End_day_31_clamps_in_February()
    {
        var rules = new[] { Rule(1, 1, 25), Rule(2, 26, 31) };

        var plain = PayrollCutoffCalendar.PeriodsForMonth(2026, 2, rules);
        Assert.Equal(new DateOnly(2026, 2, 28), plain[1].To);

        var leap = PayrollCutoffCalendar.PeriodsForMonth(2028, 2, rules);
        Assert.Equal(new DateOnly(2028, 2, 29), leap[1].To);
    }

    [Fact]
    public void Start_day_in_the_previous_month_rolls_the_year()
    {
        var periods = PayrollCutoffCalendar.PeriodsForMonth(2026, 1, SemiMonthly());

        Assert.Equal(new DateOnly(2025, 12, 26), periods[0].From);
        Assert.Equal(new DateOnly(2026, 1, 10), periods[0].To);
        Assert.Equal(new DateOnly(2026, 1, 11), periods[1].From);
        Assert.Equal(new DateOnly(2026, 1, 25), periods[1].To);
    }

    [Fact]
    public void Start_day_31_clamps_to_the_previous_month_end()
    {
        var rules = new[] { Rule(1, 31, 10), Rule(2, 11, 30) };
        Assert.True(PayrollCutoffCalendar.TryValidate(rules, out _));

        var march = PayrollCutoffCalendar.PeriodsForMonth(2026, 3, rules);

        Assert.Equal(new DateOnly(2026, 2, 28), march[0].From);
        Assert.Equal(new DateOnly(2026, 3, 10), march[0].To);
    }

    [Fact]
    public void A_full_month_and_two_halves_are_valid()
    {
        Assert.True(PayrollCutoffCalendar.TryValidate([Rule(1, 1, 31)], out _));
        Assert.True(PayrollCutoffCalendar.TryValidate([Rule(1, 1, 15), Rule(2, 16, 31)], out _));
        Assert.True(PayrollCutoffCalendar.TryValidate(SemiMonthly(), out _));
    }

    [Fact]
    public void Overlap_is_a_field_error()
    {
        var rules = new[] { Rule(1, 26, 10), Rule(2, 10, 25) };

        var ok = PayrollCutoffCalendar.TryValidate(rules, out var errors);

        Assert.False(ok);
        Assert.NotNull(errors);
        Assert.Contains("Day 10 is already in cut-off 1.", errors["cutoffs[1].startDay"]);
    }

    [Fact]
    public void A_gap_is_a_field_error()
    {
        var ok = PayrollCutoffCalendar.TryValidate([Rule(1, 11, 25)], out var errors);

        Assert.False(ok);
        Assert.NotNull(errors);
        Assert.Contains(
            "Days not covered: 1-10, 26-31. Every day from 1 through 31 must belong to one cut-off.",
            errors["cutoffs"]);
    }

    [Fact]
    public void Days_outside_1_through_31_are_field_errors()
    {
        var ok = PayrollCutoffCalendar.TryValidate([Rule(1, 0, 32)], out var errors);

        Assert.False(ok);
        Assert.NotNull(errors);
        Assert.Contains("Start day must be from 1 through 31.", errors["cutoffs[0].startDay"]);
        Assert.Contains("End day must be from 1 through 31.", errors["cutoffs[0].endDay"]);
    }

    [Fact]
    public void Ordinals_must_be_contiguous_from_1()
    {
        var ok = PayrollCutoffCalendar.TryValidate([Rule(1, 1, 15), Rule(3, 16, 31)], out var errors);

        Assert.False(ok);
        Assert.NotNull(errors);
        Assert.Contains("Ordinals must run 1, 2, 3, … in the order listed.", errors["cutoffs[1].ordinal"]);
    }

    [Fact]
    public void Blank_ordinals_are_numbered_in_list_order()
    {
        var numbered = PayrollCutoffCalendar.WithOrdinals([Rule(0, 1, 15), Rule(0, 16, 31)]);

        Assert.Equal(1, numbered[0].Ordinal);
        Assert.Equal(2, numbered[1].Ordinal);
    }

    [Fact]
    public void Empty_and_too_many_cutoffs_are_rejected()
    {
        Assert.False(PayrollCutoffCalendar.TryValidate([], out var empty));
        Assert.Contains("Add at least one cut-off.", empty!["cutoffs"]);

        var tooMany = Enumerable.Range(1, PayrollCutoffCalendar.MaxPerBranch + 1)
            .Select(ordinal => Rule(ordinal, 1, 1))
            .ToList();
        Assert.False(PayrollCutoffCalendar.TryValidate(tooMany, out var errors));
        Assert.Contains(
            $"A branch can have at most {PayrollCutoffCalendar.MaxPerBranch} cut-offs.",
            errors!["cutoffs"]);
    }

    [Fact]
    public void Describe_is_free_until_rules_exist()
    {
        Assert.Equal("free", PayrollCutoffCalendar.Describe([]));
        Assert.Equal("1:26-10;2:11-25", PayrollCutoffCalendar.Describe(SemiMonthly()));
    }

    [Fact]
    public void Boundary_days_of_26_10_and_11_25_fall_in_exactly_one_period()
    {
        var periods = PayrollCutoffCalendar.PeriodsCovering(
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 12, 31),
            SemiMonthly());

        Assert.Equal(1, Covering(periods, new DateOnly(2026, 11, 10)));
        Assert.Equal(1, Covering(periods, new DateOnly(2026, 11, 11)));
        Assert.Equal(1, Covering(periods, new DateOnly(2026, 11, 25)));
        Assert.Equal(1, Covering(periods, new DateOnly(2026, 11, 26)));
        Assert.Equal(1, Covering(periods, new DateOnly(2026, 10, 26)));
    }

    [Fact]
    public void Clamped_short_months_tile_end_to_end()
    {
        var endOn31 = new[] { Rule(1, 1, 25), Rule(2, 26, 31) };
        var endOn30 = new[] { Rule(1, 31, 10), Rule(2, 11, 30) };
        Assert.True(PayrollCutoffCalendar.TryValidate(endOn31, out _));
        Assert.True(PayrollCutoffCalendar.TryValidate(endOn30, out _));

        AssertMonthTiled(2026, 2, endOn31);
        AssertMonthTiled(2028, 2, endOn31);
        AssertMonthTiled(2026, 4, endOn31);
        AssertMonthTiled(2026, 2, endOn30);
        AssertMonthTiled(2028, 2, endOn30);
        AssertMonthTiled(2026, 4, endOn30);
    }

    private static int Covering(IReadOnlyList<PayrollCutoffPeriod> periods, DateOnly day) =>
        periods.Count(period => day >= period.From && day <= period.To);

    private static void AssertMonthTiled(int year, int month, IReadOnlyList<PayrollCutoffRule> rules)
    {
        var periods = PayrollCutoffCalendar.PeriodsForMonth(year, month, rules);
        var last = DateTime.DaysInMonth(year, month);
        var hits = new int[last + 1];
        foreach (var period in periods)
        {
            for (var day = period.From; day <= period.To; day = day.AddDays(1))
            {
                if (day.Year == year && day.Month == month)
                {
                    hits[day.Day]++;
                }
            }
        }

        for (var day = 1; day <= last; day++)
        {
            Assert.Equal(1, hits[day]);
        }
    }
}
