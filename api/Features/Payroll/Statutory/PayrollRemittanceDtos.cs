using System.Text.Json.Serialization;

namespace HrCore.Features.Payroll;

public sealed record RemittanceAgencyDto(
    string Key,
    string Label,
    decimal EmployeeShare,
    decimal EmployerShare,
    decimal Total);

/// <summary>
/// One shop, one month. <see cref="Enabled"/> is false until the shop turns the report on.
/// <see cref="EmptyMessage"/> is set when it is on and no locked run ends in the month.
/// </summary>
public sealed record RemittanceRunRefDto(
    Guid RunId,
    DateOnly From,
    DateOnly To,
    string Name,
    string Status);

public sealed record RemittanceReportDto(
    Guid BranchId,
    string Month,
    bool Enabled,
    IReadOnlyList<RemittanceAgencyDto> Agencies,
    decimal EmployeeShare,
    decimal EmployerShare,
    decimal Total,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? EmptyMessage = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<RemittanceRunRefDto>? UnreviewedRuns = null);

public sealed record EmployerShareReviewLineDto(
    Guid StaffId,
    string StaffName,
    decimal SuggestedSss,
    decimal SuggestedPhilHealth,
    decimal SuggestedPagIbig,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? EmployerSss,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? EmployerPhilHealth,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? EmployerPagIbig);

public sealed record EmployerShareReviewDto(
    Guid RunId,
    bool ReadOnly,
    IReadOnlyList<EmployerShareReviewLineDto> Lines,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTime? ReviewedAtUtc = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ReviewedByName = null);

public sealed class ConfirmEmployerSharesRequest
{
    public List<ConfirmEmployerShareLine>? Lines { get; set; }
}

public sealed class ConfirmEmployerShareLine
{
    public Guid StaffId { get; set; }
    public decimal? EmployerSss { get; set; }
    public decimal? EmployerPhilHealth { get; set; }
    public decimal? EmployerPagIbig { get; set; }
}

public sealed record RemittanceSettingDto(Guid BranchId, bool Enabled);

public sealed class UpdateRemittanceSettingRequest
{
    public bool? Enabled { get; set; }
}
