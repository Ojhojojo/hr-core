using System.Text.Json.Serialization;

namespace HrCore.Features.Payroll;

public sealed record PayrollStaffRowDto(
    Guid StaffId,
    string StaffName,
    StaffRole Role,
    Guid? BranchId,
    string? BranchName,
    BranchKind? BranchKind,
    PayBasis? Basis,
    decimal? RatePhp,
    decimal? OtAfterHoursPerDay,
    decimal? OtMultiplier,
    decimal RegularHours,
    decimal OtHours,
    decimal DaysPaid,
    decimal Amount,
    string? Notes,
    bool Active,
    decimal Sss,
    decimal PhilHealth,
    decimal PagIbig,
    decimal Wht,
    decimal Other,
    decimal Deductions,
    decimal Net,
    IReadOnlyList<PayrollRateSliceDto>? RateSlices = null,
    decimal? HolidayHours = null,
    decimal? RestDayHours = null,
    decimal? NightHours = null,
    decimal? PremiumPay = null,
    bool PremiumOverridden = false,
    IReadOnlyList<string>? Warnings = null,
    decimal? Allowances = null,
    IReadOnlyList<PayrollAllowanceItemDto>? AllowanceItems = null,
    bool AllowancesOverridden = false,
    decimal? LoanDeductions = null,
    IReadOnlyList<PayrollLoanItemDto>? LoanItems = null,
    decimal OtPay = 0,
    decimal LeaveDaysPaid = 0m,
    IReadOnlyList<PayrollDayPayOverrideDto>? DayPayOverrides = null);

/// <summary>#390: one calendar day's pay override shown on the draft register.</summary>
public sealed record PayrollDayPayOverrideDto(
    Guid Id,
    Guid StaffId,
    DateOnly WorkDate,
    PayrollDayPayMode Mode,
    decimal? CustomAmount,
    string Reason,
    Guid? SetByStaffId,
    DateTime SetAtUtc);

public sealed class SavePayrollDayPayOverrideRequest
{
    public Guid StaffId { get; set; }
    public DateOnly WorkDate { get; set; }
    /// <summary>Omit or null to clear the override for that day.</summary>
    public PayrollDayPayMode? Mode { get; set; }
    public decimal? CustomAmount { get; set; }
    public string? Reason { get; set; }
}

public sealed record PayrollDayPayOverrideClearedDto(
    Guid StaffId,
    DateOnly WorkDate,
    bool Cleared);

public sealed record PayrollDayPayOverrideSaveResult(
    PayrollDayPayOverrideDto? Saved,
    PayrollDayPayOverrideClearedDto? Cleared,
    string? Error,
    int ErrorStatus)
{
    public static PayrollDayPayOverrideSaveResult NotFound() =>
        new(null, null, null, StatusCodes.Status404NotFound);

    public static PayrollDayPayOverrideSaveResult Bad(string error) =>
        new(null, null, error, StatusCodes.Status400BadRequest);

    public static PayrollDayPayOverrideSaveResult Locked(string error) =>
        new(null, null, error, StatusCodes.Status409Conflict);

    public static PayrollDayPayOverrideSaveResult Forbidden(string error) =>
        new(null, null, error, StatusCodes.Status403Forbidden);

    public static PayrollDayPayOverrideSaveResult Ok(PayrollDayPayOverrideDto saved) =>
        new(saved, null, null, StatusCodes.Status200OK);

    public static PayrollDayPayOverrideSaveResult Removed(PayrollDayPayOverrideClearedDto cleared) =>
        new(null, cleared, null, StatusCodes.Status200OK);
}

/// <summary>
/// One dated rate span inside a register period. Only sent when the period is priced at more
/// than one rate, for example after a mid-period raise.
/// </summary>
public sealed record PayrollRateSliceDto(
    DateOnly From,
    DateOnly To,
    PayBasis Basis,
    decimal RatePhp,
    decimal? OtAfterHoursPerDay,
    decimal? OtMultiplier);

