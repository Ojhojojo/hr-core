namespace HrCore.Infrastructure;

public static class StaffActor
{
    /// <summary>
    /// Shop-scoped owner: <see cref="StaffRole.Owner"/> pinned to one live shop.
    /// Demo showcase owner stays HQ for settings/invites even though ops is locked to Demo.
    /// </summary>
    public static bool IsShopScopedOwner(this Staff? staff) =>
        staff is { Role: StaffRole.Owner, BranchId: Guid home } && home != PlatformIds.DemoBranchId;

    /// <summary>
    /// Company (unscoped) owner. Missing staff is treated as HQ so Testing hosts keep working.
    /// </summary>
    public static bool IsHqOwner(this Staff? staff) =>
        staff is null || (staff.Role == StaffRole.Owner && !staff.IsShopScopedOwner());
}
