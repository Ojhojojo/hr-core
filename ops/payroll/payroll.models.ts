export type PayBasis = 'hourly' | 'daily' | 'monthly';

export type PayrollRegisterSource = 'live' | 'locked';

export type PayrollRunStatus = 'draft' | 'locked' | 'void';

/** One dated rate span inside a register period. Sent only when the period was priced at two or more rates. */
export interface PayrollRateSlice {
  from: string;
  to: string;
  basis: PayBasis;
  ratePhp: number;
  otAfterHoursPerDay?: number | null;
  otMultiplier?: number | null;
}

export interface PayrollStaffRow {
  staffId: string;
  staffName: string;
  role: string;
  branchId?: string | null;
  branchName?: string | null;
  branchCode?: string | null;
  payBasis?: PayBasis | null;
  basis?: PayBasis | null;
  ratePhp?: number | null;
  rate?: number | null;
  otAfterHoursPerDay?: number | null;
  otMultiplier?: number | null;
  regularHours: number;
  otHours: number;
  otPay?: number;
  daysPaid: number;
  amount: number;
  notes?: string | null;
  active?: boolean;
  sss?: number;
  philHealth?: number;
  pagIbig?: number;
  wht?: number;
  other?: number;
  deductions?: number;
  net?: number;
  rateSlices?: PayrollRateSlice[] | null;
  holidayHours?: number | null;
  restDayHours?: number | null;
  nightHours?: number | null;
  premiumPay?: number | null;
  premiumOverridden?: boolean;
  warnings?: string[] | null;
  allowances?: number | null;
  allowanceItems?: PayrollAllowanceItem[] | null;
  allowancesOverridden?: boolean;
  loanDeductions?: number | null;
  loanItems?: PayrollLoanItem[] | null;
  dayPayOverrides?: PayrollDayPayOverride[] | null;
  /** `held` or `released`. Omitted when this payout was not held. */
  payoutStatus?: 'held' | 'released' | null;
  holdReason?: string | null;
  /** Manila date `yyyy-MM-dd` once the hold is released. */
  releasedOn?: string | null;
  releasedByName?: string | null;
  /** What was deposited. Omitted until someone records it. */
  depositedAmount?: number | null;
  /** Net minus deposited. Positive is short, negative is over. Omitted until a deposit is recorded. */
  discrepancy?: number | null;
}

export type PayrollDayPayMode = 'full' | 'half' | 'none' | 'custom';

export interface PayrollDayPayOverride {
  id: string;
  staffId: string;
  workDate: string;
  mode: PayrollDayPayMode;
  customAmount?: number | null;
  reason: string;
  setByStaffId?: string | null;
  setAtUtc: string;
}

export interface SavePayrollDayPayOverride {
  staffId: string;
  workDate: string;
  mode: PayrollDayPayMode | null;
  customAmount?: number | null;
  reason?: string | null;
}

export interface PayrollDeductionDraft {
  sss: number;
  philHealth: number;
  pagIbig: number;
  wht: number;
  other: number;
}

export interface SavePayrollDeductions {
  staffId: string;
  from: string;
  to: string;
  sss: number;
  philHealth: number;
  pagIbig: number;
  wht: number;
  other: number;
}

export interface PayrollDeductionSaved {
  staffId: string;
  from: string;
  to: string;
  sss: number;
  philHealth: number;
  pagIbig: number;
  wht: number;
  other: number;
  deductions: number;
}

export interface PayrollRegisterResponse {
  from: string;
  to: string;
  rows: PayrollStaffRow[];
  totalAmount?: number;
  totalNet?: number;
  /** `live` for a computed period, `locked` when the rows come from a frozen run. */
  source?: PayrollRegisterSource | null;
  runId?: string | null;
  lockedAtUtc?: string | null;
  lockedByName?: string | null;
  pendingOvertimeClaimsCount?: number;
  /** Sales bonuses still scheduled whose pay period has ended and no locked run has paid. */
  unpaidBonuses?: UnpaidBonus[];
  /** `free` when the shop has no cut-off schedule. `schedule` when `cutoffPeriods` is set. */
  cutoffMode?: 'free' | 'schedule' | null;
  /** Cut-offs for every end month from `from` through `to`. Empty or omitted in free mode. Dates are yyyy-MM-dd. */
  cutoffPeriods?: PayrollCutoffPeriod[] | null;
  /** Net still computed but not released. Omitted when nobody is held. */
  totalHeld?: number | null;
}

