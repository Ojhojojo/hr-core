namespace HrCore.Infrastructure;

public sealed record StaffInviteResult(bool Ok, string? AuthUserId, bool EmailSent, string? Note)
{
    /// <summary>
    /// True only when the host has verified that <see cref="AuthUserId"/> belongs to this person (for
    /// example a confirmed email). hr-core writes AuthUserId only then. Otherwise it leaves the link
    /// empty, and the host's <see cref="IHrStaffResolver"/> links the person on their first verified
    /// sign-in.
    /// </summary>
    public bool AccountConfirmed { get; init; }
}

/// <summary>Creates the login for a new staff member and sends the invite. The host app implements it.</summary>
public interface IStaffInvite
{
    Task<StaffInviteResult> InviteAsync(
        string staffEmail,
        string displayName,
        CancellationToken cancellationToken);
}
