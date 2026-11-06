using HrCore.Features.Payroll;

namespace HrCore.Tests;

public sealed class PayrollStatutoryAllocationTests
{
    [Fact]
    public void Prorate_keeps_the_existing_share()
    {
        Assert.Equal(787.50m, PayrollStatutoryAllocation.Amount("prorate", 1575m, 787.50m, null, 1, 2));
        Assert.Equal(500m, PayrollStatutoryAllocation.LoanInstalment("prorate", null, null, 500m));
    }

    [Fact]
    public void Cutoff_only_pays_the_full_monthly_share_on_that_period()
    {
        Assert.Equal(0m, PayrollStatutoryAllocation.Amount("cutoff", 1575m, 787.50m, 2, 1, 2));
        Assert.Equal(1575m, PayrollStatutoryAllocation.Amount("cutoff", 1575m, 787.50m, 2, 2, 2));
    }

    [Fact]
    public void Split_gives_the_remainder_to_the_last_period()
    {
        Assert.Equal(50.01m, PayrollStatutoryAllocation.Amount("split", 100.01m, 40m, null, 1, 2));
        Assert.Equal(50.00m, PayrollStatutoryAllocation.Amount("split", 100.01m, 40m, null, 2, 2));
        Assert.Equal(3.33m, PayrollStatutoryAllocation.SplitShare(10m, 3, 1));
        Assert.Equal(3.34m, PayrollStatutoryAllocation.SplitShare(10m, 3, 3));
    }

    [Fact]
    public void A_range_that_is_not_a_cut_off_keeps_the_prorated_share()
    {
        Assert.Equal(40m, PayrollStatutoryAllocation.Amount("cutoff", 1575m, 40m, 2, null, 2));
        Assert.Equal(0m, PayrollStatutoryAllocation.LoanInstalment("cutoff", 1, null, 500m));
    }

    [Fact]
    public void February_and_a_30_day_month_still_place_the_share_on_the_matching_period()
    {
        var rules = new[]
        {
            new PayrollCutoffRule(1, 1, 25),
            new PayrollCutoffRule(2, 26, 31)
        };

        AssertPlaced(2026, 2, rules);
        AssertPlaced(2028, 2, rules);
        AssertPlaced(2026, 4, rules);
    }

    [Fact]
    public void Loan_cutoff_skips_the_other_period_and_split_keeps_the_per_period_amount()
    {
        Assert.Equal(0m, PayrollStatutoryAllocation.LoanInstalment("cutoff", 2, 1, 500m));
        Assert.Equal(500m, PayrollStatutoryAllocation.LoanInstalment("cutoff", 2, 2, 500m));
        Assert.Equal(500m, PayrollStatutoryAllocation.LoanInstalment("split", null, 1, 500m));
        Assert.Equal(0m, PayrollStatutoryAllocation.LoanInstalment("split", null, null, 500m));
    }

    private static void AssertPlaced(int year, int month, IReadOnlyList<PayrollCutoffRule> rules)
    {
        var periods = PayrollCutoffCalendar.PeriodsForMonth(year, month, rules);
        Assert.Equal(2, periods.Count);
        var first = PayrollStatutoryAllocation.Amount("cutoff", 200m, 80m, 2, periods[0].Ordinal, periods.Count);
        var second = PayrollStatutoryAllocation.Amount("cutoff", 200m, 80m, 2, periods[1].Ordinal, periods.Count);
        Assert.Equal(0m, first);
        Assert.Equal(200m, second);
        Assert.Equal(100m, PayrollStatutoryAllocation.SplitShare(200m, periods.Count, periods[0].Ordinal));
        Assert.Equal(100m, PayrollStatutoryAllocation.SplitShare(200m, periods.Count, periods[1].Ordinal));
    }
}
