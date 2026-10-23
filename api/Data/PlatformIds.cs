namespace HrCore.Data;

/// <summary>
/// Fixed ids the platform seeds in every deploy: the one organization, its first live branch and the
/// Demo branch. Each deploy has its own database, so the same ids are safe across apps.
/// </summary>
public static class PlatformIds
{
    public static readonly Guid OrganizationId = Guid.Parse("aaaaaaaa-1111-4111-8111-aaaaaaaaaaaa");
    public static readonly Guid BranchId = Guid.Parse("bbbbbbbb-2222-4222-8222-bbbbbbbbbbbb");
    public static readonly Guid DemoBranchId = Guid.Parse("dddddddd-4444-4444-8444-dddddddddddd");
}
