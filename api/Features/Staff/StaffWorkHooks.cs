using HrCore.Features.Leave;

namespace HrCore.Features.Staff;

/// <summary>
/// Work the business assigns to people (open jobs for a shop, tasks for a project firm). HR asks this
/// before it approves leave or deactivates someone; the app that owns the work answers.
/// Rows are returned as the app's own response shape and sent to the client unchanged.
/// </summary>
public interface IStaffWorkHooks
{
    /// <summary>Shown in the 409 body when open work blocks deactivation.</summary>
    string BlockedDeactivationMessage { get; }

    /// <summary>The person's open work, for the leave decision screen.</summary>
    Task<IReadOnlyList<object>> ListOpenWorkAsync(Guid staffId, CancellationToken cancellationToken);

    /// <summary>
    /// Locks the person's open work inside the caller's transaction and returns what still blocks
    /// deactivation. Empty means the person can be deactivated.
    /// </summary>
    Task<IReadOnlyList<object>> LockAndListOpenWorkAsync(Guid staffId, CancellationToken cancellationToken);

    /// <summary>
    /// Runs inside the approval transaction (the caller saves). Returns an error and changes nothing
    /// when any entry is invalid.
    /// </summary>
    Task<string?> ReassignForLeaveAsync(
        LeaveRequest leave,
        IReadOnlyList<LeaveReassignmentItem> items,
        Guid actorStaffId,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

/// <summary>For an app that assigns no work to people: nothing blocks leave or deactivation.</summary>
public sealed class NoStaffWorkHooks : IStaffWorkHooks
{
    public string BlockedDeactivationMessage => "Reassign this person's open work first.";

    public Task<IReadOnlyList<object>> ListOpenWorkAsync(Guid staffId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<object>>([]);

    public Task<IReadOnlyList<object>> LockAndListOpenWorkAsync(Guid staffId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<object>>([]);

    public Task<string?> ReassignForLeaveAsync(
        LeaveRequest leave,
        IReadOnlyList<LeaveReassignmentItem> items,
        Guid actorStaffId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        Task.FromResult<string?>(items.Count == 0 ? null : "There is no open work to reassign.");
}
