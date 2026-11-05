import { PayrollCutoffPeriod } from './payroll.models';

const MONTHS = [
  'January',
  'February',
  'March',
  'April',
  'May',
  'June',
  'July',
  'August',
  'September',
  'October',
  'November',
  'December',
];

/** "November 2026" for `yyyy-MM`. The raw month is returned when it is not that shape. */
export function formatCutoffMonth(month: string): string {
  const match = /^(\d{4})-(\d{2})$/.exec(month);
  if (!match) {
    return month;
  }
  const index = Number(match[2]) - 1;
  if (index < 0 || index > 11) {
    return month;
  }
  return `${MONTHS[index]} ${match[1]}`;
}

/** Move `yyyy-MM` by whole months. January minus one is the previous December. */
export function shiftMonth(month: string, delta: number): string {
  const match = /^(\d{4})-(\d{2})$/.exec(month);
  if (!match) {
    return month;
  }
  const shifted = new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1 + delta, 1));
  const nextMonth = String(shifted.getUTCMonth() + 1).padStart(2, '0');
  return `${shifted.getUTCFullYear()}-${nextMonth}`;
}

/**
 * The period to open when the register's current range is not already one of the shop's cut-offs.
 * Returns null when the range already matches, so the register is left as it is.
 * Otherwise the period that contains `today`, or the first period.
 */
export function alignCutoffPeriod(
  from: string,
  to: string,
  periods: PayrollCutoffPeriod[],
  today: string,
): PayrollCutoffPeriod | null {
  if (periods.length === 0) {
    return null;
  }
  if (periods.some((period) => period.from === from && period.to === to)) {
    return null;
  }
  return periods.find((period) => today >= period.from && today <= period.to) ?? periods[0];
}

/** Field errors from a 400 problem-details body. Empty when the body has none. */
export function problemFieldErrors(err: unknown): Record<string, string[]> {
  if (!err || typeof err !== 'object') {
    return {};
  }
  const body = (err as { error?: unknown }).error;
  if (!body || typeof body !== 'object') {
    return {};
  }
  const errors = (body as { errors?: unknown }).errors;
  if (!errors || typeof errors !== 'object') {
    return {};
  }
  const result: Record<string, string[]> = {};
  for (const [key, value] of Object.entries(errors)) {
    if (Array.isArray(value)) {
      result[key] = value.map((item) => String(item));
    }
  }
  return result;
}

export function firstFieldError(errors: Record<string, string[]>, key: string): string {
  return errors[key]?.[0] ?? '';
}
