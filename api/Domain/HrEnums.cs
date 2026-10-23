namespace HrCore.Domain;

public enum BranchKind
{
    Owned,
    Franchise
}

public enum ProfileKind
{
    Customer,
    Staff
}

/// <summary>
/// Where an incentive bonus stands. Scheduled waits for a payroll period. Paid is frozen into a locked
/// payroll run. Void is cancelled by the owner.
/// </summary>
public enum IncentiveBonusStatus
{
    Scheduled = 0,
    Paid = 1,
    Void = 2
}

public enum EmploymentDocType
{
    Offer,
    Contract,
    Id,
    Other,
    CoeRequestNote
}

public enum LeaveType
{
    Vacation,
    Sick,
    Unpaid,

    /// <summary>
    /// Statutory service incentive leave: five days after one year of service, cash-convertible
    /// at separation. Paid leave — it does not reduce a monthly salary.
    /// </summary>
    ServiceIncentive
}

public enum LeaveRequestStatus
{
    Pending,
    Approved,
    Rejected,
    Cancelled
}

/// <summary>Which half of a single working day a leave request covers. Counts as 0.5 day.</summary>
public enum LeaveHalfDay
{
    Am,
    Pm
}

public enum PunchApprovalStatus
{
    Approved = 0,
    Pending = 1,
    Rejected = 2
}

public enum PayBasis
{
    Hourly,
    Daily,
    Monthly
}

/// <summary>#390: Owner/Admin correction for one calendar day's pay on a draft payroll period.</summary>
public enum PayrollDayPayMode
{
    Full,
    Half,
    None,
    Custom
}

/// <summary>Regular holiday pays the regular-holiday percent. Special day pays the special-day percent.</summary>
public enum HolidayKind
{
    Regular,
    Special
}

/// <summary>Period pays the amount once per payroll period. Day pays it for each day worked.</summary>
public enum AllowancePer
{
    Period,
    Day
}

/// <summary>State of a payroll run for one shop and period. Locked runs are never edited.</summary>
public enum PayrollRunStatus
{
    Draft = 0,
    Locked = 1,
    Void = 2
}
