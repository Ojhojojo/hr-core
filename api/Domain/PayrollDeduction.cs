namespace HrCore.Domain;

/// <summary>
/// Typed deduction amounts for one person and one payroll query period.
/// Missing row means every amount is zero. Not a statutory table.
/// </summary>
public sealed class PayrollDeduction
{
    public Guid Id { get; set; }
    public Guid StaffId { get; set; }
    public Staff Staff { get; set; } = null!;
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public decimal Sss { get; set; }
    public decimal PhilHealth { get; set; }
    public decimal PagIbig { get; set; }
    public decimal Wht { get; set; }
    public decimal Other { get; set; }
}
