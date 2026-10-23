import { PayrollStaffRow } from './payroll.models';

/** Weekday labels. Index 0 is Sunday, matching `Staff.RestDays`. */
export const WEEKDAY_LABELS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'] as const;

export type PremiumKind = 'regular' | 'special';

/** "Regular holiday" / "Special day". */
export function premiumLabel(kind: string | null | undefined): string {
  const key = (kind ?? '').trim().toLowerCase();
  if (key === 'regular') {
    return 'Regular holiday';
  }
  if (key === 'special') {
    return 'Special day';
  }
  const raw = (kind ?? '').trim();
  return raw || '—';
}

/** "Sun, Sat", or "—" when no rest days are set. Days are listed Sunday-first. */
export function restDaysLabel(days: readonly number[] | null | undefined): string {
  if (!days || days.length === 0) {
    return '—';
  }
  const labels = [...new Set(days)]
    .filter((day) => Number.isInteger(day) && day >= 0 && day <= 6)
    .sort((left, right) => left - right)
    .map((day) => WEEKDAY_LABELS[day]);
  return labels.length > 0 ? labels.join(', ') : '—';
}

/** `200` -> "200%". Non-finite values become "—". */
export function percentToMultiplierLabel(percent: number | null | undefined): string {
  const value = Number(percent);
  if (!Number.isFinite(value)) {
    return '—';
  }
  return `${value}%`;
}

/** True when the register row carries any premium figure (including an explicit zero). */
export function rowHasPremium(
  row: Pick<PayrollStaffRow, 'holidayHours' | 'restDayHours' | 'nightHours' | 'premiumPay'>,
): boolean {
  return (
    row.holidayHours != null ||
    row.restDayHours != null ||
    row.nightHours != null ||
    row.premiumPay != null
  );
}

/** Sum of non-null premium pay, rounded to cents. Null rows contribute nothing. */
export function sumPremiums(rows: readonly Pick<PayrollStaffRow, 'premiumPay'>[]): number {
  const total = rows.reduce((sum, row) => {
    if (row.premiumPay == null) {
      return sum;
    }
    const amount = Number(row.premiumPay);
    return Number.isFinite(amount) ? sum + amount : sum;
  }, 0);
  return Math.round(total * 100) / 100;
}

/** Owner edits holiday and premium settings. Admin can read them. */
export function canEditPremiumSettings(role: string | null | undefined): boolean {
  return role === 'owner';
}
