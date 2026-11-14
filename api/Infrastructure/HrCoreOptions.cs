namespace HrCore.Infrastructure;

/// <summary>
/// Host switches for shared HR. Every default is the safe one, so a host that registers nothing
/// gets production behaviour. Register an instance before <c>AddHrCore</c>.
/// </summary>
public sealed class HrCoreOptions
{
    /// <summary>
    /// Honours the <c>X-Test-Auth-User-Id</c> header, which signs a request in as that staff member
    /// with no token. Only the hr-core test host sets this. Keep it false in every deployed app,
    /// whatever its environment name is.
    /// </summary>
    public bool AllowTestAuthHeader { get; init; }
}
