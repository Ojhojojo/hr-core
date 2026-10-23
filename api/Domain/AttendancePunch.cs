namespace HrCore.Domain;

/// <summary>
/// One open or closed shift. <see cref="InAtUtc"/> is required; <see cref="OutAtUtc"/> is null while clocked in.
/// Stored in UTC.
/// <see cref="Source"/> is how the punch was opened: <see cref="SourceSelf"/> (phone), <see cref="SourceKiosk"/> (desk tap),
/// or <see cref="SourcePunchFor"/> (owner/admin entered the times). Closing an open punch keeps that source and sets
/// <see cref="CloseReason"/> when an owner or admin closes it. There is no separate "manual-close" source.
/// Phone self-punch stores shop coordinates. Kiosk, punch-for, and manual close do not.
/// </summary>
public sealed class AttendancePunch
{
    public const string SourceSelf = "ops";
    public const string SourceKiosk = "kiosk";
    public const string SourcePunchFor = "punch-for";

    public Guid Id { get; set; }
    public Guid StaffId { get; set; }
    public Staff Staff { get; set; } = null!;
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public DateTime InAtUtc { get; set; }
    public DateTime? OutAtUtc { get; set; }
    public string Source { get; set; } = SourceSelf;

    /// <summary>Set when an owner or admin closes an open punch. Null for a normal clock-out or a punch-for that was entered already closed.</summary>
    public string? CloseReason { get; set; }

    public double? InLatitude { get; set; }
    public double? InLongitude { get; set; }
    public double? OutLatitude { get; set; }
    public double? OutLongitude { get; set; }

    public string? InPhotoStoragePath { get; set; }
    public string? InPhotoFileName { get; set; }
    public string? InPhotoContentType { get; set; }
    public string? OutPhotoStoragePath { get; set; }
    public string? OutPhotoFileName { get; set; }
    public string? OutPhotoContentType { get; set; }

    public PunchApprovalStatus ApprovalStatus { get; set; } = PunchApprovalStatus.Approved;
    public string? OffsiteReason { get; set; }
    public Guid? ReviewedByStaffId { get; set; }
    public Staff? ReviewedByStaff { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }
}
