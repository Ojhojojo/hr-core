namespace HrCore.Domain;

public sealed class Profile
{
    public Guid Id { get; set; }
    public string? AuthUserId { get; set; }
    public ProfileKind Kind { get; set; }
    public string? Email { get; set; }
    public string DisplayName { get; set; } = "";
    public string? Mobile { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Staff? Staff { get; set; }
}