/** One sales bonus that is past its pay period and still not paid. */
export interface UnpaidBonus {
  bonusId: string;
  staffId: string;
  staffName: string;
  amount: number;
  reason: string;
  payPeriodFrom: string;
  payPeriodTo: string;
}

export interface LockPayrollRun {
  branchId: string;
  from: string;
  to: string;
  note?: string | null;
}

export interface PayrollRunSummary {
  id: string;
  branchId: string;
  branchName?: string | null;
  from: string;
  to: string;
  status: PayrollRunStatus;
  note?: string | null;
  totalGross: number;
  totalDeductions: number;
  totalNet: number;
  lineCount: number;
  createdAtUtc: string;
  createdByName?: string | null;
  lockedAtUtc?: string | null;
  lockedByName?: string | null;
  voidedAtUtc?: string | null;
  voidedByName?: string | null;
  /** Net of lines still marked held. Omitted when none are. */
  totalHeld?: number | null;
}

/** A locked line on the run detail, including a deposit when one was recorded. */
export interface PayrollRunLine {
  staffId: string;
  staffName: string;
  net: number;
  depositedAmount?: number | null;
  discrepancy?: number | null;
}

/** A locked line whose deposit does not match net. */
export interface PayrollDepositDiscrepancy {
  staffId: string;
  staffName: string;
  net: number;
  depositedAmount: number;
  discrepancy: number;
}

export interface PayrollRunDetail {
  run: PayrollRunSummary;
  lines: PayrollRunLine[];
  /** Lines with a non-zero discrepancy. Omitted when there are none. */
  discrepancies?: PayrollDepositDiscrepancy[] | null;
}

export interface RecordPayrollDeposit {
  staffId: string;
  depositedAmount: number | null;
}

export interface PayrollDepositSaved {
  runId: string;
  staffId: string;
  staffName: string;
  net: number;
  depositedAmount: number;
  discrepancy: number;
}

export interface PayoutHold {
  id: string;
  staffId: string;
  staffName: string;
  reason: string;
  heldAtUtc: string;
  heldByName?: string | null;
  releasedOn?: string | null;
  releasedByName?: string | null;
  active: boolean;
}

export interface PayoutHoldList {
  holds: PayoutHold[];
}

export interface SetPayoutHold {
  staffId: string;
  reason: string;
}

export interface PayrollRateHistoryRow {
  id: string;
  effectiveFrom: string;
  basis: PayBasis;
  ratePhp: number;
  otAfterHoursPerDay?: number | null;
  otMultiplier?: number | null;
  createdAtUtc: string;
  createdByName?: string | null;
  reason?: string | null;
}

export interface PayrollRatesResponse {
  staffId: string;
  staffName: string;
  branchId?: string | null;
  branchName?: string | null;
  basis?: PayBasis | null;
  ratePhp?: number | null;
  otAfterHoursPerDay?: number | null;
  otMultiplier?: number | null;
  history: PayrollRateHistoryRow[];
}

export interface SetPayrollRate {
  effectiveFrom: string;
  basis: PayBasis;
  ratePhp: number;
  otAfterHoursPerDay?: number | null;
  otMultiplier?: number | null;
  reason?: string | null;
}

export interface PayrollQuery {
  from?: string;
  to?: string;
  scope?: string;
  includeInactive?: boolean;
}

export interface ThirteenthMonthRow {
  staffId: string;
  staffName: string;
  role: string;
  branch?: string | null;
  hireDate?: string | null;
  payBasis?: PayBasis | null;
  ratePhp?: number | null;
  basicEarnedAuto: number;
  basicEarnedOverride?: number | null;
  basicEarned: number;
  thirteenthMonth: number;
  usedOverride: boolean;
}

export interface ThirteenthMonthResponse {
  year: number;
  rows: ThirteenthMonthRow[];
  totalThirteenthMonth?: number;
}

export interface SaveThirteenthOverride {
  staffId: string;
  year: number;
  basicEarnedOverride: number;
}

