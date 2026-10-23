import { MyPayslipDetail, Payslip } from '../payroll/payroll.models';

/**
 * Maps a saved payslip line (the /me/payslips/{lineId} detail, frozen at lock time) into the
 * Payslip shape the payslip display and PDF builder already consume. The saved line is never
 * recalculated: allowance and loan line items keep exactly their stored amounts.
 */
export function myPayslipDetailToPayslip(detail: MyPayslipDetail): Payslip {
  const items = detail.items ?? [];
  const allowanceItems = items
    .filter((item) => item?.kind === 'allowance' && item.label)
    .map((item) => ({ name: item.label, amount: item.amount }));
  const loanItems = items
    .filter((item) => item?.kind === 'loan' && item.label)
    .map((item) => ({
      label: item.label,
      instalment: item.amount,
      loanBalance: item.balanceAfter ?? null,
    }));
  return {
    staffId: detail.staffId,
    staffName: detail.staffName,
    from: detail.periodFrom,
    to: detail.periodTo,
    basis: detail.basis ?? null,
    ratePhp: detail.ratePhp ?? null,
    regularHours: detail.regularHours,
    otHours: detail.otHours,
    otPay: detail.otPay ?? undefined,
    daysPaid: detail.daysPaid,
    gross: detail.gross,
    sss: detail.sss,
    philHealth: detail.philHealth,
    pagIbig: detail.pagIbig,
    wht: detail.wht,
    other: detail.other,
    deductions: detail.deductions,
    net: detail.net,
    source: 'locked',
    lockedByName: detail.lockedByName ?? null,
    lockedAtUtc: detail.lockedAtUtc,
    holidayHours: detail.holidayHours ?? null,
    restDayHours: detail.restDayHours ?? null,
    nightHours: detail.nightHours ?? null,
    premiumPay: detail.premiumPay ?? null,
    allowances: detail.allowances ?? null,
    allowanceItems: allowanceItems.length > 0 ? allowanceItems : null,
    loanDeductions: detail.loanDeductions ?? null,
    loanItems: loanItems.length > 0 ? loanItems : null,
  };
}
