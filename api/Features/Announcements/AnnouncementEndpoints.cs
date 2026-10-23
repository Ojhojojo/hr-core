namespace HrCore.Features.Announcements;

public static class AnnouncementEndpoints
{
    public static IEndpointRouteBuilder MapAnnouncementApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/announcements")
            .WithTags("Announcements")
            .AddEndpointFilter<StaffAuthorizationFilter>();

        api.MapGet("/today", ListToday)
            .WithName("ListTodayAnnouncements")
            .WithSummary("Active announcements whose dates include today");

        api.MapGet("/", ListAll)
            .AddEndpointFilter(StaffRoleFilter.OwnerOrAdmin)
            .WithName("ListAnnouncements");

        api.MapPost("/", Create)
            .AddEndpointFilter(StaffRoleFilter.OwnerOrAdmin)
            .WithName("CreateAnnouncement");

        api.MapPut("/{id:guid}", Update)
            .AddEndpointFilter(StaffRoleFilter.OwnerOrAdmin)
            .WithName("UpdateAnnouncement");

        api.MapDelete("/{id:guid}", Delete)
            .AddEndpointFilter(StaffRoleFilter.OwnerOrAdmin)
            .WithName("DeleteAnnouncement");

        return app;
    }

    private static async Task<IResult> ListToday(
        AnnouncementService announcements,
        CancellationToken cancellationToken) =>
        Results.Ok(await announcements.ListVisibleAsync(cancellationToken));

    private static async Task<IResult> ListAll(
        AnnouncementService announcements,
        CancellationToken cancellationToken) =>
        Results.Ok(await announcements.ListAllAsync(cancellationToken));

    private static async Task<IResult> Create(
        UpsertAnnouncementRequest request,
        HttpContext http,
        AnnouncementService announcements,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await announcements.CreateAsync(request, http.GetStaff()?.Id, cancellationToken);
            return Results.Created($"/api/announcements/{created.Id}", created);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> Update(
        Guid id,
        UpsertAnnouncementRequest request,
        AnnouncementService announcements,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await announcements.UpdateAsync(id, request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    private static async Task<IResult> Delete(
        Guid id,
        AnnouncementService announcements,
        CancellationToken cancellationToken)
    {
        try
        {
            await announcements.DeleteAsync(id, cancellationToken);
            return Results.NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }
}
