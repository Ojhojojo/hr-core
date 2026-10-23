namespace HrCore.Features.Payroll;

/// <summary>Regular overtime only. Rest-day, holiday, and night premiums stay on their own path.</summary>
public static class OvertimeMath
{
    public readonly record struct Split(decimal RegularHours, decimal ClaimedHours);

    public readonly record struct DayPayInput(
        decimal PunchedOt,
        decimal ApprovedHours,
        PayBasis? Basis,
        decimal? Rate);

    /// <summary>
    /// Hours of a closed punch. Time past shift end is claimed only when it is longer than the
    /// grace. The grace is not subtracted: 08:00–19:00 with a 17:00 end is 2.00 claimed and 9.00 regular.
    /// </summary>
    public static Split SplitPunch(DateTime inUtc, DateTime outUtc, TimeOnly shiftEnd, int graceMinutes)
    {
        if (outUtc <= inUtc)
        {
            return new Split(0m, 0m);
        }

        var total = RoundHours((decimal)(outUtc - inUtc).TotalHours);
        var inLocal = ManilaTime.ToManila(inUtc);
        var outLocal = ManilaTime.ToManila(outUtc);
        var shiftEndLocal = DateOnly.FromDateTime(inLocal).ToDateTime(shiftEnd);
        if (outLocal <= shiftEndLocal)
        {
            return new Split(total, 0m);
        }

        var grace = TimeSpan.FromMinutes(Math.Clamp(graceMinutes, 0, Branch.MaxOtGraceMinutes));
        if (outLocal <= shiftEndLocal.Add(grace))
        {
            return new Split(total, 0m);
        }

        var otStart = inLocal > shiftEndLocal ? inLocal : shiftEndLocal;
        var claimed = RoundHours((decimal)(outLocal - otStart).TotalHours);
        if (claimed > total)
        {
            claimed = total;
        }

        var regular = total - claimed;
        if (regular < 0m)
        {
            regular = 0m;
        }

        return new Split(regular, claimed);
    }

    /// <summary>
    /// Hourly rate equivalent. Hourly is the rate. Daily is the rate ÷ 8.
    /// Monthly is monthly × 12 ÷ <paramref name="monthlyDayDivisor"/> ÷ 8.
    /// </summary>
    public static decimal HourlyEquivalent(PayBasis? basis, decimal? rate, decimal monthlyDayDivisor)
    {
        if (rate is not decimal value || basis is not PayBasis pay || value < 0m)
        {
            return 0m;
        }

        return pay switch
        {
            PayBasis.Hourly => value,
            PayBasis.Daily => value / 8m,
            PayBasis.Monthly when monthlyDayDivisor > 0m => value * 12m / monthlyDayDivisor / 8m,
            _ => 0m
        };
    }

    /// <summary>Approved hours pay only up to the hours actually punched past shift end.</summary>
    public static decimal PayableHours(decimal approvedHours, decimal punchedOtHours)
    {
        if (approvedHours <= 0m || punchedOtHours <= 0m)
        {
            return 0m;
        }

        return approvedHours < punchedOtHours ? approvedHours : punchedOtHours;
    }

    public static decimal Pay(decimal hours, decimal hourlyEquivalent, decimal premiumPercent) =>
        Math.Round(
            hours * hourlyEquivalent * premiumPercent / 100m,
            2,
            MidpointRounding.AwayFromZero);

    public static (decimal Hours, decimal Pay) SumDays(
        IEnumerable<DayPayInput> days,
        decimal monthlyDayDivisor,
        decimal premiumPercent)
    {
        decimal hours = 0m;
        decimal pay = 0m;
        foreach (var day in days)
        {
            var payable = PayableHours(day.ApprovedHours, day.PunchedOt);
            if (payable <= 0m)
            {
                continue;
            }

            hours += payable;
            pay += Pay(payable, HourlyEquivalent(day.Basis, day.Rate, monthlyDayDivisor), premiumPercent);
        }

        return (hours, pay);
    }

    public static decimal RoundHours(decimal hours) =>
        hours <= 0m ? 0m : Math.Round(hours, 2, MidpointRounding.AwayFromZero);
}
