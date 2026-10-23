using Microsoft.EntityFrameworkCore;

namespace HrCore.Features.Announcements;

public sealed class AnnouncementService(IHrDb db)
{
    public async Task<IReadOnlyList<AnnouncementResponse>> ListVisibleAsync(CancellationToken cancellationToken)
    {
        var today = ManilaTime.Today();
        var rows = await db.Announcements.AsNoTracking()
            .Where(a => a.Active && a.StartDate <= today && a.EndDate >= today)
            .OrderByDescending(a => a.StartDate)
            .ThenBy(a => a.Title)
            .ToListAsync(cancellationToken);
        return rows.Select(ToResponse).ToList();
    }

    public async Task<IReadOnlyList<AnnouncementResponse>> ListAllAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Announcements.AsNoTracking()
            .OrderByDescending(a => a.StartDate)
            .ThenBy(a => a.Title)
            .ToListAsync(cancellationToken);
        return rows.Select(ToResponse).ToList();
    }

    public async Task<AnnouncementResponse> CreateAsync(
        UpsertAnnouncementRequest request,
        Guid? actorStaffId,
        CancellationToken cancellationToken)
    {
        Validate(request);
        var now = DateTime.UtcNow;
        var row = new Announcement
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Body = request.Body.Trim(),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Active = request.Active,
            CreatedByStaffId = actorStaffId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Announcements.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(row);
    }

    public async Task<AnnouncementResponse> UpdateAsync(
        Guid id,
        UpsertAnnouncementRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        var row = await db.Announcements.FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                  ?? throw new KeyNotFoundException("Announcement not found.");
        row.Title = request.Title.Trim();
        row.Body = request.Body.Trim();
        row.StartDate = request.StartDate;
        row.EndDate = request.EndDate;
        row.Active = request.Active;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(row);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await db.Announcements.FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                  ?? throw new KeyNotFoundException("Announcement not found.");
        db.Announcements.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
    }

    internal static void Validate(UpsertAnnouncementRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new InvalidOperationException("Title is required.");
        }

        if (request.Title.Trim().Length > 160)
        {
            throw new InvalidOperationException("Title must be 160 characters or fewer.");
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            throw new InvalidOperationException("Body is required.");
        }

        if (request.Body.Trim().Length > 4000)
        {
            throw new InvalidOperationException("Body must be 4000 characters or fewer.");
        }

        if (request.StartDate == default || request.EndDate == default)
        {
            throw new InvalidOperationException("Start date and end date are required.");
        }

        if (request.EndDate < request.StartDate)
        {
            throw new InvalidOperationException("End date must be on or after the start date.");
        }
    }

    private static AnnouncementResponse ToResponse(Announcement row) =>
        new(row.Id, row.Title, row.Body, row.StartDate, row.EndDate, row.Active);
}
