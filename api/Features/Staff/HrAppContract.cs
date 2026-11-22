using System.Text.Json;

namespace HrCore.Features.Staff;

// What shared HR needs from the app that hosts it. Each method below is a partial method with no
// body here: the host app must implement every one in its own part of the same class, or the build
// fails. The app's StaffRole enum must have Owner and Admin; HR uses those two names directly.

/// <summary>How the app's roles behave inside HR.</summary>
public static partial class HrRoles
{
    /// <summary>
    /// Desk roles see the whole shop's HR records and decide requests. Everyone else sees and files
    /// only their own. Owner and admin are always desk roles.
    /// </summary>
    public static partial bool IsDesk(StaffRole role);

    /// <summary>Default for <c>MustClock</c> on a new person, and whether a blank rate is a config issue.</summary>
    public static partial bool ClocksByDefault(StaffRole role);

    /// <summary>Roles that see only themselves in the staff list.</summary>
    public static partial bool SeesOnlySelfInStaffList(StaffRole role);

    /// <summary>Role shown on a locked payroll line whose person no longer exists.</summary>
    public static partial StaffRole UnknownPersonRole();

    /// <summary>Roles that may read the daily staff availability snapshot (approved leave and clock-in).</summary>
    public static partial bool CanSeeStaffAvailability(StaffRole role);

    /// <summary>
    /// Who may run the shop HR desk: leave approvals/team balances, punch-for / kiosk for
    /// others, punch review, and similar manager attendance actions. Host apps with Access
    /// (subroles) can narrow this below all desk roles.
    /// </summary>
    public static partial bool CanManageShopHr(StaffRole role, string? subrole);

    /// <summary>Reads the app-supplied subrole from <see cref="StaffAccessFlags.Describe"/> when present.</summary>
    internal static string? AccessSubrole(Domain.Staff? staff) =>
        staff is not null
        && StaffAccessFlags.Describe(staff) is { } fields
        && fields.TryGetValue("subrole", out var raw)
            ? raw as string
            : null;

    internal static bool CanManageShopHr(Domain.Staff staff) =>
        CanManageShopHr(staff.Role, AccessSubrole(staff));
}

/// <summary>
/// The app's own per-person access switches (for example, who may open a document type). HR stores
/// none of them; it passes the request's extra fields through and writes the app's fields back out.
/// </summary>
public static partial class StaffAccessFlags
{
    /// <summary>Sets the switches on a new person. <paramref name="requested"/> holds the request's extra fields.</summary>
    public static partial void ApplyOnCreate(Domain.Staff staff, IReadOnlyDictionary<string, JsonElement>? requested);

    /// <summary>Applies switches present in an update request. Absent fields stay as they are.</summary>
    public static partial void ApplyOnUpdate(Domain.Staff staff, IReadOnlyDictionary<string, JsonElement>? requested);

    /// <summary>The switches as response fields, keyed by their JSON names. Null when the app has none.</summary>
    public static partial Dictionary<string, object?>? Describe(Domain.Staff staff);

    /// <summary>
    /// Auditable values for app-specific fields on <paramref name="staff"/>, keyed by
    /// <c>HrChangeLog</c> field name (for example <c>staff.subrole</c>). Null when the app audits none.
    /// </summary>
    public static partial IReadOnlyDictionary<string, string?>? AuditFields(Domain.Staff staff);
}