/** One worker's payslip for one period. Locked periods carry the frozen snapshot. */
export interface Payslip {
  staffId: string;
  staffName: string;
  from: string;
  to: string;
  basis?: PayBasis | null;
  ratePhp?: number | null;
  regularHours: number;
  otHours: number;
  otPay?: number;
  daysPaid: number;
  gross: number;
  sss: number;
  philHealth: number;
  pagIbig: number;
  wht: number;
  other: number;
  deductions: number;
  net: number;
  source: 'live' | 'locked';
  lockedByName?: string | null;
  lockedAtUtc?: string | null;
  issuedAtUtc?: string | null;
  note?: string | null;
  holidayHours?: number | null;
  restDayHours?: number | null;
  nightHours?: number | null;
  premiumPay?: number | null;
  allowances?: number | null;
  allowanceItems?: PayslipAllowanceItem[] | null;
  loanDeductions?: number | null;
  loanItems?: PayslipLoanItem[] | null;
}

/** One row of the caller's payslip history: their own line from a locked run. */
export interface MyPayslipSummary {
  runId: string;
  lineId: string;
  periodFrom: string;
  periodTo: string;
  gross: number;
  deductions: number;
  net: number;
  lockedAtUtc: string;
}

/** A saved payslip line exactly as stored at lock time, with its run period and frozen items. */
export interface MyPayslipDetail {
  runId: string;
  lineId: string;
  periodFrom: string;
  periodTo: string;
  lockedAtUtc: string;
  lockedByName?: string | null;
  staffId: string;
  staffName: string;
  branchId?: string | null;
  basis?: PayBasis | null;
  ratePhp?: number | null;
  regularHours: number;
  otHours: number;
  otPay?: number | null;
  daysPaid: number;
  gross: number;
  sss: number;
  philHealth: number;
  pagIbig: number;
  wht: number;
  other: number;
  deductions: number;
  net: number;
  notes?: string | null;
  holidayHours?: number | null;
  restDayHours?: number | null;
  nightHours?: number | null;
  premiumPay?: number | null;
  allowances?: number | null;
  loanDeductions?: number | null;
  items?: MyPayslipLineItem[] | null;
}

/** One frozen line item on a saved payslip. Kind `allowance` is an allowance; kind `loan` is an instalment. */
export interface MyPayslipLineItem {
  id: string;
  kind: string;
  label: string;
  amount: number;
  taxable?: boolean | null;
  refId?: string | null;
  quantity?: number | null;
  balanceAfter?: number | null;
}

/** Wire values for AllowancePer. Period pays once; day pays for each day worked. */
export type AllowancePer = 'period' | 'day';

/** One allowance on a worker. Owner and admin, same roles as pay. */
export interface StaffAllowance {
  id: string;
  staffId: string;
  name: string;
  amount: number;
  per: AllowancePer;
  taxable: boolean;
  effectiveFrom?: string | null;
  effectiveTo?: string | null;
  note?: string | null;
  active: boolean;
  createdAtUtc: string;
}

export interface StaffAllowanceList {
  allowances: StaffAllowance[];
  suggestedNames: string[];
}

export interface SaveStaffAllowance {
  staffId: string;
  name: string;
  amount: number;
  per: AllowancePer;
  taxable?: boolean;
  effectiveFrom?: string | null;
  effectiveTo?: string | null;
  note?: string | null;
  active?: boolean;
}

export interface UpdateStaffAllowance {
  name: string;
  amount: number;
  per: AllowancePer;
  taxable?: boolean;
  effectiveFrom?: string | null;
  effectiveTo?: string | null;
  note?: string | null;
  active?: boolean;
}

/** Register allowance line. `amount` is the extended total; `per` is null on a locked snapshot. */
export interface PayrollAllowanceItem {
  allowanceId?: string | null;
  name: string;
  per?: AllowancePer | null;
  taxable: boolean;
  quantity: number;
  amount: number;
}

/** Payslip allowance line: name and amount only. */
export interface PayslipAllowanceItem {
  name: string;
  amount: number;
}

/** Wire values for StaffLoanStatus. */
export type StaffLoanStatus = 'active' | 'paid' | 'cancelled';

/** One loan instalment on a register row. `amount` is what this run takes. */
export interface PayrollLoanItem {
  loanId: string;
  kind: string;
  suggested: number;
  amount: number;
  balanceBefore: number;
  balanceAfter: number;
  capped: boolean;
}

/** Payslip loan line. `loanBalance` is the remaining balance after this instalment. */
export interface PayslipLoanItem {
  label: string;
  instalment: number;
  loanBalance?: number | null;
}

