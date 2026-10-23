namespace HrCore.Domain;

/// <summary>A person who works for the business. The host app may add fields in another part of this class.</summary>
public sealed partial class Staff
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;
    // Null = unscoped owner (all shops). Required for every other role.
    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }
    public StaffRole Role { get; set; }
    public bool Active { get; set; } = true;
    public PayBasis? PayBasis { get; set; }
    public decimal? RatePhp { get; set; }
    public decimal? OtAfterHoursPerDay { get; set; }
    public decimal? OtMultiplier { get; set; }
    /// <summary>Default comes from <c>HrRoles.ClocksByDefault</c>. Owner cannot enable.</summary>
    public bool MustClock { get; set; }
    public DateOnly? HireDate { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    /// <summary>Display title only. Does not grant access. Role does.</summary>
    public string? Position { get; set; }
    public string? Department { get; set; }
    public string? EmployeeNo { get; set; }
    /// <summary>TIN. Plain text. No remote check.</summary>
    public string? Tin { get; set; }
    /// <summary>SSS number. Plain text. No remote check.</summary>
    public string? Sss { get; set; }
    /// <summary>PhilHealth number. Plain text. No remote check.</summary>
    public string? PhilHealth { get; set; }
    /// <summary>Pag-IBIG number. Plain text. No remote check.</summary>
    public string? PagIbig { get; set; }
    /// <summary>Registered address for tax forms (BIR 2316). Plain text, HR-maintained.</summary>
    public string? RegisteredAddress { get; set; }
    /// <summary>ZIP code of the registered address. Plain text, HR-maintained.</summary>
    public string? ZipCode { get; set; }
    /// <summary>Date of birth. HR-maintained.</summary>
    public DateOnly? BirthDate { get; set; }
    /// <summary>When the worker left. Null while employed. Set by the owner with a reason.</summary>
    public DateOnly? SeparationDate { get; set; }
    /// <summary>Why the worker left, for example resignation, retirement, or end of contract.</summary>
    public string? SeparationReason { get; set; }

    /// <summary>Comma list of DayOfWeek ints. 0 is Sunday. Null means no rest day is set.</summary>
    public string? RestDays { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
