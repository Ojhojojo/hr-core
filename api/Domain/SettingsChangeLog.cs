namespace HrCore.Domain;

/// <summary>
/// Append-only trail for shop and company settings. One row per changed field.
/// Rows are never updated or deleted in the app.
/// </summary>
public sealed class SettingsChangeLog
{
    public Guid Id { get; set; }

    /// <summary>
    /// Shop the change belongs to. Null for company-wide settings (business details, lists, services,
    /// message templates, the demo toggle). A loose reference with no foreign key, so the trail
    /// survives when a shop is hard-deleted.
    /// </summary>
    public Guid? BranchId { get; set; }

    /// <summary>Settings area, for example <c>shop_desk</c>, <c>bank_account</c> or <c>part_grade</c>.</summary>
    public string Area { get; set; } = "";

    /// <summary>Field key within the area, for example <c>invoice_prefix_vat</c> or <c>{rowId}.name</c>.</summary>
    public string Field { get; set; } = "";

    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    /// <summary>
    /// Who made the change. Null only when no staff profile was bound to the request (the unauthenticated
    /// path that Testing uses). Owner-only settings routes always bind one outside tests.
    /// </summary>
    public Guid? ChangedByStaffId { get; set; }
    public Staff? ChangedByStaff { get; set; }

    public DateTime ChangedAtUtc { get; set; }
}
