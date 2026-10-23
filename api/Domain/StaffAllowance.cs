namespace HrCore.Domain;

/// <summary>
/// Standing allowance for one worker (COLA, rice, and other free-text names).
/// A locked payroll line copies its own items, so deleting this row does not change a locked run.
/// </summary>
public sealed class StaffAllowance
{
    public Guid Id { get; set; }
    public Guid StaffId { get; set; }
    public Staff Staff { get; set; } = null!;
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public AllowancePer Per { get; set; }
    public bool Taxable { get; set; } = true;
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? Note { get; set; }
    public bool Active { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public Guid? CreatedByStaffId { get; set; }
    public Staff? CreatedByStaff { get; set; }
}