/// <summary>
/// Pay-rate span used by the gross math. Covers one contiguous run of days at one rate.
/// RatePhp and Basis are null only for the "no history" snapshot of a worker with no rate.
/// </summary>
public sealed record RateSlice(
    DateOnly From,
    DateOnly To,
    PayBasis? Basis,
    decimal? RatePhp,
    decimal? OtAfterHoursPerDay,
    decimal? OtMultiplier);

/// <summary>
/// Register payload. <c>Source</c> is <c>live</c> for a computed period and <c>locked</c> when the
/// rows come from a locked run snapshot (<c>RunId</c>, <c>LockedAtUtc</c>, <c>LockedByName</c> set).
/// </summary>
public sealed record PayrollRegisterResponse(
    DateOnly From,
    DateOnly To,
    decimal TotalAmount,
    IReadOnlyList<PayrollStaffRowDto> Rows,
    decimal TotalNet,
    string Source = "live",
    Guid? RunId = null,
    DateTime? LockedAtUtc = null,
    string? LockedByName = null,
    int PendingOvertimeClaimsCount = 0,
    IReadOnlyList<UnpaidBonusDto>? UnpaidBonuses = null);

/// <summary>A sales bonus still Scheduled whose pay period has ended. No locked run has paid it.</summary>
public sealed record UnpaidBonusDto(
    Guid BonusId,
    Guid StaffId,
    string StaffName,
    decimal Amount,
    string Reason,
    DateOnly PayPeriodFrom,
    DateOnly PayPeriodTo);

public sealed record SavePayrollDeductionsRequest(
    Guid StaffId,
    DateOnly From,
    DateOnly To,
    decimal Sss,
    decimal PhilHealth,
    decimal PagIbig,
    decimal Wht,
    decimal Other);

public sealed record PayrollDeductionDto(
    Guid StaffId,
    DateOnly From,
    DateOnly To,
    decimal Sss,
    decimal PhilHealth,
    decimal PagIbig,
    decimal Wht,
    decimal Other,
    decimal Deductions);

public sealed record PayrollCsvFile(string FileName, byte[] Bytes);

public sealed record ThirteenthMonthRowDto(
    Guid StaffId,
    string StaffName,
    StaffRole Role,
    string? Branch,
    DateOnly? HireDate,
    PayBasis? PayBasis,
    decimal? RatePhp,
    decimal BasicEarnedAuto,
    decimal? BasicEarnedOverride,
    decimal BasicEarned,
    decimal ThirteenthMonth,
    bool UsedOverride);

public sealed record ThirteenthMonthResponse(
    int Year,
    IReadOnlyList<ThirteenthMonthRowDto> Rows,
    decimal TotalThirteenthMonth);

public sealed record SaveThirteenthOverrideRequest(
    Guid StaffId,
    int Year,
    decimal BasicEarnedOverride);

/// <summary>Body for locking one shop's payroll period.</summary>
public sealed record LockPayrollRunRequest(
    Guid BranchId,
    DateOnly From,
    DateOnly To,
    string? Note = null);

/// <summary>Row of the locked/voided payroll run list.</summary>
public sealed record PayrollRunSummaryDto(
    Guid Id,
    Guid BranchId,
    string? BranchName,
    DateOnly From,
    DateOnly To,
    PayrollRunStatus Status,
    string? Note,
    decimal TotalGross,
    decimal TotalDeductions,
    decimal TotalNet,
    int LineCount,
    DateTime CreatedAtUtc,
    string? CreatedByName,
    DateTime? LockedAtUtc,
    string? LockedByName,
    DateTime? VoidedAtUtc,
    string? VoidedByName);

/// <summary>One frozen register row inside a locked run.</summary>
public sealed record PayrollRunLineDto(
    Guid StaffId,
    string StaffName,
    Guid? BranchId,
    PayBasis? Basis,
    decimal? RatePhp,
    decimal RegularHours,
    decimal OtHours,
    decimal DaysPaid,
    decimal Amount,
    decimal Sss,
    decimal PhilHealth,
    decimal PagIbig,
    decimal Wht,
    decimal Other,
    decimal Deductions,
    decimal Net,
    string? Notes,
    decimal? HolidayHours = null,
    decimal? RestDayHours = null,
    decimal? NightHours = null,
    decimal? PremiumPay = null,
    decimal? Allowances = null,
    IReadOnlyList<PayrollAllowanceItemDto>? AllowanceItems = null,
    decimal? LoanDeductions = null,
    IReadOnlyList<PayrollLoanItemDto>? LoanItems = null,
    decimal? OtPay = null);

