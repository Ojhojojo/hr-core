namespace HrCore.Domain;

/// <summary>Stored payout status on a locked line. Null means the payout was not held.</summary>
public static class PayoutHoldStatus
{
    public const string Held = "held";

    public const string Released = "released";
}

/// <summary>
/// One worker's current payout hold. Earnings are still computed. The payout stays held
/// until clearance is approved and an owner or admin releases it.
/// Hosts add this table only when <see cref="HrCoreFeatures.PayoutHold"/> is on.
/// </summary>
public sealed class StaffPayoutHold
{
    public const int ReasonMaxLength = 500;

    public Guid Id { get; set; }

    public Guid StaffId { get; set; }

    public Staff Staff { get; set; } = null!;

    /// <summary>Why the payout is held. Required. Clearance notes live here.</summary>
    public string Reason { get; set; } = "";

    public DateTime HeldAtUtc { get; set; }

    public Guid? HeldByStaffId { get; set; }

    public Staff? HeldByStaff { get; set; }

    /// <summary>Manila calendar date the hold was released. Null while the payout is held.</summary>
    public DateOnly? ReleasedOn { get; set; }

    public Guid? ReleasedByStaffId { get; set; }

    public Staff? ReleasedByStaff { get; set; }
}
