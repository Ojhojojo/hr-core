namespace HrCore.Domain;

/// <summary>
/// Owner-recorded discretionary bonus for one sales person and one calendar quarter. Never computed
/// from a target. It is paid through payroll: it is scheduled for one payroll period and becomes Paid
/// when that period's run locks. Void instead of delete: a voided line keeps its amount and the void reason.
/// </summary>
public sealed class IncentiveBonus
{
    public Guid Id { get; set; }
    public Guid StaffId { get; set; }
    public int Year { get; set; }
    public int Quarter { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = "";
    public Guid CreatedByStaffId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool IsVoided { get; set; }
    public string? VoidReason { get; set; }
    public Guid? VoidedByStaffId { get; set; }
    public DateTime? VoidedAtUtc { get; set; }

    /// <summary>Scheduled, Paid or Void. Void always matches <see cref="IsVoided"/>.</summary>
    public IncentiveBonusStatus Status { get; set; } = IncentiveBonusStatus.Scheduled;

    /// <summary>First day of the payroll period this bonus is paid in. Null only on rows recorded before payroll pay.</summary>
    public DateOnly? PayPeriodFrom { get; set; }

    /// <summary>Last day of the payroll period this bonus is paid in.</summary>
    public DateOnly? PayPeriodTo { get; set; }

    /// <summary>The locked payroll run that paid this bonus. Set only when Status is Paid.</summary>
    public Guid? PaidRunId { get; set; }

    /// <summary>When the payroll run locked and paid this bonus.</summary>
    public DateTime? PaidAtUtc { get; set; }

    /// <summary>Client-supplied key from one open of the Record bonus dialog. Unique per sales person.</summary>
    public string? IdempotencyKey { get; set; }
}
