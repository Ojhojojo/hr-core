import { describe, expect, it } from 'vitest';
import type { LeaveBalance } from './leave.api';
import { describeLeaveExpiry, teamExpirySubtext } from './leave-expiry';

function makeBalance(overrides: Partial<LeaveBalance> = {}): LeaveBalance {
  return {
    leaveType: 'vacation',
    allotment: 10,
    used: 2,
    pending: 0,
    remaining: 8,
    expiresOn: '2026-12-31',
    expired: 0,
    convertibleToCash: false,
    usesHireAnniversary: false,
    hasCustomExpiry: false,
    ...overrides,
  };
}

describe('describeLeaveExpiry', () => {
  it('returns empty info when expiresOn is missing (e.g. unpaid leave)', () => {
    expect(
      describeLeaveExpiry(
        makeBalance({ leaveType: 'unpaid', expiresOn: null, allotment: 0, remaining: 0 }),
        '2026-06-01',
      ),
    ).toEqual({
      label: null,
      expiringSoon: false,
      hasExpired: false,
    });
  });

  it('shows cash-conversion copy for SIL and never marks it expiring soon or expired', () => {
    expect(
      describeLeaveExpiry(
        makeBalance({
          leaveType: 'service_incentive',
          allotment: 5,
          used: 1,
          remaining: 4,
          expiresOn: '2026-12-31',
          expired: 0,
          convertibleToCash: true,
        }),
        '2026-12-20',
      ),
    ).toEqual({
      label: 'Cycle ends Dec 31, 2026 · convertible to cash',
      expiringSoon: false,
      hasExpired: false,
    });
  });

  it('reports expired days when forfeited', () => {
    expect(
      describeLeaveExpiry(
        makeBalance({
          allotment: 10,
          used: 6,
          remaining: 0,
          expired: 4,
          expiresOn: '2026-06-30',
          hasCustomExpiry: true,
        }),
        '2026-07-05',
      ),
    ).toEqual({
      label: '4 expired on Jun 30, 2026',
      expiringSoon: false,
      hasExpired: true,
    });
  });

  it('flags balances expiring within 30 days when remaining days exist', () => {
    expect(describeLeaveExpiry(makeBalance(), '2026-12-01')).toEqual({
      label: 'Expires Dec 31, 2026 (30d left)',
      expiringSoon: true,
      hasExpired: false,
    });

    expect(describeLeaveExpiry(makeBalance(), '2026-12-31')).toEqual({
      label: 'Expires Dec 31, 2026 (0d left)',
      expiringSoon: true,
      hasExpired: false,
    });

    // 31 days left is not expiring soon yet.
    expect(describeLeaveExpiry(makeBalance(), '2026-11-30')).toEqual({
      label: 'Expires Dec 31, 2026',
      expiringSoon: false,
      hasExpired: false,
    });

    // Zero remaining days should not trigger an expiring-soon warning.
    expect(describeLeaveExpiry(makeBalance({ used: 10, remaining: 0 }), '2026-12-20')).toEqual({
      label: 'Expires Dec 31, 2026',
      expiringSoon: false,
      hasExpired: false,
    });
  });

  it('appends (custom) when a non-expiring-soon balance has a custom expiry', () => {
    expect(
      describeLeaveExpiry(
        makeBalance({ expiresOn: '2026-09-30', hasCustomExpiry: true }),
        '2026-05-01',
      ),
    ).toEqual({
      label: 'Expires Sep 30, 2026 (custom)',
      expiringSoon: false,
      hasExpired: false,
    });
  });
});

describe('teamExpirySubtext', () => {
  it('returns null for standard calendar-year balances more than 30 days away', () => {
    expect(teamExpirySubtext(makeBalance(), '2026-06-01')).toBeNull();
  });

  it('returns compact subtext for custom expiry, hire anniversary, expired, and expiring-soon balances', () => {
    expect(
      teamExpirySubtext(
        makeBalance({ expiresOn: '2026-09-30', hasCustomExpiry: true }),
        '2026-05-01',
      ),
    ).toBe('Expires Sep 30, 2026 (custom)');

    expect(
      teamExpirySubtext(
        makeBalance({ expiresOn: '2027-07-14', usesHireAnniversary: true }),
        '2026-08-01',
      ),
    ).toBe('Expires Jul 14, 2027');

    expect(
      teamExpirySubtext(
        makeBalance({
          leaveType: 'service_incentive',
          expiresOn: '2027-07-14',
          convertibleToCash: true,
          usesHireAnniversary: true,
        }),
        '2026-08-01',
      ),
    ).toBe('Cycle ends Jul 14, 2027');

    expect(
      teamExpirySubtext(
        makeBalance({ remaining: 0, expired: 3, expiresOn: '2026-06-30' }),
        '2026-07-10',
      ),
    ).toBe('3 expired on Jun 30, 2026');

    expect(teamExpirySubtext(makeBalance(), '2026-12-20')).toBe('Expires Dec 31, 2026 (11d left)');
  });
});
