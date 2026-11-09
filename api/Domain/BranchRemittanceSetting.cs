namespace HrCore.Domain;

/// <summary>
/// Whether one shop records employer statutory shares and shows the monthly remittance report.
/// No row means off, which is every shop until an owner turns it on.
/// Hosts add this table only when <see cref="HrCoreFeatures.EmployerRemittance"/> is on.
/// </summary>
public sealed class BranchRemittanceSetting
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    /// <summary>Off until an owner turns the report on for this shop.</summary>
    public bool Enabled { get; set; }
}
