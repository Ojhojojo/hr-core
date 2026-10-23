namespace HrCore.Features.Announcements;

public sealed record AnnouncementResponse(
    Guid Id,
    string Title,
    string Body,
    DateOnly StartDate,
    DateOnly EndDate,
    bool Active);

public sealed class UpsertAnnouncementRequest
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool Active { get; set; } = true;
}
