import { describe, expect, it } from 'vitest';
import { mustClockAfterBasisChange, mustClockAfterRoleChange, mustClockDefault } from './must-clock';

const shopRule = (role: string) => role === 'technician' || role === 'admin';

describe('mustClockDefault', () => {
  it('follows the app rule for non-owner roles', () => {
    expect(mustClockDefault('technician', shopRule)).toBe(true);
    expect(mustClockDefault('admin', shopRule)).toBe(true);
    expect(mustClockDefault('sales', shopRule)).toBe(false);
  });

  it('is always off for the owner, even if the app says otherwise', () => {
    expect(mustClockDefault('owner', () => true)).toBe(false);
  });

  it('uses the host rule for a clocking role, a non-clocking role, and the owner', () => {
    const clocks = (role: string) => role === 'engineer' || role === 'crew' || role === 'foreman';
    expect(mustClockDefault('engineer', clocks)).toBe(true);
    expect(mustClockDefault('accounting', clocks)).toBe(false);
    expect(mustClockDefault('owner', clocks)).toBe(false);
  });
});

describe('mustClockAfterRoleChange', () => {
  const clocks = (role: string) => role === 'engineer' || role === 'technician';

  it('follows the default for a new person who has not touched the clock', () => {
    expect(
      mustClockAfterRoleChange('engineer', {
        isNewPerson: true,
        mustClockTouched: false,
        current: false,
        payBasis: null,
        clocksByDefault: clocks,
      }),
    ).toBe(true);
    expect(
      mustClockAfterRoleChange('accounting', {
        isNewPerson: true,
        mustClockTouched: false,
        current: true,
        payBasis: null,
        clocksByDefault: clocks,
      }),
    ).toBe(false);
    expect(
      mustClockAfterRoleChange('owner', {
        isNewPerson: true,
        mustClockTouched: false,
        current: true,
        payBasis: null,
        clocksByDefault: () => true,
      }),
    ).toBe(false);
  });

  it('keeps a manual override when the role changes, and still forces the owner off', () => {
    expect(
      mustClockAfterRoleChange('sales', {
        isNewPerson: true,
        mustClockTouched: true,
        current: true,
        payBasis: null,
        clocksByDefault: () => false,
      }),
    ).toBe(true);
    expect(
      mustClockAfterRoleChange('owner', {
        isNewPerson: true,
        mustClockTouched: true,
        current: true,
        payBasis: null,
        clocksByDefault: () => true,
      }),
    ).toBe(false);
  });

  it('leaves an existing person alone unless the role becomes owner', () => {
    expect(
      mustClockAfterRoleChange('accounting', {
        isNewPerson: false,
        mustClockTouched: false,
        current: true,
        payBasis: null,
        clocksByDefault: () => false,
      }),
    ).toBe(true);
    expect(
      mustClockAfterRoleChange('owner', {
        isNewPerson: false,
        mustClockTouched: false,
        current: true,
        payBasis: 'daily',
        clocksByDefault: () => true,
      }),
    ).toBe(false);
  });
});

describe('mustClockAfterBasisChange', () => {
  it('unticks Must clock when monthly pay is chosen', () => {
    expect(mustClockAfterBasisChange(true, 'monthly')).toBe(false);
    expect(mustClockAfterBasisChange(false, 'monthly')).toBe(false);
  });

  it('leaves Must clock alone for hourly, daily and no pay basis', () => {
    expect(mustClockAfterBasisChange(true, 'hourly')).toBe(true);
    expect(mustClockAfterBasisChange(true, 'daily')).toBe(true);
    expect(mustClockAfterBasisChange(false, null)).toBe(false);
    expect(mustClockAfterBasisChange(true, null)).toBe(true);
  });
});
