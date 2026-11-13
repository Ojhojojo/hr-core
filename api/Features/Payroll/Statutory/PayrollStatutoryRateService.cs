using System.Globalization;
using HrCore.Features.SettingsLog;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Payroll;

public sealed partial class PayrollService
{
    /// <summary>
    /// Latest covering row of each kind for <paramref name="month"/>. Null when the host flag is
    /// off, so callers keep using <see cref="StatutoryPack"/>.
    /// </summary>
    public async Task<StatutoryTables?> LoadStatutoryTablesAsync(
        DateOnly month,
        CancellationToken cancellationToken)
    {
        if (!features.StatutoryRates)
        {
            return null;
        }

        var rows = await db.StatutoryRates().AsNoTracking()
            .Include(row => row.Brackets)
            .Where(row => row.EffectiveFrom <= month)
            .ToListAsync(cancellationToken);

        StatutoryRate? Pick(string kind) => rows
            .Where(row => row.Kind == kind)
            .OrderByDescending(row => row.EffectiveFrom)
            .FirstOrDefault();

        return new StatutoryTables(
            ToSssTable(Pick(StatutoryRateKind.Sss)),
            ToPhilHealthTable(Pick(StatutoryRateKind.PhilHealth)),
            ToPagIbigTable(Pick(StatutoryRateKind.PagIbig)),
            ToWhtTable(Pick(StatutoryRateKind.Wht)));
    }

    public async Task<StatutoryRateListResponse> ListStatutoryRatesAsync(CancellationToken cancellationToken)
    {
        var rows = await db.StatutoryRates().AsNoTracking()
            .Include(row => row.Brackets)
            .OrderBy(row => row.Kind)
            .ThenBy(row => row.EffectiveFrom)
            .ToListAsync(cancellationToken);
        return new StatutoryRateListResponse(rows.Select(ToDto).ToList());
    }

    public async Task<StatutoryRateWriteResult> CreateStatutoryRateAsync(
        SaveStatutoryRateRequest request,
        Guid? actorStaffId,
        CancellationToken cancellationToken)
    {
        if (!TryNormalize(request, out var kind, out var from, out var brackets, out var errors))
        {
            return StatutoryRateWriteResult.Bad(errors!);
        }

        if (await KindDateTakenAsync(kind, from, null, cancellationToken))
        {
            return StatutoryRateWriteResult.Bad(OverlapError());
        }

        var row = NewRow(kind, from, request, brackets);
        db.StatutoryRates().Add(row);
        new SettingsChangeLogService(db).Record(
            actorStaffId,
            null,
            SettingsArea.PayrollStatutoryRate,
            kind,
            null,
            RateLog(row));
        await db.SaveChangesAsync(cancellationToken);
        return StatutoryRateWriteResult.Ok(ToDto(row));
    }

    public async Task<StatutoryRateWriteResult> UpdateStatutoryRateAsync(
        Guid id,
        SaveStatutoryRateRequest request,
        Guid? actorStaffId,
        CancellationToken cancellationToken)
    {
        if (!TryNormalize(request, out var kind, out var from, out var brackets, out var errors))
        {
            return StatutoryRateWriteResult.Bad(errors!);
        }

        var row = await db.StatutoryRates()
            .Include(item => item.Brackets)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return StatutoryRateWriteResult.NotFound();
        }

        if (await KindDateTakenAsync(kind, from, row.Id, cancellationToken))
        {
            return StatutoryRateWriteResult.Bad(OverlapError());
        }

