import { PayBasis, PayrollRateSlice, PayrollRegisterSource } from './payroll.models';

const LOCK_DATE = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'Asia/Manila',
  day: 'numeric',
  month: 'short',
  year: 'numeric',
});

/** Manila calendar date of a lock, e.g. "7 Oct 2026". Empty when the timestamp is missing. */
export function manilaLockDate(iso: string | null | undefined): string {
  if (!iso) {
    return '';
  }

  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return iso.slice(0, 10);
  }

  return LOCK_DATE.format(date);
}

/**
 * Header line for a locked period, e.g. "Locked 7 Oct 2026 by Owner Apple".
 * Empty for a live register so callers can render it unconditionally.
 */
export function payrollLockLine(
  source: PayrollRegisterSource | null | undefined,
  lockedAtUtc: string | null | undefined,
  lockedByName: string | null | undefined,
): string {
  if (source !== 'locked') {
    return '';
  }

  const when = manilaLockDate(lockedAtUtc);
  const who = (lockedByName ?? '').trim();
  if (when && who) {
    return `Locked ${when} by ${who}`;
  }
  if (when) {
    return `Locked ${when}`;
  }
  if (who) {
    return `Locked by ${who}`;
  }
  return 'Locked';
}

/**
 * CSV body with the lock line as the first row. The register CSV is built by the API, which has no
 * knowledge of the frozen run, so the ops UI prepends the lock line for a locked period.
 */
export async function csvTextWithLockLine(blob: Blob, lockLine: string): Promise<Blob> {
  if (!lockLine.trim()) {
    return blob;
  }

  const text = await blob.text();
  return new Blob([`${lockLine}\n${text}`], { type: blob.type || 'text/csv;charset=utf-8' });
}

/** Readable one-liners for the rate spans of a mid-period rate change. */
export function rateSliceLabels(slices: PayrollRateSlice[] | null | undefined): string[] {
  return (slices ?? []).map((slice) => {
    const parts = [`${slice.from} to ${slice.to}`, basisLabel(slice.basis), money(slice.ratePhp)];
    if (slice.otAfterHoursPerDay != null) {
      parts.push(`OT after ${trimNumber(slice.otAfterHoursPerDay)}h`);
    }
    if (slice.otMultiplier != null) {
      parts.push(`×${trimNumber(slice.otMultiplier)}`);
    }
    return parts.join(' · ');
  });
}

export function basisLabel(basis: PayBasis | null | undefined): string {
  if (!basis) {
    return '—';
  }
  return basis.charAt(0).toUpperCase() + basis.slice(1);
}

export function money(amount: number | null | undefined): string {
  const value = Number(amount ?? 0);
  const safe = Number.isFinite(value) ? value : 0;
  return `₱${safe.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

function trimNumber(value: number): string {
  return Number(value)
    .toFixed(2)
    .replace(/\.?0+$/, '');
}

/**
 * Warning added to the lock confirm when people have no clock-out in the period. Locking is still
 * allowed; the warning says those days won't be paid. Empty when nobody is open.
 */
export function openPunchWarning(names: readonly string[]): string {
  const unique = [...new Set(names.map((name) => name.trim()).filter((name) => name.length > 0))];
  if (unique.length === 0) {
    return '';
  }

  return `These people have no clock-out in this period and won't be paid for those days: ${unique.join(', ')}.`;
}
