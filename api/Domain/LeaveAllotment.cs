namespace HrCore.Domain;

public sealed class LeaveAllotment
{
    public Guid Id { get; set; }
    public Guid StaffId { get; set; }
    public Staff Staff { get; set; } = null!;
    public int Year { get; set; }
    public LeaveType LeaveType { get; set; }
    public decimal Days { get; set; }

    /// <summary>
    /// Optional custom expiry date for this allotment. Null computes automatically from the
    /// branch's <see cref="Branch.LeaveExpirationMode"/>.
    /// </summary>
    public DateOnly? ExpiresOn { get; set; }
}