        var before = RateLog(row);
        Apply(row, kind, from, request, brackets);
        new SettingsChangeLogService(db).Record(
            actorStaffId,
            null,
            SettingsArea.PayrollStatutoryRate,
            kind,
            before,
            RateLog(row));
        await db.SaveChangesAsync(cancellationToken);
        return StatutoryRateWriteResult.Ok(ToDto(row));
    }

    public async Task<StatutoryRateDeleteResult> DeleteStatutoryRateAsync(
        Guid id,
        Guid? actorStaffId,
        CancellationToken cancellationToken)
    {
        var row = await db.StatutoryRates()
            .Include(item => item.Brackets)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return StatutoryRateDeleteResult.NotFound();
        }

        var before = RateLog(row);
        db.StatutoryTaxBrackets().RemoveRange(row.Brackets);
        db.StatutoryRates().Remove(row);
        new SettingsChangeLogService(db).Record(
            actorStaffId,
            null,
            SettingsArea.PayrollStatutoryRate,
            row.Kind,
            before,
            null);
        await db.SaveChangesAsync(cancellationToken);
        return StatutoryRateDeleteResult.Ok();
    }

    private async Task<bool> KindDateTakenAsync(
        string kind,
        DateOnly from,
        Guid? exceptId,
        CancellationToken cancellationToken)
    {
        var query = db.StatutoryRates().AsNoTracking()
            .Where(row => row.Kind == kind && row.EffectiveFrom == from);
        if (exceptId is Guid id)
        {
            query = query.Where(row => row.Id != id);
        }

        return await query.AnyAsync(cancellationToken);
    }

    private static bool TryNormalize(
        SaveStatutoryRateRequest request,
        out string kind,
        out DateOnly from,
        out IReadOnlyList<SaveStatutoryTaxBracketRequest> brackets,
        out Dictionary<string, string[]>? errors)
    {
        var bag = new Dictionary<string, string[]>(StringComparer.Ordinal);
        kind = request.Kind?.Trim().ToLowerInvariant() ?? "";
        from = request.EffectiveFrom ?? default;
        brackets = request.Brackets ?? [];

        if (!StatutoryRateKind.IsKnown(kind))
        {
            bag["kind"] = ["Kind must be sss, philhealth, pagibig, or wht."];
        }

        if (request.EffectiveFrom is null)
        {
            bag["effectiveFrom"] = ["Effective from is required as yyyy-MM-dd."];
        }

        switch (kind)
        {
            case StatutoryRateKind.Sss:
                Require(bag, "mscFloor", request.MscFloor, positive: true);
                Require(bag, "mscCeiling", request.MscCeiling, positive: true);
                Require(bag, "mscStep", request.MscStep, positive: true);
                RequireRate(bag, "employeeRate", request.EmployeeRate);
                RequireRate(bag, "employerRate", request.EmployerRate);
                Require(bag, "ecLow", request.EcLow, positive: false);
                Require(bag, "ecHigh", request.EcHigh, positive: false);
                Require(bag, "ecMscCeilingForLow", request.EcMscCeilingForLow, positive: true);
                if (request.MscFloor is decimal floor
                    && request.MscCeiling is decimal ceiling
                    && floor > ceiling)
                {
                    bag["mscFloor"] = ["MSC floor must not be above the ceiling."];
                }

                break;
            case StatutoryRateKind.PhilHealth:
                Require(bag, "floor", request.Floor, positive: true);
                Require(bag, "ceiling", request.Ceiling, positive: true);
                RequireRate(bag, "employeeRate", request.EmployeeRate);
                RequireRate(bag, "employerRate", request.EmployerRate);
                if (request.Floor is decimal philFloor
                    && request.Ceiling is decimal philCeiling
                    && philFloor > philCeiling)
                {
                    bag["floor"] = ["Floor must not be above the ceiling."];
                }

                break;
            case StatutoryRateKind.PagIbig:
                Require(bag, "smallSalary", request.SmallSalary, positive: false);
                RequireRate(bag, "rateSmall", request.RateSmall);
                RequireRate(bag, "rate", request.Rate);
                Require(bag, "cap", request.Cap, positive: true);
                RequireRate(bag, "employerRate", request.EmployerRate);
                break;
            case StatutoryRateKind.Wht:
                Require(bag, "exemptMonthly", request.ExemptMonthly, positive: false);
                if (brackets.Count == 0)
                {
                    bag["brackets"] = ["Withholding needs at least one tax bracket."];
                }
                else
                {
                    ValidateBrackets(bag, brackets);
                }

                break;
        }

        if (bag.Count > 0)
        {
            errors = bag;
            return false;
        }

        errors = null;
        return true;
    }

    private static void ValidateBrackets(
        Dictionary<string, string[]> bag,
        IReadOnlyList<SaveStatutoryTaxBracketRequest> brackets)
    {
        decimal? previousUpTo = null;
        var seenUnbounded = false;
        for (var i = 0; i < brackets.Count; i++)
        {
            var bracket = brackets[i];
            RequireRate(bag, BracketField(i, "rate"), bracket.Rate);
            if (bracket.BaseTax < 0m)
            {
                bag[BracketField(i, "baseTax")] = ["Base tax must be zero or more."];
            }

            if (bracket.ExcessOver < 0m)
            {
                bag[BracketField(i, "excessOver")] = ["Excess over must be zero or more."];
            }

            if (bracket.UpTo is decimal upTo)
            {
                if (upTo <= 0m)
                {
                    bag[BracketField(i, "upTo")] = ["Up to must be greater than zero, or empty for the top band."];
                }

                if (seenUnbounded || (previousUpTo is decimal previous && upTo <= previous))
                {
                    bag[BracketField(i, "upTo")] = ["Tax brackets overlap. Each Up to must be greater than the one before it."];
                }

                previousUpTo = upTo;
            }
            else
            {
                if (i != brackets.Count - 1)
                {
                    bag[BracketField(i, "upTo")] = ["Only the last tax bracket may be unbounded."];
                }

                seenUnbounded = true;
            }
        }
    }

    private static void Require(
        Dictionary<string, string[]> bag,
        string field,
        decimal? value,
        bool positive)
    {
        if (value is null)
        {
            bag[field] = [$"{Label(field)} is required."];
            return;
        }

        if (positive && value <= 0m)
        {
            bag[field] = [$"{Label(field)} must be greater than zero."];
        }
        else if (!positive && value < 0m)
        {
            bag[field] = [$"{Label(field)} must be zero or more."];
        }
    }

    private static void RequireRate(Dictionary<string, string[]> bag, string field, decimal? value)
    {
        if (value is null)
        {
            bag[field] = [$"{Label(field)} is required."];
            return;
        }

        if (value is < 0m or > 1m)
        {
            bag[field] = [$"{Label(field)} must be between 0 and 1 (0.05 is 5%)."];
        }
    }

    private static string Label(string field) => field switch
    {
        "mscFloor" => "MSC floor",
        "mscCeiling" => "MSC ceiling",
        "mscStep" => "MSC step",
        "employeeRate" => "Employee rate",
        "employerRate" => "Employer rate",
        "ecLow" => "EC low",
        "ecHigh" => "EC high",
        "ecMscCeilingForLow" => "EC MSC ceiling",
        "floor" => "Floor",
        "ceiling" => "Ceiling",
        "smallSalary" => "Small salary",
        "rateSmall" => "Small rate",
        "rate" => "Rate",
        "cap" => "Cap",
        "exemptMonthly" => "Exempt monthly",
        _ => field
    };

    private static string BracketField(int index, string name) =>
        $"brackets[{index.ToString(CultureInfo.InvariantCulture)}].{name}";

    private static Dictionary<string, string[]> OverlapError() =>
        new() { ["effectiveFrom"] = ["A row for this kind and date already exists."] };

    private static StatutoryRate NewRow(
        string kind,
        DateOnly from,
        SaveStatutoryRateRequest request,
        IReadOnlyList<SaveStatutoryTaxBracketRequest> brackets)
    {
        var row = new StatutoryRate { Id = Guid.NewGuid() };
        Apply(row, kind, from, request, brackets);
        return row;
    }

    private static void Apply(
        StatutoryRate row,
        string kind,
        DateOnly from,
        SaveStatutoryRateRequest request,
        IReadOnlyList<SaveStatutoryTaxBracketRequest> brackets)
    {
        row.Kind = kind;
        row.EffectiveFrom = from;
        row.MscFloor = kind == StatutoryRateKind.Sss ? request.MscFloor : null;
        row.MscCeiling = kind == StatutoryRateKind.Sss ? request.MscCeiling : null;
        row.MscStep = kind == StatutoryRateKind.Sss ? request.MscStep : null;
        row.EmployeeRate = kind is StatutoryRateKind.Sss or StatutoryRateKind.PhilHealth
            ? request.EmployeeRate
            : null;
        row.EmployerRate = kind is StatutoryRateKind.Sss or StatutoryRateKind.PhilHealth or StatutoryRateKind.PagIbig
            ? request.EmployerRate
            : null;
        row.EcLow = kind == StatutoryRateKind.Sss ? request.EcLow : null;
        row.EcHigh = kind == StatutoryRateKind.Sss ? request.EcHigh : null;
        row.EcMscCeilingForLow = kind == StatutoryRateKind.Sss ? request.EcMscCeilingForLow : null;
        row.Floor = kind == StatutoryRateKind.PhilHealth ? request.Floor : null;
        row.Ceiling = kind == StatutoryRateKind.PhilHealth ? request.Ceiling : null;
        row.SmallSalary = kind == StatutoryRateKind.PagIbig ? request.SmallSalary : null;
        row.RateSmall = kind == StatutoryRateKind.PagIbig ? request.RateSmall : null;
        row.Rate = kind == StatutoryRateKind.PagIbig ? request.Rate : null;
        row.Cap = kind == StatutoryRateKind.PagIbig ? request.Cap : null;
        row.ExemptMonthly = kind == StatutoryRateKind.Wht ? request.ExemptMonthly : null;
        row.Brackets.Clear();
        if (kind != StatutoryRateKind.Wht)
        {
            return;
        }

        var order = 1;
        foreach (var bracket in brackets)
        {
            row.Brackets.Add(new StatutoryTaxBracket
            {
                Id = Guid.NewGuid(),
                StatutoryRateId = row.Id,
                SortOrder = bracket.SortOrder ?? order,
                UpTo = bracket.UpTo,
                BaseTax = bracket.BaseTax,
                Rate = bracket.Rate,
                ExcessOver = bracket.ExcessOver
            });
            order++;
        }
    }

    private static StatutoryRateDto ToDto(StatutoryRate row)
    {
        IReadOnlyList<StatutoryTaxBracketDto>? brackets = null;
        if (row.Kind == StatutoryRateKind.Wht)
        {
            brackets = row.Brackets
                .OrderBy(item => item.SortOrder)
                .Select(item => new StatutoryTaxBracketDto(
                    item.Id, item.SortOrder, item.UpTo, item.BaseTax, item.Rate, item.ExcessOver))
                .ToList();
        }

        return new StatutoryRateDto(
            row.Id,
            row.Kind,
            row.EffectiveFrom,
            row.MscFloor,
            row.MscCeiling,
            row.MscStep,
            row.EmployeeRate,
            row.EmployerRate,
            row.EcLow,
            row.EcHigh,
            row.EcMscCeilingForLow,
            row.Floor,
            row.Ceiling,
            row.SmallSalary,
            row.RateSmall,
            row.Rate,
            row.Cap,
            row.ExemptMonthly,
            brackets);
    }

    private static string RateLog(StatutoryRate row) =>
        row.Kind
        + " "
        + row.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static SssRateTable? ToSssTable(StatutoryRate? row)
    {
        if (row is null
            || row.MscFloor is not decimal floor
            || row.MscCeiling is not decimal ceiling
            || row.MscStep is not decimal step
            || row.EmployeeRate is not decimal employee
            || row.EmployerRate is not decimal employer
            || row.EcLow is not decimal ecLow
            || row.EcHigh is not decimal ecHigh
            || row.EcMscCeilingForLow is not decimal ecCeiling)
        {
            return null;
        }

        return new SssRateTable(floor, ceiling, step, employee, employer, ecLow, ecHigh, ecCeiling);
    }

    private static PhilHealthRateTable? ToPhilHealthTable(StatutoryRate? row)
    {
        if (row is null
            || row.Floor is not decimal floor
            || row.Ceiling is not decimal ceiling
            || row.EmployeeRate is not decimal employee
            || row.EmployerRate is not decimal employer)
        {
            return null;
        }

        return new PhilHealthRateTable(floor, ceiling, employee, employer);
    }

    private static PagIbigRateTable? ToPagIbigTable(StatutoryRate? row)
    {
        if (row is null
            || row.SmallSalary is not decimal small
            || row.RateSmall is not decimal rateSmall
            || row.Rate is not decimal rate
            || row.Cap is not decimal cap
            || row.EmployerRate is not decimal employer)
        {
            return null;
        }

        return new PagIbigRateTable(small, rateSmall, rate, cap, employer);
    }

    private static WhtRateTable? ToWhtTable(StatutoryRate? row)
    {
        if (row is null || row.ExemptMonthly is not decimal exempt || row.Brackets.Count == 0)
        {
            return null;
        }

        var bands = row.Brackets
            .OrderBy(item => item.SortOrder)
            .Select(item => new WhtRateBracket(item.UpTo, item.BaseTax, item.Rate, item.ExcessOver))
            .ToList();
        return new WhtRateTable(exempt, bands);
    }
}
