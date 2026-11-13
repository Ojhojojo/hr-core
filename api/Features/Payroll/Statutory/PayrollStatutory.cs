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

        var baseAmount = StatutoryBase(row);
        var shares = await MonthlyEmployeeSharesAsync(monthStart, baseAmount, cancellationToken);
        if (shares is null)
        {
            return Suggest(
                row,
                monthStart,
                baseAmount,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                [
                    features.StatutoryRates
                        ? "No statutory rate row covers this month. Enter the amounts by hand."
                        : "The statutory pack covers January 2024 on. Enter the amounts by hand."
                ],
                periodFrom,
                periodTo,
                null);
        }

        var (sssMsc, sss, philHealthBase, philHealth, pagIbigBase, pagIbig, whtTaxable, wht) = shares.Value;

        if (periodFrom is null || periodTo is null)
        {
            return Suggest(
                row,
                monthStart,
                baseAmount,
                sssMsc,
                sss,
                philHealthBase,
                philHealth,
                pagIbigBase,
                pagIbig,
                whtTaxable,
                wht,
                StatutoryPack.VerificationNotes,
                null,
                null,
                null);
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
        var scheduled = row.BranchId is Guid homeBranch
            ? await ApplyDeductionScheduleAsync(
                homeBranch,
                periodFrom.Value,
                periodTo.Value,
                sss,
                philHealth,
                pagIbig,
                wht,
                periodBase,
                prorated,
                cancellationToken)
            : null;
        if (scheduled is not null)
        {
            return Suggest(
                row,
                monthStart,
                baseAmount,
                sssMsc,
                scheduled.Value.Sss,
                philHealthBase,
                scheduled.Value.PhilHealth,
                pagIbigBase,
                scheduled.Value.PagIbig,
                scheduled.Value.Taxable,
                scheduled.Value.Wht,
                scheduled.Value.Notes,
                periodFrom,
                periodTo,
                prorated.Fraction);
        }

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
            sssMsc,
            prorated.Sss,
            philHealthBase,
            prorated.PhilHealth,
            pagIbigBase,
            prorated.PagIbig,
            prorated.PeriodTaxable,
            prorated.Wht,
            notes,
            periodFrom,
            periodTo,
            prorated.Fraction);
    }

    private async Task<(
        decimal SssMsc,
        decimal Sss,
        decimal PhilHealthBase,
        decimal PhilHealth,
        decimal PagIbigBase,
        decimal PagIbig,
        decimal WhtTaxable,
        decimal Wht)?> MonthlyEmployeeSharesAsync(
        DateOnly monthStart,
        decimal baseAmount,
        CancellationToken cancellationToken)
    {
        if (features.StatutoryRates)
        {
            var tables = await LoadStatutoryTablesAsync(monthStart, cancellationToken);
            if (tables is null || !tables.CoversSuggestions)
            {
                return null;
            }

            var sss = StatutoryMath.SssEmployeeShare(tables.Sss!, baseAmount);
            var philHealth = StatutoryMath.PhilHealthEmployeeShare(tables.PhilHealth!, baseAmount);
            var pagIbig = StatutoryMath.PagIbigEmployeeShare(tables.PagIbig!, baseAmount);
            var taxable = baseAmount - sss - philHealth - pagIbig;
            return (
                StatutoryMath.SssMsc(tables.Sss!, baseAmount),
                sss,
                StatutoryMath.PhilHealthBase(tables.PhilHealth!, baseAmount),
                philHealth,
                StatutoryMath.PagIbigBase(tables.PagIbig!, baseAmount),
                pagIbig,
                taxable,
                StatutoryMath.MonthlyWithholdingTax(tables.Wht!, taxable));
        }

        if (monthStart < StatutoryPack.EffectiveFrom)
        {
            return null;
        }

        var packSss = StatutoryPack.SssEmployeeShare(monthStart, baseAmount);
        var packPhilHealth = StatutoryPack.PhilHealthEmployeeShare(monthStart, baseAmount);
        var packPagIbig = StatutoryPack.PagIbigEmployeeShare(monthStart, baseAmount);
        var packTaxable = baseAmount - packSss - packPhilHealth - packPagIbig;
        return (
            StatutoryPack.SssMsc(monthStart, baseAmount),
            packSss,
            StatutoryPack.PhilHealthBase(baseAmount),
            packPhilHealth,
            StatutoryPack.PagIbigBase(baseAmount),
            packPagIbig,
            packTaxable,
            StatutoryPack.MonthlyWithholdingTax(packTaxable));
    }

    private static StatutorySuggestionDto Suggest(
        PayrollStaffRowDto row,
        DateOnly monthStart,
        decimal baseAmount,
        decimal sssMsc,
        decimal sss,
        decimal philHealthBase,
        decimal philHealth,
        decimal pagIbigBase,
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
            sssMsc,
            sss,
            philHealthBase,
            philHealth,
            pagIbigBase,
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
    internal static decimal StatutoryBase(PayrollStaffRowDto row)
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

    /// <summary>
    /// Null when every deduction still prorates, so the caller keeps the proration result unchanged.
    /// Adjusted amounts are returned only. Nothing here stores a cut-off number.
    /// </summary>
    private async Task<(decimal Sss, decimal PhilHealth, decimal PagIbig, decimal Taxable, decimal Wht, IReadOnlyList<string> Notes)?>
        ApplyDeductionScheduleAsync(
            Guid branchId,
            DateOnly from,
            DateOnly to,
            decimal monthlySss,
            decimal monthlyPhilHealth,
            decimal monthlyPagIbig,
            decimal monthlyWht,
            decimal periodBase,
            PayrollStatutoryProration.Result prorated,
            CancellationToken cancellationToken)
    {
        var schedule = new PayrollDeductionScheduleService(db, features);
        var (rules, matched, periodCount) = await schedule.ForPeriodAsync(branchId, from, to, cancellationToken);
        if (rules.Count == 0 || rules.All(rule => rule.Mode == PayrollStatutoryAllocation.Prorate))
        {
            return null;
        }

        decimal Share(string key, decimal monthly, decimal proratedShare)
        {
            var rule = rules.FirstOrDefault(row => row.DeductionKey == key);
            return PayrollStatutoryAllocation.Amount(
                rule?.Mode,
                monthly,
                proratedShare,
                rule?.CutoffOrdinal,
                matched,
                periodCount);
        }

        var sssOut = Share("sss", monthlySss, prorated.Sss);
        var philOut = Share("philhealth", monthlyPhilHealth, prorated.PhilHealth);
        var pagOut = Share("pagibig", monthlyPagIbig, prorated.PagIbig);
        var taxable = periodBase - sssOut - philOut - pagOut;
        if (taxable < 0m)
        {
            taxable = 0m;
        }

        var whtRule = rules.FirstOrDefault(row => row.DeductionKey == "wht");
        decimal whtOut;
        if (whtRule is null || whtRule.Mode == PayrollStatutoryAllocation.Prorate)
        {
            var uncapped = PayrollStatutoryAllocation.Round(monthlyWht * prorated.Fraction);
            whtOut = uncapped > taxable ? PayrollStatutoryAllocation.Round(taxable) : uncapped;
        }
        else
        {
            whtOut = PayrollStatutoryAllocation.Amount(
                whtRule.Mode, monthlyWht, prorated.Wht, whtRule.CutoffOrdinal, matched, periodCount);
        }

        var notes = new List<string>();
        foreach (var note in StatutoryPack.VerificationNotes)
        {
            if (note.Contains("Divide them for shorter pay periods", StringComparison.Ordinal))
            {
                continue;
            }

            notes.Add(note);
        }

        notes.Add(PayrollStatutoryProration.Note(from, to, prorated.Fraction));
        foreach (var rule in rules.Where(rule => rule.Mode != PayrollStatutoryAllocation.Prorate))
        {
            notes.Add(rule.Mode == PayrollStatutoryAllocation.Cutoff
                ? $"{rule.DeductionKey} is taken on cut-off {rule.CutoffOrdinal} only."
                : $"{rule.DeductionKey} is split across the month's cut-offs.");
        }

        return (sssOut, philOut, pagOut, taxable, whtOut, notes);
    }
}
