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
public sealed record RemittanceReportDto(
    Guid BranchId,
    string Month,
    bool Enabled,
    IReadOnlyList<RemittanceAgencyDto> Agencies,
    decimal EmployeeShare,
    decimal EmployerShare,
    decimal Total,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? EmptyMessage = null);

public sealed record RemittanceSettingDto(Guid BranchId, bool Enabled);

public sealed class UpdateRemittanceSettingRequest
{
    public bool? Enabled { get; set; }
}
