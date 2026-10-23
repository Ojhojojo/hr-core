import { describe, expect, it } from 'vitest';
import {
  canEditPremiumSettings,
  percentToMultiplierLabel,
  premiumLabel,
  restDaysLabel,
  rowHasPremium,
  sumPremiums,
} from './payroll-premiums.format';
import { PayrollStaffRow } from './payroll.models';

function row(partial: Partial<PayrollStaffRow>): PayrollStaffRow {
  return {
    staffId: 's',
    staffName: 'A',
    role: 'technician',
    regularHours: 8,
    otHours: 0,
    daysPaid: 1,
    amount: 100,
    ...partial,
  };
}

describe('payroll premium labels', () => {
  it('names holiday kinds', () => {
    expect(premiumLabel('regular')).toBe('Regular holiday');
    expect(premiumLabel('special')).toBe('Special day');
    expect(premiumLabel('REGULAR')).toBe('Regular holiday');
    expect(premiumLabel('')).toBe('—');
    expect(premiumLabel(null)).toBe('—');
  });

  it('lists rest days Sunday-first, or an em dash when unset', () => {
    expect(restDaysLabel([0, 6])).toBe('Sun, Sat');
    expect(restDaysLabel([6, 0])).toBe('Sun, Sat');
    expect(restDaysLabel([1])).toBe('Mon');
    expect(restDaysLabel(null)).toBe('—');
    expect(restDaysLabel(undefined)).toBe('—');
    expect(restDaysLabel([])).toBe('—');
    expect(restDaysLabel([9, -1])).toBe('—');
  });

  it('formats a percent multiplier', () => {
    expect(percentToMultiplierLabel(200)).toBe('200%');
    expect(percentToMultiplierLabel(10.5)).toBe('10.5%');
    expect(percentToMultiplierLabel(Number.NaN)).toBe('—');
  });

  it('detects a premium row only when a premium field is non-null', () => {
    expect(rowHasPremium(row({}))).toBe(false);
    expect(
      rowHasPremium(
        row({ holidayHours: null, restDayHours: null, nightHours: null, premiumPay: null }),
      ),
    ).toBe(false);
    expect(rowHasPremium(row({ premiumPay: 0 }))).toBe(true);
    expect(rowHasPremium(row({ nightHours: 4 }))).toBe(true);
    expect(rowHasPremium(row({ holidayHours: 8 }))).toBe(true);
    expect(rowHasPremium(row({ restDayHours: 8 }))).toBe(true);
  });

  it('sums non-null premium pay and skips blanks', () => {
    expect(
      sumPremiums([
        row({ premiumPay: 800 }),
        row({ premiumPay: null }),
        row({}),
        row({ premiumPay: 125.5 }),
      ]),
    ).toBe(925.5);
    expect(sumPremiums([])).toBe(0);
    expect(sumPremiums([row({ premiumPay: null })])).toBe(0);
  });

  it('lets only the owner edit premium settings', () => {
    expect(canEditPremiumSettings('owner')).toBe(true);
    expect(canEditPremiumSettings('admin')).toBe(false);
    expect(canEditPremiumSettings('technician')).toBe(false);
    expect(canEditPremiumSettings('sales')).toBe(false);
    expect(canEditPremiumSettings(null)).toBe(false);
    expect(canEditPremiumSettings(undefined)).toBe(false);
  });
});
