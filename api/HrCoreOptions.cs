namespace HrCore;

/// <summary>
/// Opt-in host switches for <see cref="HrCoreSetup.AddHrCore{TDb}(IServiceCollection, Action{HrCoreOptions})"/>.
/// Every switch is off by default, so a host that sets none sees no change.
/// </summary>
public sealed class HrCoreOptions
{
    /// <summary>
    /// When true, an open (not locked) payroll register shows the statutory pack's suggested
    /// SSS, PhilHealth, Pag-IBIG and withholding for a person who has no saved deduction row for
    /// the period, prorated the same way the Suggest button does. A saved row, including saved
    /// zeros, always wins. Locking freezes what the register shows. Off: unsaved rows show zero.
    /// No table or column is involved, so a host needs no migration.
    /// </summary>
    public bool SuggestStatutoryInPreview { get; set; }
}