public sealed record PayrollRunDetailDto(
    PayrollRunSummaryDto Run,
    IReadOnlyList<PayrollRunLineDto> Lines);

/// <summary>One row of the caller's own payslip history: locked runs only, newest period first.</summary>
public sealed record MyPayslipSummaryDto(
    Guid RunId,
    Guid LineId,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    decimal Gross,
    decimal Deductions,
    decimal Net,
    DateTime LockedAtUtc);

/// <summary>Outcome of a self payslip lookup. Status 403 is another worker's line; 404 is missing or not locked.</summary>
public sealed record MyPayslipLookup(MyPayslipDetailDto? Saved, int Status)
{
    public static MyPayslipLookup Forbidden() => new(null, StatusCodes.Status403Forbidden);

    public static MyPayslipLookup NotFound() => new(null, StatusCodes.Status404NotFound);

    public static MyPayslipLookup Ok(MyPayslipDetailDto saved) => new(saved, StatusCodes.Status200OK);
}

/// <summary>
/// One saved payslip line exactly as stored at lock time (not recalculated), with its run period
/// and its frozen line items. Owner and admin locked it via the runs endpoint.
/// </summary>
public sealed record MyPayslipDetailDto(
    Guid RunId,
    Guid LineId,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    DateTime LockedAtUtc,
    string? LockedByName,
    Guid StaffId,
    string StaffName,
    Guid? BranchId,
    PayBasis? Basis,
    decimal? RatePhp,
    decimal RegularHours,
    decimal OtHours,
    decimal? OtPay,
    decimal DaysPaid,
    decimal Gross,
    decimal Sss,
    decimal PhilHealth,
    decimal PagIbig,
    decimal Wht,
    decimal Other,
    decimal Deductions,
    decimal Net,
    string? Notes,
    decimal? HolidayHours = null,
    decimal? RestDayHours = null,
    decimal? NightHours = null,
    decimal? PremiumPay = null,
    decimal? Allowances = null,
    decimal? LoanDeductions = null,
    IReadOnlyList<PayrollRunLineItemDto>? Items = null);

/// <summary>One frozen piece of a saved payslip line. Kind "allowance" is an allowance slice; loans reuse the row.</summary>
public sealed record PayrollRunLineItemDto(
    Guid Id,
    string Kind,
    string Label,
    decimal Amount,
    bool? Taxable,
    Guid? RefId,
    decimal? Quantity,
    decimal? BalanceAfter);

/// <summary>Append-only rate history for one worker plus the current Staff snapshot.</summary>
public sealed record PayrollRatesResponse(
    Guid StaffId,
    string StaffName,
    Guid? BranchId,
    string? BranchName,
    PayBasis? Basis,
    decimal? RatePhp,
    decimal? OtAfterHoursPerDay,
    decimal? OtMultiplier,
    IReadOnlyList<PayrollRateHistoryRowDto> History);

public sealed record PayrollRateHistoryRowDto(
    Guid Id,
    DateOnly EffectiveFrom,
    PayBasis Basis,
    decimal RatePhp,
    decimal? OtAfterHoursPerDay,
    decimal? OtMultiplier,
    DateTime CreatedAtUtc,
    string? CreatedByName,
    string? Reason);

/// <summary>Body for a dated rate change. Appends one history row and refreshes the snapshot.</summary>
public sealed record SetPayrollRateRequest(
    DateOnly EffectiveFrom,
    PayBasis Basis,
    decimal RatePhp,
    decimal? OtAfterHoursPerDay = null,
    decimal? OtMultiplier = null,
    string? Reason = null);

