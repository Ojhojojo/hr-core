namespace HrCore.Data;

/// <summary>
/// Fixed ids the platform seeds in every deploy: the one organization and its first live
/// branch. Each deploy has its own database, so the same ids are safe across apps.
/// </summary>
public static class PlatformIds
{
    public static readonly Guid OrganizationId = Guid.Parse("aaaaaaaa-1111-4111-8111-aaaaaaaaaaaa");
    public static readonly Guid BranchId = Guid.Parse("bbbbbbbb-2222-4222-8222-bbbbbbbbbbbb");
}
