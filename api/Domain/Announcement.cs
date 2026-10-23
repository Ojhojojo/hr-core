namespace HrCore.Domain;

public sealed class Announcement
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool Active { get; set; } = true;
    public Guid? CreatedByStaffId { get; set; }
    public Staff? CreatedByStaff { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