/// <summary>
/// Outcome of a deduction save. <c>Error</c> set means the period is locked (409);
/// a null <c>Saved</c> without an error means the person is outside the shop scope (404).
/// </summary>
public sealed record PayrollDeductionSaveResult(
    PayrollDeductionDto? Saved,
    string? Error)
{
    public static PayrollDeductionSaveResult NotFound() => new(null, null);

    public static PayrollDeductionSaveResult Locked(string error) => new(null, error);

    public static PayrollDeductionSaveResult Ok(PayrollDeductionDto saved) => new(saved, null);
}

/// <summary>One worker's payslip for one period. Values match the register, or its locked snapshot.</summary>
public sealed record PayslipResponse(
    Guid StaffId,
    string StaffName,
    DateOnly From,
    DateOnly To,
    PayBasis? Basis,
    decimal? RatePhp,
    decimal RegularHours,
    decimal OtHours,
    decimal DaysPaid,
    decimal Gross,
    decimal Sss,
    decimal PhilHealth,
    decimal PagIbig,
    decimal Wht,
    decimal Other,
    decimal Deductions,
    decimal Net,
    string Source,
    string? LockedByName = null,
    DateTime? LockedAtUtc = null,
    DateTime? IssuedAtUtc = null,
    string? Note = null,
    decimal? HolidayHours = null,
    decimal? RestDayHours = null,
    decimal? NightHours = null,
    decimal? PremiumPay = null,
    decimal? Allowances = null,
    IReadOnlyList<PayslipAllowanceItemDto>? AllowanceItems = null,
    decimal? LoanDeductions = null,
    IReadOnlyList<PayslipLoanItemDto>? LoanItems = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? Warnings = null,
    decimal OtPay = 0);

/// <summary>One allowance line on a register row. Per is null on a locked snapshot.</summary>
public sealed record PayrollAllowanceItemDto(
    Guid? AllowanceId,
    string Name,
    AllowancePer? Per,
    bool Taxable,
    decimal Quantity,
    decimal Amount);

/// <summary>Allowance line on a payslip: name and amount only.</summary>
public sealed record PayslipAllowanceItemDto(string Name, decimal Amount);

/// <summary>Body for issuing one worker's payslip. Issuance is recorded on the HR trail.</summary>
public sealed record IssuePayslipRequest(
    Guid StaffId,
    DateOnly From,
    DateOnly To,
    string? Note = null);

/// <summary>
/// Suggested statutory amounts for one worker and one calendar month, from the statutory pack.
/// Suggestions only — the owner verifies before saving them as deductions.
/// </summary>
public sealed record StatutorySuggestionDto(
    Guid StaffId,
    string StaffName,
    string Month,
    decimal Base,
    decimal SssMsc,
    decimal Sss,
    decimal PhilHealthBase,
    decimal PhilHealth,
    decimal PagIbigBase,
    decimal PagIbig,
    decimal WhtTaxable,
    decimal Wht,
    decimal StatutoryTotal,
    IReadOnlyList<string> Notes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateOnly? PeriodFrom = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateOnly? PeriodTo = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? Fraction = null);

/// <summary>Final pay estimate for a separated worker. Estimate only — not a payout.</summary>
public sealed record PayrollFinalPayDto(
    Guid StaffId,
    string StaffName,
    DateOnly SeparationDate,
    string? SeparationReason,
    PayrollFinalPayWages Wages,
    PayrollFinalPayThirteenth ThirteenthMonth,
    PayrollFinalPaySil Sil,
    decimal GrossTotal,
    IReadOnlyList<string> Notes);

/// <summary>Unpaid wages window: the day after the last locked period to the separation date.</summary>
public sealed record PayrollFinalPayWages(DateOnly From, DateOnly To, decimal Gross);

/// <summary>Pro-rated 13th month for the separation year: basic earned divided by 12.</summary>
public sealed record PayrollFinalPayThirteenth(int Year, decimal BasicEarned, decimal Amount);

/// <summary>Cash-convertible service incentive leave at the daily rate equivalent.</summary>
public sealed record PayrollFinalPaySil(
    bool Eligible,
    decimal Allotted,
    decimal Used,
    decimal Pending,
    decimal Remaining,
    decimal DailyRate,
    decimal Amount);

