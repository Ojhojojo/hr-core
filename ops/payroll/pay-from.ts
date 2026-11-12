import { addDays } from '@hr-host';
import { manilaWeekRange } from '../attendance/attendance';
import { formatDay, formatDayRange } from '../hr-dates';

export interface PayFromDateResponse {
  payFromDate: string | null;
  hasRun: boolean;
}

/**
 * Period used when a payroll run is opened and the dates are left alone.
 * To stays on the usual Monday–Sunday week. From moves up to the pay-from date
 * when that week would start earlier.
 */
export function defaultPayrollPeriod(
  today: string,
  payFrom: string | null | undefined,
): { from: string; to: string } {
  const week = manilaWeekRange(today);
  if (!payFrom || week.from >= payFrom) {
    return week;
  }
  if (payFrom <= week.to) {
    return { from: payFrom, to: week.to };
  }
  return { from: payFrom, to: manilaWeekRange(payFrom).to };
}

/**
 * Warning when From is before the shop's pay-from date. Null when there is nothing to warn about.
 * Does not change the period or the pay.
 */
export function earlyPayFromWarning(
  periodFrom: string,
  periodTo: string,
  payFrom: string | null | undefined,
): string | null {
  if (!payFrom || !periodFrom || periodFrom >= payFrom) {
    return null;
  }

  const dayBefore = addDays(payFrom, -1);
  const earlyTo = periodTo && periodTo < dayBefore ? periodTo : dayBefore;
  if (!earlyTo || earlyTo < periodFrom) {
    return null;
  }

  return (
    `This period starts before ${formatDay(payFrom)}, the first day this system should pay. ` +
    `Monthly staff will be paid for ${formatDayRange(periodFrom, earlyTo)} even with no clock data.`
  );
}
