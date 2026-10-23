/** COLA / allowance display and activity helpers. Pure; no Angular. */

export type AllowancePer = 'period' | 'day';

/** Shown in the name field when the shop has not used any allowance names yet. */
export const ALLOWANCE_SEED_NAMES = ['COLA', 'Rice', 'Transport'] as const;

export interface AllowanceActivityWindow {
  active?: boolean | null;
  effectiveFrom?: string | null;
  effectiveTo?: string | null;
}

/** Line the label helper formats. `amount` is the rate, not the extended total. */
export interface AllowanceLineItem {
  name: string;
  amount: number;
  per: AllowancePer | 'Period' | 'Day' | string;
  /** Days the daily rate was applied. Ignored for a flat period amount. */
  days?: number | null;
}

export function normalizeAllowancePer(per: string | null | undefined): AllowancePer {
  return (per ?? '').trim().toLowerCase() === 'day' ? 'day' : 'period';
}

export function allowancePerLabel(per: AllowancePer | 'Period' | 'Day' | string): string {
  return normalizeAllowancePer(per) === 'day' ? 'per day' : 'per period';
}

/** Shop names already in use, or the seed list when that list is empty. */
export function allowanceNameSuggestions(
  suggested: readonly string[] | null | undefined,
): string[] {
  const names = (suggested ?? []).map((name) => name.trim()).filter((name) => name.length > 0);
  return names.length > 0 ? names : [...ALLOWANCE_SEED_NAMES];
}

/** e.g. "Rice ₱50 × 3 days" or "COLA ₱500 per period". */
export function allowanceItemLabel(item: AllowanceLineItem): string {
  const money = formatAllowancePeso(item.amount);
  if (normalizeAllowancePer(item.per) === 'day') {
    const days = finite(item.days);
    const word = days === 1 ? 'day' : 'days';
    return `${item.name} ${money} × ${formatCount(days)} ${word}`;
  }
  return `${item.name} ${money} ${allowancePerLabel('period')}`;
}

/** Period pays the amount once. Day pays amount × days worked. Rounded to cents. */
export function estimateAllowance(
  amount: number,
  per: AllowancePer | 'Period' | 'Day' | string,
  daysWorked: number,
): number {
  const rate = finite(amount);
  if (normalizeAllowancePer(per) === 'day') {
    return roundMoney(rate * finite(daysWorked));
  }
  return roundMoney(rate);
}

/**
 * True when the allowance is switched on and `date` falls inside the effective window.
 * A missing bound is open. Both ends are inclusive. Dates compare on the yyyy-MM-dd prefix.
 */
export function isAllowanceActiveOn(allowance: AllowanceActivityWindow, date: string): boolean {
  if (allowance.active === false) {
    return false;
  }
  const day = datePrefix(date);
  if (!day) {
    return false;
  }
  const from = allowance.effectiveFrom ? datePrefix(allowance.effectiveFrom) : '';
  const to = allowance.effectiveTo ? datePrefix(allowance.effectiveTo) : '';
  if (from && to && from > to) {
    return false;
  }
  if (from && day < from) {
    return false;
  }
  if (to && day > to) {
    return false;
  }
  return true;
}

/** True when the register row has an allowance total, items, or an edited amount. */
export function rowHasAllowances(row: {
  allowances?: number | null;
  allowanceItems?: readonly unknown[] | null;
  allowancesOverridden?: boolean | null;
}): boolean {
  return (
    row.allowances != null ||
    (row.allowanceItems?.length ?? 0) > 0 ||
    row.allowancesOverridden === true
  );
}

/** Compact peso for allowance labels: ₱50, ₱50.50, ₱1,500. */
export function formatAllowancePeso(amount: number): string {
  const value = finite(amount);
  const negative = value < 0;
  const abs = Math.abs(value);
  const cents = Math.round(abs * 100) % 100;
  const text = abs.toLocaleString('en-US', {
    minimumFractionDigits: cents === 0 ? 0 : 2,
    maximumFractionDigits: 2,
  });
  return negative ? `-₱${text}` : `₱${text}`;
}

function finite(value: number | null | undefined): number {
  const amount = Number(value ?? 0);
  return Number.isFinite(amount) ? amount : 0;
}

function formatCount(value: number): string {
  if (Number.isInteger(value)) {
    return String(value);
  }
  return String(roundMoney(value));
}

/** Two-decimal money, matching API MidpointRounding.AwayFromZero for non-negative amounts. */
function roundMoney(value: number): number {
  return Math.round((value + Number.EPSILON) * 100) / 100;
}

function datePrefix(value: string): string {
  const match = /^(\d{4}-\d{2}-\d{2})/.exec(value.trim());
  return match?.[1] ?? '';
}
