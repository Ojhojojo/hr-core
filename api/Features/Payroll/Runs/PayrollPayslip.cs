using HrCore.Features.HrChangeLog;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Payroll;

/// <summary>
/// Payslips: one worker, one period. Values come from the register compute, so a locked period
/// serves its frozen snapshot. Issuance is recorded on the append-only HR trail.
/// </summary>
public sealed partial class PayrollService
{
    public async Task<PayslipResponse?> GetPayslipAsync(
        IReadOnlyList<Branch> shops,
        Guid staffId,
        DateOnly from,
        DateOnly to,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        var staff = await db.Staff.AsNoTracking()
            .Include(s => s.Profile)
            .Include(s => s.Branch)
            .FirstOrDefaultAsync(s => s.Id == staffId, cancellationToken);
        if (staff is null || !IsInRegisterScope(staff, shops, includeUnscopedOwners))
        {
            return null;
        }

        // Price against the worker's own shop so an exact locked run serves its snapshot.
        var payslipShops = staff.Branch is { } branch ? [branch] : shops;
        var register = await GetRegisterAsync(
            payslipShops, from, to, true, includeUnscopedOwners, cancellationToken);
        var row = register.Rows.FirstOrDefault(r => r.StaffId == staff.Id);
        if (row is null)
        {
            return null;
        }

        var issuedAtUtc = await FindIssuedAtUtcAsync(staff.Id, from, to, cancellationToken);
        return ToPayslip(register, row, issuedAtUtc, null);
    }

    /// <summary>The caller's own payslip. Unscoped owners have no home shop, so no own payslip.</summary>
    public async Task<PayslipResponse?> GetMyPayslipAsync(
        Domain.Staff actor,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        if (actor.BranchId is not Guid branchId)
        {
            return null;
        }

        var (start, end, _, _) = ResolvePeriod(from, to);
        var branch = await db.Branches.AsNoTracking()
            .FirstAsync(b => b.Id == branchId, cancellationToken);
        var register = await GetRegisterAsync([branch], start, end, true, false, cancellationToken);
        var row = register.Rows.FirstOrDefault(r => r.StaffId == actor.Id);
        if (row is null)
        {
            return null;
        }

        var issuedAtUtc = await FindIssuedAtUtcAsync(actor.Id, start, end, cancellationToken);
        return ToPayslip(register, row, issuedAtUtc, null);
    }

    public async Task<(PayslipResponse? Payslip, string? Error)> IssuePayslipAsync(
        IReadOnlyList<Branch> shops,
        IssuePayslipRequest request,
        Guid? actorStaffId,
        bool includeUnscopedOwners,
        CancellationToken cancellationToken)
    {
        if (request.From > request.To)
        {
            return (null, "The period end must not be before the start.");
        }

        if (request.Note is { Length: > 200 })
        {
            return (null, "Note must be 200 characters or fewer.");
        }

        var payslip = await GetPayslipAsync(
            shops, request.StaffId, request.From, request.To, includeUnscopedOwners, cancellationToken);
        if (payslip is null)
        {
            return (null, null);
        }

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var issuedAtUtc = DateTime.UtcNow;
        db.HrChangeLogs.Add(new Domain.HrChangeLog
        {
            Id = Guid.NewGuid(),
            ActorStaffId = actorStaffId,
            SubjectStaffId = request.StaffId,
            AtUtc = issuedAtUtc,
            Field = HrChangeField.PayslipIssued,
            NewValue = note ?? "issued",
            PeriodFrom = request.From,
            PeriodTo = request.To,
            RefType = "payslip"
        });
        await db.SaveChangesAsync(cancellationToken);

        return (payslip with { IssuedAtUtc = issuedAtUtc, Note = note }, null);
    }

    private async Task<DateTime?> FindIssuedAtUtcAsync(
        Guid staffId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken) =>
        await db.HrChangeLogs.AsNoTracking()
            .Where(row => row.Field == HrChangeField.PayslipIssued
                && row.SubjectStaffId == staffId
                && row.PeriodFrom == from
                && row.PeriodTo == to)
            .OrderByDescending(row => row.AtUtc)
            .Select(row => (DateTime?)row.AtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    private static PayslipResponse ToPayslip(
        PayrollRegisterResponse register,
        PayrollStaffRowDto row,
        DateTime? issuedAtUtc,
        string? note) =>
        new(
            row.StaffId,
            row.StaffName,
            register.From,
            register.To,
            row.Basis,
            row.RatePhp,
            row.RegularHours,
            row.OtHours,
            row.DaysPaid,
            row.Amount,
            row.Sss,
            row.PhilHealth,
            row.PagIbig,
            row.Wht,
            row.Other,
            row.Deductions,
            row.Net,
            register.Source,
            register.LockedByName,
            register.LockedAtUtc,
            issuedAtUtc,
            note,
            row.HolidayHours,
            row.RestDayHours,
            row.NightHours,
            row.PremiumPay,
            row.Allowances,
            row.AllowanceItems is null
                ? null
                : row.AllowanceItems
                    .Select(item => new PayslipAllowanceItemDto(item.Name, item.Amount))
                    .ToList(),
            row.LoanDeductions,
            row.LoanItems is null
                ? null
                : row.LoanItems
                    .Select(item => new PayslipLoanItemDto(item.Kind, item.Amount, item.BalanceAfter))
                    .ToList(),
            row.Warnings is { Count: > 0 } ? row.Warnings : null,
            row.OtPay);
}
