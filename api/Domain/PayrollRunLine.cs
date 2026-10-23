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
}
