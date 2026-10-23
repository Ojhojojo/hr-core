namespace HrCore.Domain;

/// <summary>
/// Append-only pay-rate history for one worker. A new row supersedes the previous one
/// from its <see cref="EffectiveFrom"/> date. Staff.PayBasis/RatePhp stay the current snapshot.
/// </summary>
public sealed class StaffPayRate
{
    public Guid Id { get; set; }
    public Guid StaffId { get; set; }
    public Staff Staff { get; set; } = null!;
    public DateOnly EffectiveFrom { get; set; }
    public PayBasis Basis { get; set; }
    public decimal RatePhp { get; set; }
    public decimal? OtAfterHoursPerDay { get; set; }
    public decimal? OtMultiplier { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public Guid? CreatedByStaffId { get; set; }
    public Staff? CreatedByStaff { get; set; }
    /// <summary>Owner's note, for example "annual raise" or "promotion".</summary>
    public string? Reason { get; set; }
}
