namespace HrCore.Domain;

/// <summary>
/// A shop or office. This part holds what the platform and shared HR need: identity, location and the
/// time clock, and payroll rules. The host app adds its own fields in another part of this class.
/// </summary>
public sealed partial class Branch
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string TimeZone { get; set; } = "Asia/Manila";
    public bool IsLive { get; set; } = true;
    public BranchKind Kind { get; set; } = BranchKind.Owned;
    public const int DefaultClockRadiusMeters = 150;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int ClockRadiusMeters { get; set; } = DefaultClockRadiusMeters;
    /// <summary>When on, clock-in and clock-out must include a photo of the shift. Seeded off.</summary>
    public const bool DefaultRequireClockPhoto = false;
    public bool RequireClockPhoto { get; set; } = DefaultRequireClockPhoto;

    /// <summary>When on, a self punch outside the shop radius (or without GPS) is saved as pending for owner/admin review. Seeded on.</summary>
    public const bool DefaultAllowOffsitePunchWithApproval = true;
    public bool AllowOffsitePunchWithApproval { get; set; } = DefaultAllowOffsitePunchWithApproval;

    /// <summary>
    /// Daily minimum wage the shop's worker rates are checked against. Null means the guard is off.
    /// Seeded at the NCR non-agriculture daily minimum — the owner verifies it against the current
    /// NCR wage order in Shop settings.
    /// </summary>
    public const decimal DefaultMinimumDailyWagePhp = 695m;
    public decimal? MinimumDailyWagePhp { get; set; } = DefaultMinimumDailyWagePhp;

    public const decimal DefaultPayrollRegularHolidayPercent = 200m;
    public const decimal DefaultPayrollSpecialDayPercent = 130m;
    public const decimal DefaultPayrollRestDayPercent = 130m;
    public const bool DefaultPayrollHolidayPremiumEnabled = true;
    public const bool DefaultPayrollRestDayPremiumEnabled = true;
    public const bool DefaultPayrollNightDiffEnabled = false;
    public const decimal DefaultPayrollNightDiffPercent = 10m;
    public static readonly TimeOnly DefaultPayrollNightStart = new(22, 0);
    public static readonly TimeOnly DefaultPayrollNightEnd = new(6, 0);

    /// <summary>Percent of the hourly rate paid for hours on a regular holiday, including the base 100. Seed 200.</summary>
    public decimal PayrollRegularHolidayPercent { get; set; } = DefaultPayrollRegularHolidayPercent;

    /// <summary>Percent of the hourly rate paid for hours on a special day, including the base 100. Seed 130.</summary>
    public decimal PayrollSpecialDayPercent { get; set; } = DefaultPayrollSpecialDayPercent;

    /// <summary>Percent of the hourly rate paid for hours on a rest day, including the base 100. Seed 130.</summary>
    public decimal PayrollRestDayPercent { get; set; } = DefaultPayrollRestDayPercent;

    public bool PayrollHolidayPremiumEnabled { get; set; } = DefaultPayrollHolidayPremiumEnabled;
    public bool PayrollRestDayPremiumEnabled { get; set; } = DefaultPayrollRestDayPremiumEnabled;

    /// <summary>Off by default so existing daytime registers stay identical until the owner turns it on.</summary>
    public bool PayrollNightDiffEnabled { get; set; } = DefaultPayrollNightDiffEnabled;

    public decimal PayrollNightDiffPercent { get; set; } = DefaultPayrollNightDiffPercent;
    public TimeOnly PayrollNightStart { get; set; } = new(22, 0);
    public TimeOnly PayrollNightEnd { get; set; } = new(6, 0);

    /// <summary>Manila time a shift ends. Time punched past this, after the grace, is overtime claimed.</summary>
    public static readonly TimeOnly DefaultShiftEnd = new(17, 0);
    public TimeOnly ShiftEnd { get; set; } = new(17, 0);

    /// <summary>Minutes past shift end that stay regular. Longer than this flags the full time past shift end.</summary>
    public const int DefaultOtGraceMinutes = 30;
    public const int MaxOtGraceMinutes = 240;
    public int OtGraceMinutes { get; set; } = DefaultOtGraceMinutes;

    /// <summary>When off, only the owner can approve overtime. Seeded off.</summary>
    public const bool DefaultAdminsCanApproveOt = false;
    public bool AdminsCanApproveOt { get; set; } = DefaultAdminsCanApproveOt;

    /// <summary>Regular overtime percent of the hourly equivalent, including the base 100. Seed 125.</summary>
    public const decimal DefaultOtPremiumPercent = 125m;
    public decimal OtPremiumPercent { get; set; } = DefaultOtPremiumPercent;

    /// <summary>
    /// Working days in a year used to turn a monthly salary into an hourly equivalent:
    /// monthly × 12 ÷ this ÷ 8. Seed 313.
    /// </summary>
    public const decimal DefaultOtMonthlyDayDivisor = 313m;
    public decimal OtMonthlyDayDivisor { get; set; } = DefaultOtMonthlyDayDivisor;

    public const DayOfWeek DefaultWeekStartDay = DayOfWeek.Monday;
    public DayOfWeek WeekStartDay { get; set; } = DefaultWeekStartDay;

    public DateTime CreatedAtUtc { get; set; }
}