public sealed record PayrollPremiumSettingsDto(
    Guid BranchId,
    decimal RegularHolidayPercent,
    decimal SpecialDayPercent,
    decimal RestDayPercent,
    bool HolidayPremiumEnabled,
    bool RestDayPremiumEnabled,
    bool NightDiffEnabled,
    decimal NightDiffPercent,
    TimeOnly NightStart,
    TimeOnly NightEnd,
    TimeOnly ShiftEnd,
    int OtGraceMinutes,
    bool AdminsCanApproveOt,
    decimal OtPremiumPercent,
    decimal OtMonthlyDayDivisor);

public sealed class UpdatePayrollPremiumSettingsRequest
{
    public decimal RegularHolidayPercent { get; set; }
    public decimal SpecialDayPercent { get; set; }
    public decimal RestDayPercent { get; set; }
    public bool HolidayPremiumEnabled { get; set; }
    public bool RestDayPremiumEnabled { get; set; }
    public bool NightDiffEnabled { get; set; }
    public decimal NightDiffPercent { get; set; }
    public TimeOnly NightStart { get; set; }
    public TimeOnly NightEnd { get; set; }
    public TimeOnly? ShiftEnd { get; set; }
    public int? OtGraceMinutes { get; set; }
    public bool? AdminsCanApproveOt { get; set; }
    public decimal? OtPremiumPercent { get; set; }
    public decimal? OtMonthlyDayDivisor { get; set; }
}

public sealed record ShopHolidayDto(
    Guid Id,
    Guid? BranchId,
    DateOnly Date,
    string Name,
    HolidayKind Kind);

public sealed record ShopHolidayListResponse(
    int Year,
    IReadOnlyList<ShopHolidayDto> Holidays);

public sealed class UpsertShopHolidayRequest
{
    public Guid? BranchId { get; set; }
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
    public HolidayKind Kind { get; set; }
}

public sealed class CopyHolidayYearRequest
{
    public int FromYear { get; set; }
    public int ToYear { get; set; }
}

public sealed record CopyHolidayYearResponse(int Created, int Skipped);

public sealed class SavePayrollOverrideRequest
{
    public Guid StaffId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public string Key { get; set; } = "";
    public decimal? Amount { get; set; }
    public string? Reason { get; set; }
}

public sealed record PayrollOverrideDto(
    Guid Id,
    Guid StaffId,
    string? StaffName,
    DateOnly From,
    DateOnly To,
    string Key,
    decimal Amount,
    string Reason,
    Guid? SetByStaffId,
    DateTime SetAtUtc);

public sealed record PayrollOverrideClearedDto(
    Guid StaffId,
    DateOnly From,
    DateOnly To,
    string Key,
    bool Cleared);

/// <summary>
/// Outcome of a premium-override save. ErrorStatus 409 is a locked period, 400 is validation,
/// and a null Saved without an error is not-found.
/// </summary>
public sealed record PayrollOverrideSaveResult(
    PayrollOverrideDto? Saved,
    PayrollOverrideClearedDto? Cleared,
    string? Error,
    int ErrorStatus)
{
    public static PayrollOverrideSaveResult NotFound() => new(null, null, null, StatusCodes.Status404NotFound);

    public static PayrollOverrideSaveResult Bad(string error) =>
        new(null, null, error, StatusCodes.Status400BadRequest);

    public static PayrollOverrideSaveResult Locked(string error) =>
        new(null, null, error, StatusCodes.Status409Conflict);

    public static PayrollOverrideSaveResult Ok(PayrollOverrideDto saved) =>
        new(saved, null, null, StatusCodes.Status200OK);

    public static PayrollOverrideSaveResult Removed(PayrollOverrideClearedDto cleared) =>
        new(null, cleared, null, StatusCodes.Status200OK);
}

public sealed record StaffAllowanceDto(
    Guid Id,
    Guid StaffId,
    string Name,
    decimal Amount,
    AllowancePer Per,
    bool Taxable,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    string? Note,
    bool Active,
    DateTime CreatedAtUtc);

public sealed record StaffAllowanceListResponse(
    IReadOnlyList<StaffAllowanceDto> Allowances,
    IReadOnlyList<string> SuggestedNames);

