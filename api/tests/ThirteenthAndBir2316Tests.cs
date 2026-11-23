using HrCore.Features.Payroll;
using Xunit;

namespace HrCore.Tests;

public class ThirteenthAndBir2316Tests
{
    [Fact]
    public void Unpaid_leave_in_a_former_monthly_month_is_still_subtracted()
    {
        // Monthly Jan–Jun, Daily Jul–Dec; two unpaid days in March must still come off basic pay.
        var staffId = Guid.NewGuid();
        var slices = new List<RateSlice>
        {
            new(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), PayBasis.Monthly, 30_000m, null, null),
            new(new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31), PayBasis.Daily, 800m, null, null)
        };
        var leaves = new List<LeaveRequest>
        {
            new()
            {
                Id = Guid.NewGuid(),
                StaffId = staffId,
                BranchId = Guid.NewGuid(),
                LeaveType = LeaveType.Unpaid,
                Status = LeaveRequestStatus.Approved,
                StartDate = new DateOnly(2026, 3, 10),
                EndDate = new DateOnly(2026, 3, 11),
                CreatedAtUtc = DateTime.UtcNow
            }
        };

        var march = PayrollService.UnpaidLeaveForMonthSlice(
            staffId,
            slices,
            leaves,
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 31));

        Assert.NotNull(march);
        Assert.Equal(
            new HashSet<DateOnly> { new(2026, 3, 10), new(2026, 3, 11) },
            march);

        var july = PayrollService.UnpaidLeaveForMonthSlice(
            staffId,
            slices,
            leaves,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31));
        Assert.Null(july);
    }

    [Fact]
    public void Bir2316_missing_months_use_runs_that_include_the_employee()
    {
        // Shop ran every month; this person was left out of May → May is missing.
        var hire = new DateOnly(2026, 1, 1);
        var included = new List<(DateOnly From, DateOnly To)>
        {
            (new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
            (new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)),
            (new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)),
            (new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30)),
            // May omitted — person not on that run
            (new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)),
            (new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)),
            (new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)),
            (new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)),
            (new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)),
            (new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)),
            (new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31))
        };

        var missing = Bir2316Service.MissingMonths(included, 2026, hire, separationDate: null);

        Assert.Equal(new[] { 5 }, missing);
    }
}
