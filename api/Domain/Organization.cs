namespace HrCore.Domain;

/// <summary>The business. One per deploy. The host app adds its own fields in another part of this class.</summary>
public sealed partial class Organization
{
    public Guid Id { get; set; }
    public string LegalName { get; set; } = "";
    public string TradingName { get; set; } = "";
    public string? Tin { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public ICollection<Branch> Branches { get; set; } = new List<Branch>();
}
