import { manilaToday } from '@hr-host';
import { formatDay } from '../hr-dates';
import type { LeaveBalance } from './leave.api';

const ISO_DAY = /^(\d{4})-(\d{2})-(\d{2})$/;
const MS_PER_DAY = 86_400_000;

/** Non-SIL balances with remaining days expiring within this many days are highlighted as expiring soon. */
export const LEAVE_EXPIRING_SOON_DAYS = 30;

export interface LeaveExpiryInfo {
  /** Concise expiry/cycle subtext, or null when the balance has no expiry (e.g., unpaid). */
  label: string | null;
  /** True when a non-SIL balance has remaining days expiring within 30 days. */
  expiringSoon: boolean;
  /** True when a non-SIL balance has forfeited expired days. */
  hasExpired: boolean;
}

function parseUtcDayMs(iso: string | null | undefined): number | null {
  if (!iso) {
    return null;
  }
  const match = ISO_DAY.exec(iso.trim());
  if (!match) {
    return null;
  }
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  if (month < 1 || month > 12 || day < 1 || day > 31) {
    return null;
  }
  return Date.UTC(year, month - 1, day);
}

/**
 * Formats expiry/cycle subtext for a single leave balance card.
 * PH Labor Code Art. 95: SIL (`convertibleToCash`) never forfeits — it shows its cycle end date
 * and cash-conversion note instead of an expiry warning.
 */
export function describeLeaveExpiry(
  balance: LeaveBalance,
  todayIso: string = manilaToday(),
): LeaveExpiryInfo {
  if (!balance.expiresOn) {
    return { label: null, expiringSoon: false, hasExpired: false };
  }

  const formatted = formatDay(balance.expiresOn);
  if (formatted === '—') {
    return { label: null, expiringSoon: false, hasExpired: false };
  }

  if (balance.convertibleToCash) {
    return {
      label: `Cycle ends ${formatted} · convertible to cash`,
      expiringSoon: false,
      hasExpired: false,
    };
  }

  const expired = balance.expired ?? 0;
  if (expired > 0) {
    return {
      label: `${expired} expired on ${formatted}`,
      expiringSoon: false,
      hasExpired: true,
    };
  }

  const expiresMs = parseUtcDayMs(balance.expiresOn);
  const todayMs = parseUtcDayMs(todayIso);
  const customSuffix = balance.hasCustomExpiry ? ' (custom)' : '';
  if (expiresMs !== null && todayMs !== null) {
    const daysLeft = Math.round((expiresMs - todayMs) / MS_PER_DAY);
    if (daysLeft < 0) {
      return {
        label: `Expired ${formatted}${customSuffix}`,
        expiringSoon: false,
        hasExpired: false,
      };
    }
    if (balance.remaining > 0 && daysLeft <= LEAVE_EXPIRING_SOON_DAYS) {
      const leftLabel = daysLeft === 0 ? 'today' : `${daysLeft}d left`;
      return {
        label: `Expires ${formatted} (${leftLabel})`,
        expiringSoon: true,
        hasExpired: false,
      };
    }
  }

  return {
    label: `Expires ${formatted}${customSuffix}`,
    expiringSoon: false,
    hasExpired: false,
  };
}

/**
 * Compact expiry subtext for the Team balances table. Only returns text when the cell carries
 * non-default expiry information (custom expiry, hire-anniversary cycle, expired days, or expiring soon),
 * keeping standard calendar-year tables uncluttered.
 */
export function teamExpirySubtext(
  balance: LeaveBalance,
  todayIso: string = manilaToday(),
): string | null {
  if (!balance.expiresOn) {
    return null;
  }

  const info = describeLeaveExpiry(balance, todayIso);
  if (!info.label) {
    return null;
  }

  if (info.hasExpired || info.expiringSoon) {
    return info.label;
  }

  if (balance.hasCustomExpiry) {
    return balance.convertibleToCash
      ? `Cycle ends ${formatDay(balance.expiresOn)} (custom)`
      : info.label;
  }

  if (balance.usesHireAnniversary) {
    return balance.convertibleToCash ? `Cycle ends ${formatDay(balance.expiresOn)}` : info.label;
  }

  return null;
}
