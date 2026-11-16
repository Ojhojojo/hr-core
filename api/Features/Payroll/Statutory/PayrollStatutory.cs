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
        var (sss, philHealth, pagIbig, whtTaxable, wht) = MonthShares(monthStart, baseAmount);

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

    private static (decimal Sss, decimal PhilHealth, decimal PagIbig, decimal WhtTaxable, decimal Wht)
        MonthShares(DateOnly monthStart, decimal baseAmount)
    {
        var sss = StatutoryPack.SssEmployeeShare(monthStart, baseAmount);
        var philHealth = StatutoryPack.PhilHealthEmployeeShare(monthStart, baseAmount);
        var pagIbig = StatutoryPack.PagIbigEmployeeShare(monthStart, baseAmount);
        var whtTaxable = baseAmount - sss - philHealth - pagIbig;
        var wht = StatutoryPack.MonthlyWithholdingTax(whtTaxable);
        return (sss, philHealth, pagIbig, whtTaxable, wht);
    }

    /// <summary>
    /// What an open register shows for a person with no saved deduction row. <c>Applied</c> is
    /// false when the pack cannot suggest (a month before January 2024); the amounts are then zero
    /// and <c>Note</c> says to enter them by hand.
    /// </summary>
    private sealed record PreviewSuggestion(
        decimal Sss,
        decimal PhilHealth,
        decimal PagIbig,
        decimal Wht,
        bool Applied,
        string? Note);

    /// <summary>
    /// Suggestions for the register's period, one per person in <paramref name="staffIds"/>.
    /// Same maths as <see cref="GetStatutorySuggestionAsync"/> for that person, month (the one
    /// containing the period start) and period: the pack on the month's gross, then the period's
    /// share of it. Two gross-only registers serve everyone, not one per person.
    /// </summary>
    private async Task<Dictionary<Guid, PreviewSuggestion>> LoadPreviewSuggestionsAsync(
        IReadOnlyList<Branch> shops,
        HashSet<Guid> staffIds,
        DateOnly start,
        DateOnly end,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, PreviewSuggestion>();
        if (staffIds.Count == 0)
        {
            return result;
        }

        var monthStart = new DateOnly(start.Year, start.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        if (monthStart < StatutoryPack.EffectiveFrom)
        {
            const string note = "The statutory pack covers January 2024 on. Enter the amounts by hand.";
            foreach (var id in staffIds)
            {
                result[id] = new PreviewSuggestion(0m, 0m, 0m, 0m, false, note);
            }

            return result;
        }

        var monthRegister = await BuildRegisterAsync(
            shops, monthStart, monthEnd, true, includeUnscopedOwners, null, false, cancellationToken);
        var monthRows = monthRegister.Rows.ToDictionary(r => r.StaffId);
        Dictionary<Guid, PayrollStaffRowDto> periodRows;
        if (start == monthStart && end == monthEnd)
        {
            periodRows = monthRows;
        }
        else
        {
            var periodRegister = await BuildRegisterAsync(
                shops, start, end, true, includeUnscopedOwners, null, false, cancellationToken);
            periodRows = periodRegister.Rows.ToDictionary(r => r.StaffId);
        }

        foreach (var id in staffIds)
        {
            if (!monthRows.TryGetValue(id, out var monthRow))
            {
                continue;
            }

            var baseAmount = StatutoryBase(monthRow);
            var month = MonthShares(monthStart, baseAmount);
            var periodBase = periodRows.TryGetValue(id, out var periodRow) ? StatutoryBase(periodRow) : 0m;
            var prorated = PayrollStatutoryProration.Apportion(
                new PayrollStatutoryProration.MonthShares(month.Sss, month.PhilHealth, month.PagIbig, month.Wht),
                baseAmount,
                periodBase);
            result[id] = new PreviewSuggestion(
                prorated.Sss, prorated.PhilHealth, prorated.PagIbig, prorated.Wht, true, null);
        }

        return result;
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
            StatutoryPack.SssMsc(monthStart, baseAmount),
            sss,
            StatutoryPack.PhilHealthBase(baseAmount),
            philHealth,
            StatutoryPack.PagIbigBase(monthStart, baseAmount),
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
