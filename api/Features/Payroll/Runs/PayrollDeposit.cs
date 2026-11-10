using HrCore.Features.HrChangeLog;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Payroll;

/// <summary>
/// Deposit recordkeeping. Discrepancy is net minus what was deposited.
/// Positive is short, negative is over. Null when no deposit has been entered.
/// </summary>
public static class PayrollDepositMath
{
    public const decimal MaxAmount = 999999999999.99m;

    public static decimal? Discrepancy(decimal net, decimal? depositedAmount) =>
        depositedAmount is decimal deposited ? net - deposited : null;

    /// <summary>
    /// Lines whose discrepancy is not zero, in the order given. Null when every line
    /// matches or has no deposit, so the run JSON stays unchanged.
    /// </summary>
    public static IReadOnlyList<PayrollDepositDiscrepancyDto>? NonZero(IEnumerable<PayrollRunLine> lines)
    {
        List<PayrollDepositDiscrepancyDto>? rows = null;
        foreach (var line in lines)
        {
            if (Discrepancy(line.Net, line.DepositedAmount) is not decimal difference || difference == 0m)
            {
                continue;
            }

            rows ??= [];
            rows.Add(new PayrollDepositDiscrepancyDto(
                line.StaffId,
                line.StaffName,
                line.Net,
                line.DepositedAmount!.Value,
                difference));
        }

        return rows;
    }
}

public sealed record PayrollDepositDto(
    Guid RunId,
    Guid StaffId,
    string StaffName,
    decimal Net,
    decimal DepositedAmount,
    decimal Discrepancy);

public sealed record PayrollDepositWriteResult(
    PayrollDepositDto? Saved,
    Dictionary<string, string[]>? Errors,
    string? Error,
    int Status)
{
    public static PayrollDepositWriteResult Ok(PayrollDepositDto saved) =>
        new(saved, null, null, StatusCodes.Status200OK);

    public static PayrollDepositWriteResult Bad(Dictionary<string, string[]> errors) =>
        new(null, errors, null, StatusCodes.Status400BadRequest);

    public static PayrollDepositWriteResult Missing(string error) =>
        new(null, null, error, StatusCodes.Status404NotFound);

    public static PayrollDepositWriteResult Conflict(string error) =>
        new(null, null, error, StatusCodes.Status409Conflict);
}

public sealed partial class PayrollService
{
    public const string DepositRecordedOnLockedRun = "Deposits are recorded on a locked run.";

    public const string DepositAmountMessage =
        "Amount must be zero or more, with at most 2 decimal places.";

    public async Task<PayrollDepositWriteResult> RecordDepositAsync(
        IReadOnlyList<Branch> shops,
        Guid runId,
        Guid staffId,
        decimal? depositedAmount,
        Guid actorStaffId,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeDeposit(depositedAmount, out var amount, out var errors))
        {
            return PayrollDepositWriteResult.Bad(errors!);
        }

        if (!features.Deposits)
        {
            return PayrollDepositWriteResult.Missing("That payroll run is not on this shop scope.");
        }

        var run = await db.PayrollRuns
            .FirstOrDefaultAsync(row => row.Id == runId, cancellationToken);
        if (run is null || !shops.Any(shop => shop.Id == run.BranchId))
        {
            return PayrollDepositWriteResult.Missing("That payroll run is not on this shop scope.");
        }

        if (run.Status != PayrollRunStatus.Locked)
        {
            return PayrollDepositWriteResult.Conflict(DepositRecordedOnLockedRun);
        }

        var line = await db.PayrollRunLines
            .FirstOrDefaultAsync(row => row.RunId == run.Id && row.StaffId == staffId, cancellationToken);
        if (line is null)
        {
            return PayrollDepositWriteResult.Missing("That person is not on this run.");
        }

        var previous = line.DepositedAmount;
        line.DepositedAmount = amount;
        new HrChangeLogService(db).RecordMoney(
            actorStaffId,
            line.StaffId,
            HrChangeField.PayrollDeposit,
            previous,
            amount,
            periodFrom: run.PeriodFrom,
            periodTo: run.PeriodTo,
            refType: "payroll_deposit",
            refId: line.Id);
        await db.SaveChangesAsync(cancellationToken);
        return PayrollDepositWriteResult.Ok(new PayrollDepositDto(
            run.Id,
            line.StaffId,
            line.StaffName,
            line.Net,
            amount,
            PayrollDepositMath.Discrepancy(line.Net, amount)!.Value));
    }

    internal static bool TryNormalizeDeposit(
        decimal? depositedAmount,
        out decimal amount,
        out Dictionary<string, string[]>? errors)
    {
        amount = 0m;
        if (depositedAmount is not decimal value
            || value < 0m
            || value > PayrollDepositMath.MaxAmount
            || value != PayrollStatutoryAllocation.Round(value))
        {
            errors = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["depositedAmount"] = [DepositAmountMessage]
            };
            return false;
        }

        amount = value;
        errors = null;
        return true;
    }
}
