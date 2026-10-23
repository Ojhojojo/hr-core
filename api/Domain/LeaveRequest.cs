namespace HrCore.Domain;

public sealed class LeaveRequest
{
    public Guid Id { get; set; }
    public Guid StaffId { get; set; }
    public Staff Staff { get; set; } = null!;
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public LeaveType LeaveType { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public LeaveRequestStatus Status { get; set; }
    public string? Note { get; set; }
    public Guid? DecidedByStaffId { get; set; }
    public Staff? DecidedByStaff { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Null for a full-day request. Set only when StartDate == EndDate.</summary>
    public LeaveHalfDay? HalfDay { get; set; }
    public Guid? CancelledByStaffId { get; set; }
    public Staff? CancelledByStaff { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancelReason { get; set; }
}
