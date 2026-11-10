namespace HrCore.Domain;

/// <summary>Frozen register row for one worker inside a locked payroll run.</summary>
public sealed class PayrollRunLine
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public PayrollRun Run { get; set; } = null!;
    public Guid StaffId { get; set; }
    public Staff Staff { get; set; } = null!;
    /// <summary>Snapshot of the name at lock time. Names change later.</summary>
    public string StaffName { get; set; } = "";
    public Guid? BranchId { get; set; }
    public PayBasis? Basis { get; set; }
    public decimal? RatePhp { get; set; }
    public decimal RegularHours { get; set; }
    public decimal OtHours { get; set; }

    /// <summary>Approved overtime pay included in <see cref="Gross"/>. Null on runs locked before overtime approval.</summary>
    public decimal? OtPay { get; set; }
    public decimal DaysPaid { get; set; }
    public decimal Gross { get; set; }
    public decimal Sss { get; set; }
    public decimal PhilHealth { get; set; }
    public decimal PagIbig { get; set; }
    public decimal Wht { get; set; }
    public decimal Other { get; set; }
    public decimal Deductions { get; set; }
    public decimal Net { get; set; }
    public string? Notes { get; set; }

    /// <summary>Hours on a holiday that received a premium. Null when the shop had nothing configured.</summary>
    public decimal? HolidayHours { get; set; }
    public decimal? RestDayHours { get; set; }
    public decimal? NightHours { get; set; }
    public decimal? PremiumPay { get; set; }

    /// <summary>Allowance total included in <see cref="Gross"/>. Null when the worker had none.</summary>
    public decimal? Allowances { get; set; }

    /// <summary>Loan and cash-advance instalments deducted on this line. Null when none applied.</summary>
    public decimal? LoanDeductions { get; set; }

    /// <summary>
    /// Employer SSS share in PHP, including the EC. Null when the shop had remittance off at lock.
    /// Mapped only when <see cref="HrCoreFeatures.EmployerRemittance"/> is on: nullable
    /// <c>numeric(14,2)</c> with no database default, so null stays null. Ignored when the flag is off.
    /// </summary>
    public decimal? EmployerSss { get; set; }

    /// <summary>
    /// Employer PhilHealth share in PHP. Null when the shop had remittance off at lock.
    /// Mapped only when <see cref="HrCoreFeatures.EmployerRemittance"/> is on.
    /// </summary>
    public decimal? EmployerPhilHealth { get; set; }

    /// <summary>
    /// Employer Pag-IBIG share in PHP. Null when the shop had remittance off at lock.
    /// Mapped only when <see cref="HrCoreFeatures.EmployerRemittance"/> is on.
    /// </summary>
    public decimal? EmployerPagIbig { get; set; }

    /// <summary>
    /// <c>held</c> or <c>released</c>. Null when this payout was not held.
    /// Mapped only when <see cref="HrCoreFeatures.PayoutHold"/> is on. No database default,
    /// so null stays null. Ignored when the flag is off.
    /// </summary>
    public string? PayoutStatus { get; set; }

    /// <summary>
    /// Reason copied from the staff hold at lock. Mapped only when the payout-hold flag is on.
    /// </summary>
    public string? HoldReason { get; set; }

    /// <summary>
    /// Manila date (<c>yyyy-MM-dd</c>) the held payout was released. Null while held.
    /// Mapped only when the payout-hold flag is on.
    /// </summary>
    public DateOnly? ReleasedOn { get; set; }

    /// <summary>
    /// Staff who released the hold. Mapped only when the payout-hold flag is on.
    /// </summary>
    public Guid? ReleasedByStaffId { get; set; }

    public Staff? ReleasedByStaff { get; set; }

    /// <summary>
    /// What was actually deposited for this locked line, in PHP. Null until someone records it.
    /// Mapped only when <see cref="HrCoreFeatures.Deposits"/> is on: nullable
    /// <c>numeric(14,2)</c> with no database default, so null stays null. Ignored when the flag is off.
    /// Recording it does not change <see cref="Gross"/>, <see cref="Deductions"/>, or <see cref="Net"/>.
    /// </summary>
    public decimal? DepositedAmount { get; set; }
}
