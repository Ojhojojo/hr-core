namespace HrCore.Domain;

/// <summary>
/// One holiday for a shop, or for every shop when <see cref="BranchId"/> is null.
/// Unique on (BranchId, Date).
/// </summary>
public sealed class ShopHoliday
{
    public Guid Id { get; set; }
    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
    public HolidayKind Kind { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