/** One loan or cash advance on a worker. Owner and admin, same roles as pay. */
export interface StaffLoan {
  id: string;
  staffId: string;
  kind: string;
  principal: number;
  perPeriodAmount: number;
  startPeriod: string;
  note?: string | null;
  createdAtUtc: string;
  cancelledAtUtc?: string | null;
  cancelReason?: string | null;
  paid: number;
  balance: number;
  status: StaffLoanStatus;
  loanKindId?: string | null;
}

export interface StaffLoanPayment {
  id: string;
  loanId: string;
  runId: string;
  staffId: string;
  periodFrom: string;
  periodTo: string;
  amount: number;
  createdAtUtc: string;
  reversedAtUtc?: string | null;
}

export interface StaffLoanDetail extends StaffLoan {
  payments: StaffLoanPayment[];
}

export interface StaffLoanList {
  loans: StaffLoan[];
  suggestedKinds: string[];
}

export interface SaveStaffLoan {
  staffId: string;
  kind: string;
  principal: number;
  perPeriodAmount: number;
  startPeriod: string;
  note?: string | null;
  loanKindId?: string | null;
}

/** One row on the loan kind list. cutoffOrdinal null means every period. */
export interface LoanKind {
  id: string;
  name: string;
  active: boolean;
  cutoffOrdinal: number | null;
}

export interface LoanKindList {
  kinds: LoanKind[];
}

export interface SaveLoanKind {
  name: string;
  active: boolean;
  cutoffOrdinal: number | null;
}

export interface UpdateStaffLoan {
  kind: string;
  perPeriodAmount: number;
  startPeriod: string;
  principal?: number | null;
  note?: string | null;
  loanKindId?: string | null;
}

export type HolidayKind = 'regular' | 'special';

export interface PayrollPremiumSettings {
  branchId: string;
  regularHolidayPercent: number;
  specialDayPercent: number;
  restDayPercent: number;
  holidayPremiumEnabled: boolean;
  restDayPremiumEnabled: boolean;
  nightDiffEnabled: boolean;
  nightDiffPercent: number;
  nightStart: string;
  nightEnd: string;
  shiftEnd: string;
  otGraceMinutes: number;
  adminsCanApproveOt: boolean;
  otPremiumPercent: number;
  otMonthlyDayDivisor: number;
  hoursPerDay: number;
}

export type PayrollCutoffMode = 'free' | 'schedule';

export interface PayrollCutoff {
  ordinal: number;
  startDay: number;
  endDay: number;
}

/** One generated period. `from` and `to` are yyyy-MM-dd. */
export interface PayrollCutoffPeriod {
  ordinal: number;
  from: string;
  to: string;
}

export interface PayrollCutoffSchedule {
  branchId: string;
  mode: PayrollCutoffMode;
  cutoffs: PayrollCutoff[];
  /** yyyy-MM the periods were generated for. */
  month: string;
  periods: PayrollCutoffPeriod[];
}

export interface UpdatePayrollCutoffSchedule {
  mode: PayrollCutoffMode;
  cutoffs: PayrollCutoff[];
}

export type DeductionScheduleMode = 'prorate' | 'cutoff' | 'split';

/** A rule for the current cut-off list. `cutoffOrdinal` is the position, not a stored period id. */
export interface DeductionScheduleRule {
  deductionKey: string;
  mode: DeductionScheduleMode;
  cutoffOrdinal?: number | null;
}

export interface DeductionSchedule {
  branchId: string;
  cutoffCount: number;
  rules: DeductionScheduleRule[];
}

export interface UpdateDeductionSchedule {
  rules: DeductionScheduleRule[];
}

/** Catch-up for one period. The period is `periodFrom`/`periodTo`, not a cut-off number. */
export interface PayrollCatchUp {
  id: string;
  staffId: string;
  periodFrom: string;
  periodTo: string;
  deductionKey: string;
  amount: number;
  note: string;
}

export interface SavePayrollCatchUp {
  staffId: string;
  periodFrom: string;
  periodTo: string;
  deductionKey: string;
  amount: number;
  note?: string | null;
}

