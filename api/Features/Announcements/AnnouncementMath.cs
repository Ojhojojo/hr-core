namespace HrCore.Features.Announcements;

public static class AnnouncementMath
{
    /// <summary>
    /// Visible on Today when the row is active and Manila today falls on the start or end date, or between them.
    /// An end date of yesterday is hidden.
    /// </summary>
    public static bool IsVisibleOn(bool active, DateOnly startDate, DateOnly endDate, DateOnly today) =>
        active && startDate <= today && endDate >= today;
}
