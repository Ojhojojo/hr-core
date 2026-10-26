/** One append-only people/HR trail row, newest first from the API. */
export interface HrChangeLogRow {
  id: string;
  actorStaffId?: string | null;
  actorName?: string | null;
  subjectStaffId: string;
  subjectName: string;
  atUtc: string;
  field: string;
  oldValue?: string | null;
  newValue?: string | null;
  periodFrom?: string | null;
  periodTo?: string | null;
  year?: number | null;
  refType?: string | null;
  refId?: string | null;
}

export interface HrChangeLogList {
  rows: HrChangeLogRow[];
}

/** Dotted keys written by HrChangeField on the API. Unknown keys fall back to the raw key. */
const FIELD_LABELS: Record<string, string> = {
  'staff.role': 'Role',
  'staff.active': 'Active',
  'staff.pay_basis': 'Pay basis',
  'staff.rate': 'Pay rate',
  'staff.ot_after_hours': 'OT after hours',
  'staff.ot_multiplier': 'OT multiplier',
  'staff.hire_date': 'Hire date',
  'staff.position': 'Position',
  'staff.department': 'Department',
  'staff.employee_no': 'Employee number',
  'staff.emergency_contact': 'Emergency contact',
  'staff.tin': 'TIN',
  'staff.sss': 'SSS',
  'staff.philhealth': 'PhilHealth',
  'staff.pagibig': 'Pag-IBIG',
  'staff.must_clock': 'Uses time clock',
  'leave.allotment': 'Leave allotment',
  'leave.decision': 'Leave decision',
  'leave.request': 'Leave request',
  'payroll.deduction.sss': 'SSS deduction',
  'payroll.deduction.philhealth': 'PhilHealth deduction',
  'payroll.deduction.pagibig': 'Pag-IBIG deduction',
  'payroll.deduction.wht': 'WHT deduction',
  'payroll.deduction.other': 'Other deduction',
  'payroll.thirteenth.override': '13th-month override',
  'payroll.run.lock': 'Payroll run lock',
  'payroll.run.void': 'Payroll run void',
  'payslip.issued': 'Payslip issued',
  'staff.separation_date': 'Separation date',
  'staff.separation_reason': 'Separation reason',
  'attendance.punch_for': 'Punch added',
  'attendance.punch_close': 'Punch closed',
  'document.upload': 'Document upload',
};

/** "staff.rate" -> "Pay rate"; an unknown key is shown as-is. */
export function hrChangeFieldLabel(field: string): string {
  return FIELD_LABELS[field] ?? field;
}

const TRUE_FALSE_LABELS: Record<string, [string, string]> = {
  'staff.active': ['Active', 'Inactive'],
  'staff.must_clock': ['Uses time clock', "Doesn't use clock"],
};

/** Value shown next to the label. Stored booleans read better as words. */
export function hrChangeValue(field: string, value: string | null | undefined): string {
  if (value == null || value === '') {
    return '—';
  }

  const labels = TRUE_FALSE_LABELS[field];
  if (labels) {
    return value.trim().toLowerCase() === 'true' ? labels[0] : labels[1];
  }

  return value;
}

/** Newest first, with the id as a tie-break so the order is stable for same-instant rows. */
export function sortChangeLogNewestFirst(rows: HrChangeLogRow[]): HrChangeLogRow[] {
  return [...rows].sort((left, right) => {
    const at = right.atUtc.localeCompare(left.atUtc);
    return at !== 0 ? at : right.id.localeCompare(left.id);
  });
}
