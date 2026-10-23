namespace HrCore.Domain;

/// <summary>How an overtime row was opened. A punch past shift end, or a request filed ahead.</summary>
public enum OvertimeKind
{
    Punch,
    Request
}

/// <summary>Claimed overtime is not paid until it is approved.</summary>
public enum OvertimeStatus
{
    Pending,
    Approved,
    Rejected
}

/// <summary>
/// One overtime claim or ahead-of-time request. Pay is the approved hours, capped by the hours
/// actually punched past shift end on that Manila date.
/// </summary>
public sealed class OvertimeEntry
{
    public Guid Id { get; set; }
    public Guid StaffId { get; set; }
    public Staff Staff { get; set; } = null!;
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public DateOnly WorkDate { get; set; }
    public OvertimeKind Kind { get; set; }
    public Guid? PunchId { get; set; }
    public AttendancePunch? Punch { get; set; }
    public decimal ClaimedHours { get; set; }
    public decimal? ApprovedHours { get; set; }
    public OvertimeStatus Status { get; set; } = OvertimeStatus.Pending;
    public string? Reason { get; set; }
    public string? DecisionNote { get; set; }
    public Guid? DecidedByStaffId { get; set; }
    public Staff? DecidedByStaff { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
