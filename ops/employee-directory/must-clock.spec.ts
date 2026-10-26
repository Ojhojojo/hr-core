import { describe, expect, it } from 'vitest';
import { mustClockAfterBasisChange, mustClockDefault } from './must-clock';

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
