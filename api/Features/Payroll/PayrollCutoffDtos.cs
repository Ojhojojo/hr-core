namespace HrCore.Features.Payroll;

public sealed record PayrollCutoffDto(int Ordinal, int StartDay, int EndDay);

public sealed record PayrollCutoffPeriodDto(int Ordinal, DateOnly From, DateOnly To);

/// <summary>
/// A branch schedule. <see cref="Mode"/> is <c>free</c> when the branch has no rows.
/// <see cref="Periods"/> are the concrete dates for <see cref="Month"/> (<c>yyyy-MM</c>), empty in free mode.
/// </summary>
public sealed record PayrollCutoffScheduleDto(
    Guid BranchId,
    string Mode,
    IReadOnlyList<PayrollCutoffDto> Cutoffs,
    string Month,
    IReadOnlyList<PayrollCutoffPeriodDto> Periods);

public sealed class UpdatePayrollCutoffScheduleRequest
{
    /// <summary><c>free</c> (typed from/to, the default) or <c>schedule</c>.</summary>
    public string? Mode { get; set; }

    public List<PayrollCutoffInput>? Cutoffs { get; set; }
}

public sealed class PayrollCutoffInput
{
    public int Ordinal { get; set; }
    public int StartDay { get; set; }
    public int EndDay { get; set; }
}
