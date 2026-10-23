namespace HrCore.Infrastructure;

public sealed record StaffInviteResult(bool Ok, string? AuthUserId, bool EmailSent, string? Note);

/// <summary>Creates the login for a new staff member and sends the invite. The host app implements it.</summary>
public interface IStaffInvite
{
    Task<StaffInviteResult> InviteAsync(
        string staffEmail,
        string displayName,
        CancellationToken cancellationToken);
}