public sealed record SaveStaffAllowanceRequest(
    Guid StaffId,
    string? Name,
    decimal Amount,
    AllowancePer Per,
    bool? Taxable = null,
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null,
    string? Note = null,
    bool? Active = null);

public sealed record UpdateStaffAllowanceRequest(
    string? Name,
    decimal Amount,
    AllowancePer Per,
    bool? Taxable = null,
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null,
    string? Note = null,
    bool? Active = null);

public sealed record AllowanceWriteResult(StaffAllowanceDto? Saved, string? Error, int Status)
{
    public static AllowanceWriteResult NotFound() => new(null, null, StatusCodes.Status404NotFound);

    public static AllowanceWriteResult Bad(string error) => new(null, error, StatusCodes.Status400BadRequest);

    public static AllowanceWriteResult Ok(StaffAllowanceDto saved) => new(saved, null, StatusCodes.Status200OK);
}

/// <summary>One loan instalment on a register row. Amount is what this run takes. Suggested is before the net cap.</summary>
public sealed record PayrollLoanItemDto(
    Guid LoanId,
    string Kind,
    decimal Suggested,
    decimal Amount,
    decimal BalanceBefore,
    decimal BalanceAfter,
    bool Capped);

/// <summary>Payslip loan line. LoanBalance is the remaining balance after this instalment ("Loan balance").</summary>
public sealed record PayslipLoanItemDto(string Label, decimal Instalment, decimal LoanBalance);

public sealed record StaffLoanDto(
    Guid Id,
    Guid StaffId,
    string Kind,
    decimal Principal,
    decimal PerPeriodAmount,
    DateOnly StartPeriod,
    string? Note,
    DateTime CreatedAtUtc,
    DateTime? CancelledAtUtc,
    string? CancelReason,
    decimal Paid,
    decimal Balance,
    StaffLoanStatus Status);

public sealed record StaffLoanPaymentDto(
    Guid Id,
    Guid LoanId,
    Guid RunId,
    Guid StaffId,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    decimal Amount,
    DateTime CreatedAtUtc,
    DateTime? ReversedAtUtc);

public sealed record StaffLoanDetailDto(
    Guid Id,
    Guid StaffId,
    string Kind,
    decimal Principal,
    decimal PerPeriodAmount,
    DateOnly StartPeriod,
    string? Note,
    DateTime CreatedAtUtc,
    DateTime? CancelledAtUtc,
    string? CancelReason,
    decimal Paid,
    decimal Balance,
    StaffLoanStatus Status,
    IReadOnlyList<StaffLoanPaymentDto> Payments);

public sealed record StaffLoanListResponse(
    IReadOnlyList<StaffLoanDto> Loans,
    IReadOnlyList<string> SuggestedKinds);

public sealed record SaveStaffLoanRequest(
    Guid StaffId,
    string? Kind,
    decimal Principal,
    decimal PerPeriodAmount,
    DateOnly StartPeriod,
    string? Note = null);

public sealed record UpdateStaffLoanRequest(
    string? Kind,
    decimal PerPeriodAmount,
    DateOnly StartPeriod,
    decimal? Principal = null,
    string? Note = null);

public sealed record CancelStaffLoanRequest(string? Reason);

public sealed record LoanWriteResult(StaffLoanDto? Saved, string? Error, int Status)
{
    public static LoanWriteResult NotFound() => new(null, null, StatusCodes.Status404NotFound);

    public static LoanWriteResult Bad(string error) => new(null, error, StatusCodes.Status400BadRequest);

    public static LoanWriteResult Conflict(string error) => new(null, error, StatusCodes.Status409Conflict);

    public static LoanWriteResult Ok(StaffLoanDto saved) => new(saved, null, StatusCodes.Status200OK);
}

public sealed record LoanDeleteResult(bool Deleted, string? Error, int Status)
{
    public static LoanDeleteResult NotFound() => new(false, null, StatusCodes.Status404NotFound);

    public static LoanDeleteResult Conflict(string error) => new(false, error, StatusCodes.Status409Conflict);

    public static LoanDeleteResult Ok() => new(true, null, StatusCodes.Status204NoContent);
}
