namespace HrCore.Domain;

/// <summary>
/// Owner-entered basic earned for one person and one calendar year.
/// When a row exists, the 13th-month estimate uses this amount instead of the computed basic.
/// </summary>
public sealed class PayrollThirteenthOverride
{
    public const int MinYear = 2000;
    public const int MaxYear = 2100;
    public const decimal MaxAmount = 9999999999.99m;

    public Guid Id { get; set; }
    public Guid StaffId { get; set; }
    public Staff Staff { get; set; } = null!;
    public int Year { get; set; }
    public decimal BasicEarnedOverride { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid? UpdatedByStaffId { get; set; }
    public Staff? UpdatedByStaff { get; set; }
}
