namespace HrCore.Domain;

/// <summary>
/// A loan kind accounts pick from a list. <see cref="CutoffOrdinal"/> null means the instalment
/// is taken every period, the same as a free-text loan. A number means that cut-off only:
/// cut-off 1 is the first row of the worker's shop schedule. The number is not stored on the loan
/// or the payslip. Hosts add this table only when <see cref="HrCoreFeatures.LoanKinds"/> is on.
/// </summary>
public sealed class LoanKind
{
    public Guid Id { get; set; }

    /// <summary>Shown on the loan and on the payslip. Unique, ignoring case.</summary>
    public string Name { get; set; } = "";

    /// <summary>Inactive kinds stay on loans that already use them and drop off the picker.</summary>
    public bool Active { get; set; } = true;

    /// <summary>Null deducts every period. 1 is cut-off 1 only.</summary>
    public int? CutoffOrdinal { get; set; }
}
