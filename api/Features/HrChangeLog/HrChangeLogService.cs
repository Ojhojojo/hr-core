using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.HrChangeLog;

/// <summary>Dotted field keys for <see cref="Domain.HrChangeLog"/>. One key per changed value.</summary>
public static class HrChangeField
{
    public const string StaffRole = "staff.role";
    public const string StaffActive = "staff.active";
    public const string StaffPayBasis = "staff.pay_basis";
    public const string StaffRate = "staff.rate";
    public const string StaffOtAfterHours = "staff.ot_after_hours";
    public const string StaffOtMultiplier = "staff.ot_multiplier";
    public const string StaffHireDate = "staff.hire_date";
    public const string StaffPosition = "staff.position";
    public const string StaffDepartment = "staff.department";
    public const string StaffEmployeeNo = "staff.employee_no";
    public const string StaffEmergencyContact = "staff.emergency_contact";
    public const string StaffTin = "staff.tin";
    public const string StaffSss = "staff.sss";
    public const string StaffPhilHealth = "staff.philhealth";
    public const string StaffPagIbig = "staff.pagibig";
    public const string StaffRegisteredAddress = "staff.registered_address";
    public const string StaffZipCode = "staff.zip_code";
    public const string StaffBirthDate = "staff.birth_date";
    public const string StaffSeparationDate = "staff.separation_date";
    public const string StaffSeparationReason = "staff.separation_reason";
    public const string PayslipIssued = "payslip.issued";
    public const string StaffMustClock = "staff.must_clock";
    public const string LeaveAllotment = "leave.allotment";
    public const string LeaveDecision = "leave.decision";
    public const string LeaveCancelReason = "leave.cancel_reason";
    public const string LeaveRequest = "leave.request";
    public const string PayrollDeductionSss = "payroll.deduction.sss";
    public const string PayrollDeductionPhilHealth = "payroll.deduction.philhealth";
    public const string PayrollDeductionPagIbig = "payroll.deduction.pagibig";
    public const string PayrollDeductionWht = "payroll.deduction.wht";
    public const string PayrollDeductionOther = "payroll.deduction.other";
    public const string PayrollThirteenthOverride = "payroll.thirteenth.override";
    public const string PayrollRunLock = "payroll.run.lock";
    public const string PayrollRunVoid = "payroll.run.void";
    public const string PayrollOverride = "payroll.override";
    public const string PayrollDayOverride = "payroll.day_override";
    public const string PayrollAllowance = "payroll.allowance";
    public const string PayrollLoan = "payroll.loan";
    public const string StaffRestDays = "staff.rest_days";
    public const string OvertimeRequest = "overtime.request";
    public const string OvertimeDecision = "overtime.decision";
    public const string AttendancePunchFor = "attendance.punch_for";
    public const string AttendancePunchClose = "attendance.punch_close";
    public const string DocumentUpload = "document.upload";
}

public sealed record HrChangeLogRow(
    Guid Id,
    Guid? ActorStaffId,
    string? ActorName,
    Guid SubjectStaffId,
    string SubjectName,
    DateTime AtUtc,
    string Field,
    string? OldValue,
    string? NewValue,
    DateOnly? PeriodFrom,
    DateOnly? PeriodTo,
    int? Year,
    string? RefType,
    Guid? RefId);

/// <summary>
/// Writes and reads the append-only people/HR trail. Recording is a no-op when nothing changed,
/// so callers can record unconditionally after an edit.
/// </summary>
public sealed class HrChangeLogService(IHrDb db)
{
    public const string CannotChangeMessage = "HR log rows cannot be changed or deleted.";
    public const int MaxTake = 500;

    public void Record(
        Guid? actorStaffId,
        Guid subjectStaffId,
        string field,
        string? oldValue,
        string? newValue,
        DateOnly? periodFrom = null,
        DateOnly? periodTo = null,
        int? year = null,
        string? refType = null,
        Guid? refId = null)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        db.HrChangeLogs.Add(new Domain.HrChangeLog
        {
            Id = Guid.NewGuid(),
            ActorStaffId = actorStaffId,
            SubjectStaffId = subjectStaffId,
            AtUtc = DateTime.UtcNow,
            Field = field,
            OldValue = oldValue,
            NewValue = newValue,
            PeriodFrom = periodFrom,
            PeriodTo = periodTo,
            Year = year,
            RefType = refType,
            RefId = refId
        });
    }

    public void RecordMoney(
        Guid? actorStaffId,
        Guid subjectStaffId,
        string field,
        decimal? oldValue,
        decimal? newValue,
        DateOnly? periodFrom = null,
        DateOnly? periodTo = null,
        int? year = null,
        string? refType = null,
        Guid? refId = null)
    {
        if (oldValue is decimal previous && newValue is decimal next && previous == next)
        {
            return;
        }

        Record(
            actorStaffId,
            subjectStaffId,
            field,
            Format(oldValue),
            Format(newValue),
            periodFrom,
            periodTo,
            year,
            refType,
            refId);
    }

    public static string? Format(decimal? value) =>
        value is null ? null : value.Value.ToString("0.00", CultureInfo.InvariantCulture);

    public Task<IReadOnlyList<HrChangeLogRow>> ListAsync(
        IReadOnlyCollection<Guid> subjectStaffIds,
        string? field,
        DateOnly? from,
        DateOnly? to,
        int take,
        CancellationToken cancellationToken)
    {
        var query = db.HrChangeLogs.Where(row => subjectStaffIds.Contains(row.SubjectStaffId));

        if (!string.IsNullOrWhiteSpace(field))
        {
            query = query.Where(row => row.Field == field);
        }

        if (from is DateOnly fromDay)
        {
            var fromUtc = ManilaTime.StartOfDayUtc(fromDay);
            query = query.Where(row => row.AtUtc >= fromUtc);
        }

        if (to is DateOnly toDay)
        {
            var toUtc = ManilaTime.StartOfDayUtc(toDay.AddDays(1));
            query = query.Where(row => row.AtUtc < toUtc);
        }

        return ListAsync(query, take, cancellationToken);
    }

    private static async Task<IReadOnlyList<HrChangeLogRow>> ListAsync(
        IQueryable<Domain.HrChangeLog> query,
        int take,
        CancellationToken cancellationToken)
    {
        var capped = Math.Clamp(take, 1, MaxTake);
        var rows = await query
            .AsNoTracking()
            .Include(row => row.ActorStaff)
            .ThenInclude(staff => staff!.Profile)
            .Include(row => row.SubjectStaff)
            .ThenInclude(staff => staff.Profile)
            .OrderByDescending(row => row.AtUtc)
            .ThenByDescending(row => row.Id)
            .Take(capped)
            .ToListAsync(cancellationToken);

        return rows.Select(row => new HrChangeLogRow(
            row.Id,
            row.ActorStaffId,
            row.ActorStaff?.Profile.DisplayName,
            row.SubjectStaffId,
            row.SubjectStaff.Profile.DisplayName,
            row.AtUtc,
            row.Field,
            row.OldValue,
            row.NewValue,
            row.PeriodFrom,
            row.PeriodTo,
            row.Year,
            row.RefType,
            row.RefId)).ToList();
    }
}