export interface UpdatePayrollPremiumSettings {
  regularHolidayPercent: number;
  specialDayPercent: number;
  restDayPercent: number;
  holidayPremiumEnabled: boolean;
  restDayPremiumEnabled: boolean;
  nightDiffEnabled: boolean;
  nightDiffPercent: number;
  nightStart: string;
  nightEnd: string;
  shiftEnd: string;
  otGraceMinutes: number;
  adminsCanApproveOt: boolean;
  otPremiumPercent: number;
  otMonthlyDayDivisor: number;
  hoursPerDay: number;
}

export interface ShopHoliday {
  id: string;
  branchId?: string | null;
  date: string;
  name: string;
  kind: HolidayKind;
}

export interface ShopHolidayList {
  year: number;
  holidays: ShopHoliday[];
}

export interface UpsertShopHoliday {
  date: string;
  name: string;
  kind: HolidayKind;
  branchId?: string | null;
}

export interface CopyHolidayYear {
  fromYear: number;
  toYear: number;
}

export interface CopyHolidayYearResult {
  created: number;
  skipped: number;
}

export interface PayrollOverride {
  id: string;
  staffId: string;
  staffName?: string | null;
  from: string;
  to: string;
  key: string;
  amount: number;
  reason: string;
  setByStaffId?: string | null;
  setAtUtc: string;
}

export interface PayrollOverrideCleared {
  staffId: string;
  from: string;
  to: string;
  key: string;
  cleared: boolean;
}

export interface SavePayrollOverride {
  staffId: string;
  from: string;
  to: string;
  key: string;
  amount: number | null;
  reason?: string | null;
}

export interface IssuePayslip {
  staffId: string;
  from: string;
  to: string;
  note?: string | null;
}

/** Suggested statutory amounts for one worker and one calendar month. */
export interface StatutorySuggestion {
  staffId: string;
  staffName: string;
  month: string;
  base: number;
  sssMsc: number;
  sss: number;
  philHealthBase: number;
  philHealth: number;
  pagIbigBase: number;
  pagIbig: number;
  whtTaxable: number;
  wht: number;
  statutoryTotal: number;
  notes: string[];
  /** Set when the suggestion was apportioned to a register period. */
  periodFrom?: string | null;
  periodTo?: string | null;
  fraction?: number | null;
}

/** Final pay estimate for a separated worker. */
export interface PayrollFinalPay {
  staffId: string;
  staffName: string;
  separationDate: string;
  separationReason?: string | null;
  wages: { from: string; to: string; gross: number };
  thirteenthMonth: { year: number; basicEarned: number; amount: number };
  sil: {
    eligible: boolean;
    allotted: number;
    used: number;
    pending: number;
    remaining: number;
    dailyRate: number;
    amount: number;
  };
  grossTotal: number;
  notes: string[];
}

/** One agency on the monthly remittance report. Amounts are PHP. */
export interface RemittanceAgency {
  key: string;
  label: string;
  employeeShare: number;
  employerShare: number;
  total: number;
}

/** Current shop, one month. `enabled` is false until the shop turns the report on. */
export interface RemittanceReport {
  branchId: string;
  month: string;
  enabled: boolean;
  agencies: RemittanceAgency[];
  employeeShare: number;
  employerShare: number;
  total: number;
  /** Set when the report is on and no locked run ends in the month. */
  emptyMessage?: string | null;
  /** Locked runs in the month whose employer shares are not confirmed. */
  unreviewedRuns?: RemittanceRunRef[] | null;
}

/** A locked run named by its period and shop, with a status such as "Not reviewed". */
export interface RemittanceRunRef {
  runId: string;
  from: string;
  to: string;
  name: string;
  status: string;
}

export interface EmployerShareReviewLine {
  staffId: string;
  staffName: string;
  suggestedSss: number;
  suggestedPhilHealth: number;
  suggestedPagIbig: number;
  employerSss?: number | null;
  employerPhilHealth?: number | null;
  employerPagIbig?: number | null;
}

export interface EmployerShareReview {
  runId: string;
  readOnly: boolean;
  lines: EmployerShareReviewLine[];
  reviewedAtUtc?: string | null;
  reviewedByName?: string | null;
}

export interface ConfirmEmployerShareLine {
  staffId: string;
  employerSss: number;
  employerPhilHealth: number;
  employerPagIbig: number;
}

export interface RemittanceSetting {
  branchId: string;
  enabled: boolean;
}

export interface UpdateRemittanceSetting {
  enabled: boolean;
}
