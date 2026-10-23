namespace HrCore.Domain;

/// <summary>
/// One frozen piece of a locked payroll line. Kind "allowance" is this slice;
/// a later slice reuses the same row for loans.
/// </summary>
public sealed class PayrollRunLineItem
{
    public Guid Id { get; set; }
    public Guid LineId { get; set; }
    public PayrollRunLine Line { get; set; } = null!;
    public string Kind { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal Amount { get; set; }
    public bool? Taxable { get; set; }
    public Guid? RefId { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? BalanceAfter { get; set; }
}
