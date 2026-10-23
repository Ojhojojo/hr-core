namespace HrCore.Features.Attendance;

public sealed record AttendancePunchDto(
    Guid Id,
    Guid StaffId,
    Guid BranchId,
    string BranchName,
    DateTime InAtUtc,
    DateTime? OutAtUtc,
    string InAtManila,
    string? OutAtManila,
    string Source,
    bool HasInPhoto = false,
    bool HasOutPhoto = false,
    string? CloseReason = null,
    double? InLatitude = null,
    double? InLongitude = null,
    double? OutLatitude = null,
    double? OutLongitude = null,
    PunchApprovalStatus ApprovalStatus = PunchApprovalStatus.Approved,
    string? OffsiteReason = null,
    string? ReviewNote = null,
    string? ReviewedBy = null,
    DateTime? ReviewedAtUtc = null);

public sealed record AttendanceMeResponse(
    bool ClockedIn,
    string Status,
    AttendancePunchDto? Open,
    IReadOnlyList<AttendancePunchDto> Today,
    bool RequireClockPhoto = false);

public sealed record AttendanceRegisterRowDto(
    Guid StaffId,
    string StaffName,
    Guid BranchId,
    string BranchName,
    string BranchCode,
    Domain.BranchKind BranchKind,
    DateTime InAtUtc,
    string InAtManila,
    string InDateManila,
    DateTime? OutAtUtc,
    string? OutAtManila,
    decimal? Hours,
    bool IsOpen,
    Guid PunchId,
    bool HasInPhoto,
    bool HasOutPhoto,
    string? CloseReason = null,
    double? InLatitude = null,
    double? InLongitude = null,
    double? OutLatitude = null,
    double? OutLongitude = null,
    PunchApprovalStatus ApprovalStatus = PunchApprovalStatus.Approved,
    string? OffsiteReason = null,
    string? ReviewNote = null,
    string? ReviewedBy = null,
    DateTime? ReviewedAtUtc = null);

public sealed record AttendanceRegisterResponse(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<AttendanceRegisterRowDto> Rows);

/// <summary>A punch with no clock-out yet. Used by the payroll lock warning.</summary>
public sealed record OpenPunchDto(
    Guid PunchId,
    Guid StaffId,
    string StaffName,
    DateTime InAtUtc,
    string InAtManila);

public sealed record AttendanceCsvFile(string FileName, byte[] Bytes);

public sealed record KioskStaffDto(
    Guid Id,
    string DisplayName,
    bool MustClock,
    bool ClockedIn,
    Guid? OpenPunchId,
    DateTime? InAtUtc,
    string? InAtManila);

public sealed class ClockInRequest
{
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? OffsiteReason { get; set; }
}

public sealed class ReviewPunchRequest
{
    public string? Note { get; set; }
}

public sealed class PunchForRequest
{
    public Guid StaffId { get; set; }
    public DateTime? InAtUtc { get; set; }
    public string? InAtManila { get; set; }
    public DateTime? OutAtUtc { get; set; }
    public string? OutAtManila { get; set; }
}

public sealed class ClosePunchRequest
{
    public DateTime? OutAtUtc { get; set; }
    public string? OutAtManila { get; set; }
    public string? Reason { get; set; }
}

/// <summary>Clock-in or clock-out was rejected because the photo was missing or not a picture.</summary>
public sealed class ClockPhotoException(string message) : Exception(message);

public sealed class AttendanceForbiddenException(string message) : Exception(message);

public sealed class AttendanceBadRequestException(string message) : Exception(message);
