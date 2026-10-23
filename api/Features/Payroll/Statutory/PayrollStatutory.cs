namespace HrCore.Features.Payroll;

/// <summary>Suggested statutory amounts for one worker and one calendar month, from the pack.</summary>
public sealed partial class PayrollService
{
    public async Task<StatutorySuggestionDto?> GetStatutorySuggestionAsync(
        IReadOnlyList<Branch> shops,
        Guid staffId,
        int year,
        int month,
        DateOnly? periodFrom,
        DateOnly? periodTo,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
        {
            return null;
        }

        // The monthly base is that month's register gross: salary for monthly, closed punches
        // for time-rated workers, unpaid leave subtracted the usual way. A period suggestion
        // runs the pack on that same month (the month containing `from`), then takes the
        // period's share of those amounts.
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var register = await GetRegisterAsync(
            shops, monthStart, monthEnd, true, includeUnscopedOwners, cancellationToken);
        var row = register.Rows.FirstOrDefault(r => r.StaffId == staffId);
        if (row is null)
        {
            return null;
        }

        if (monthStart < StatutoryPack.EffectiveFrom)
        {
            return Suggest(
                row, monthStart, StatutoryBase(row), 0m, 0m, 0m, 0m, 0m,
                ["The statutory pack covers January 2024 on. Enter the amounts by hand."],
                periodFrom, periodTo, null);
        }

        var baseAmount = StatutoryBase(row);
        var sss = StatutoryPack.SssEmployeeShare(monthStart, baseAmount);
        var philHealth = StatutoryPack.PhilHealthEmployeeShare(monthStart, baseAmount);
        var pagIbig = StatutoryPack.PagIbigEmployeeShare(baseAmount);
        var whtTaxable = baseAmount - sss - philHealth - pagIbig;
        var wht = StatutoryPack.MonthlyWithholdingTax(whtTaxable);

        if (periodFrom is null || periodTo is null)
        {
            return Suggest(
                row, monthStart, baseAmount, sss, philHealth, pagIbig, whtTaxable, wht,
                StatutoryPack.VerificationNotes, null, null, null);
        }

        decimal periodBase;
        if (periodFrom == monthStart && periodTo == monthEnd)
        {
            periodBase = baseAmount;
        }
        else
        {
            var periodRegister = await GetRegisterAsync(
                shops, periodFrom, periodTo, true, includeUnscopedOwners, cancellationToken);
            var periodRow = periodRegister.Rows.FirstOrDefault(r => r.StaffId == staffId);
            periodBase = periodRow is null ? 0m : StatutoryBase(periodRow);
        }

        var prorated = PayrollStatutoryProration.Apportion(
            new PayrollStatutoryProration.MonthShares(sss, philHealth, pagIbig, wht),
            baseAmount,
            periodBase);
        var notes = new List<string>();
        foreach (var note in StatutoryPack.VerificationNotes)
        {
            if (note.Contains("Divide them for shorter pay periods", StringComparison.Ordinal))
            {
                continue;
            }

            notes.Add(note);
        }

        notes.Add(PayrollStatutoryProration.Note(periodFrom.Value, periodTo.Value, prorated.Fraction));
        return Suggest(
            row,
            monthStart,
            baseAmount,
            prorated.Sss,
            prorated.PhilHealth,
            prorated.PagIbig,
            prorated.PeriodTaxable,
            prorated.Wht,
            notes,
            periodFrom,
            periodTo,
            prorated.Fraction);
    }

    private static StatutorySuggestionDto Suggest(
        PayrollStaffRowDto row,
        DateOnly monthStart,
        decimal baseAmount,
        decimal sss,
        decimal philHealth,
        decimal pagIbig,
        decimal whtTaxable,
        decimal wht,
        IReadOnlyList<string> notes,
        DateOnly? periodFrom,
        DateOnly? periodTo,
        decimal? fraction) =>
        new(
            row.StaffId,
            row.StaffName,
            monthStart.ToString("yyyy-MM"),
            baseAmount,
            StatutoryPack.SssMsc(baseAmount),
            sss,
            StatutoryPack.PhilHealthBase(baseAmount),
            philHealth,
            StatutoryPack.PagIbigBase(baseAmount),
            pagIbig,
            whtTaxable,
            wht,
            RoundMoney(sss + philHealth + pagIbig + wht),
            notes,
            periodFrom,
            periodTo,
            fraction);

    /// <summary>
    /// WHT base is register gross with non-taxable allowance items taken out.
    /// Taxable allowance items stay in. An allowances override replaces the allowance
    /// total, so that replacement stays in the base and the pre-override items are not
    /// subtracted again. No allowance items leaves the gross unchanged.
    /// </summary>
    private static decimal StatutoryBase(PayrollStaffRowDto row)
    {
        if (row.AllowancesOverridden || row.AllowanceItems is null)
        {
            return row.Amount;
        }

        decimal nonTaxable = 0m;
        foreach (var item in row.AllowanceItems)
        {
            if (!item.Taxable)
            {
                nonTaxable += item.Amount;
            }
        }

        return row.Amount - nonTaxable;
    }
}
