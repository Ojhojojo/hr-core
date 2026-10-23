import { describe, expect, it } from 'vitest';
import {
  allowanceItemLabel,
  allowanceNameSuggestions,
  allowancePerLabel,
  estimateAllowance,
  isAllowanceActiveOn,
  rowHasAllowances,
} from './payroll-allowances.format';

describe('payroll allowance helpers', () => {
  it('labels period and day', () => {
    expect(allowancePerLabel('Period')).toBe('per period');
    expect(allowancePerLabel('Day')).toBe('per day');
    expect(allowancePerLabel('period')).toBe('per period');
    expect(allowancePerLabel('day')).toBe('per day');
  });

  it('seeds COLA, Rice, and Transport only when the shop list is empty', () => {
    expect(allowanceNameSuggestions([])).toEqual(['COLA', 'Rice', 'Transport']);
    expect(allowanceNameSuggestions(null)).toEqual(['COLA', 'Rice', 'Transport']);
    expect(allowanceNameSuggestions(['  ', ''])).toEqual(['COLA', 'Rice', 'Transport']);
    expect(allowanceNameSuggestions(['Meal', ' Rice '])).toEqual(['Meal', 'Rice']);
  });

  it('formats a daily line as name, rate, and days', () => {
    expect(allowanceItemLabel({ name: 'Rice', amount: 50, per: 'Day', days: 3 })).toBe(
      'Rice ₱50 × 3 days',
    );
    expect(allowanceItemLabel({ name: 'Rice', amount: 50, per: 'day', days: 1 })).toBe(
      'Rice ₱50 × 1 day',
    );
    expect(allowanceItemLabel({ name: 'Rice', amount: 50.5, per: 'day', days: 2 })).toBe(
      'Rice ₱50.50 × 2 days',
    );
  });

  it('formats a period line as a flat amount', () => {
    expect(allowanceItemLabel({ name: 'COLA', amount: 500, per: 'Period' })).toBe(
      'COLA ₱500 per period',
    );
    expect(allowanceItemLabel({ name: 'Transport', amount: 1500, per: 'period', days: 4 })).toBe(
      'Transport ₱1,500 per period',
    );
  });

  it('estimates a period amount once and a daily amount times days worked', () => {
    expect(estimateAllowance(500, 'Period', 3)).toBe(500);
    expect(estimateAllowance(50, 'Day', 3)).toBe(150);
    expect(estimateAllowance(50, 'day', 0)).toBe(0);
    expect(estimateAllowance(10.005, 'period', 9)).toBe(10.01);
    expect(estimateAllowance(33.335, 'day', 1)).toBe(33.34);
  });

  it('is active on a date inside an inclusive window', () => {
    const row = { effectiveFrom: '2026-10-01', effectiveTo: '2026-10-15' };
    expect(isAllowanceActiveOn(row, '2026-10-01')).toBe(true);
    expect(isAllowanceActiveOn(row, '2026-10-15')).toBe(true);
    expect(isAllowanceActiveOn(row, '2026-10-10T16:00:00Z')).toBe(true);
    expect(isAllowanceActiveOn(row, '2026-09-30')).toBe(false);
    expect(isAllowanceActiveOn(row, '2026-10-16')).toBe(false);
  });

  it('treats a missing bound as open and an inactive row as off', () => {
    expect(isAllowanceActiveOn({ effectiveFrom: '2026-10-01' }, '2026-12-01')).toBe(true);
    expect(isAllowanceActiveOn({ effectiveTo: '2026-10-01' }, '2026-01-01')).toBe(true);
    expect(isAllowanceActiveOn({}, '2026-10-10')).toBe(true);
    expect(isAllowanceActiveOn({ active: false, effectiveFrom: '2026-01-01' }, '2026-10-10')).toBe(
      false,
    );
    expect(isAllowanceActiveOn({ active: true }, '2026-10-10')).toBe(true);
    expect(
      isAllowanceActiveOn({ effectiveFrom: '2026-10-20', effectiveTo: '2026-10-01' }, '2026-10-15'),
    ).toBe(false);
    expect(isAllowanceActiveOn({}, '')).toBe(false);
  });

  it('detects a register row that carries allowances', () => {
    expect(rowHasAllowances({})).toBe(false);
    expect(rowHasAllowances({ allowances: null, allowanceItems: [] })).toBe(false);
    expect(rowHasAllowances({ allowances: 0 })).toBe(true);
    expect(rowHasAllowances({ allowanceItems: [{ name: 'Rice' }] })).toBe(true);
    expect(rowHasAllowances({ allowancesOverridden: true })).toBe(true);
  });
});
