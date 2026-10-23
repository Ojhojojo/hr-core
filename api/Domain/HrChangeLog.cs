namespace HrCore.Domain;

/// <summary>
/// Append-only people/HR trail. One row per changed field on a worker's record.
/// Rows are never updated or deleted in the app.
/// </summary>
public sealed class HrChangeLog
{
    public Guid Id { get; set; }
    /// <summary>Who made the change. Null for system writes, such as a backfill.</summary>
    public Guid? ActorStaffId { get; set; }
    public Staff? ActorStaff { get; set; }
    /// <summary>Whose record changed.</summary>
    public Guid SubjectStaffId { get; set; }
    public Staff SubjectStaff { get; set; } = null!;
    public DateTime AtUtc { get; set; }
    /// <summary>Dotted key, for example <c>staff.rate</c> or <c>leave.allotment</c>.</summary>
    public string Field { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateOnly? PeriodFrom { get; set; }
    public DateOnly? PeriodTo { get; set; }
    public int? Year { get; set; }
    /// <summary>Loose reference to the row that changed: <c>leave_request</c>, <c>document</c>, <c>payroll_run</c>.</summary>
    public string? RefType { get; set; }
    public Guid? RefId { get; set; }
}
