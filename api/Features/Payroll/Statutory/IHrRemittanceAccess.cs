namespace HrCore.Features.Payroll;

/// <summary>
/// Who may view or export the remittance report. The host supplies this the same way it
/// supplies other optional HR hooks: register it before <c>AddHrCore</c>. The default uses
/// <see cref="StaffRoleFilter.OwnerOrAdmin"/>, so any other role is refused. hr-core does not
/// name host roles.
/// </summary>
public interface IHrRemittanceAccess
{
    bool CanView(StaffRole role);
}

/// <summary>Default: the existing owner-or-admin policy.</summary>
public sealed class OwnerAdminRemittanceAccess : IHrRemittanceAccess
{
    public bool CanView(StaffRole role) => StaffRoleFilter.OwnerOrAdmin.Allows(role);
}

/// <summary>
/// Who may confirm employer shares. The host supplies this before <c>AddHrCore</c>.
/// The default refuses every caller. hr-core does not name a role for this.
/// </summary>
public interface IHrEmployerShareReview
{
    bool CanReview(StaffRole role);
}

/// <summary>Default until the host supplies its own answer.</summary>
public sealed class ClosedEmployerShareReview : IHrEmployerShareReview
{
    public bool CanReview(StaffRole role) => false;
}
