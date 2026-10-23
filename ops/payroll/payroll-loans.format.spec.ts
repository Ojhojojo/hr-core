import { describe, expect, it } from 'vitest';
import {
  formatLoanPeso,
  loanDeleteAllowed,
  loanItemLabel,
  loanKindSuggestions,
  loanOverrideKey,
  loanProgress,
  loanStatusLabel,
  periodsRemaining,
  rowHasLoans,
} from './payroll-loans.format';

describe('payroll loan helpers', () => {
  it('labels active, paid, and cancelled', () => {
    expect(loanStatusLabel('active')).toBe('Active');
    expect(loanStatusLabel('paid')).toBe('Paid');
    expect(loanStatusLabel('cancelled')).toBe('Cancelled');
    expect(loanStatusLabel('ACTIVE')).toBe('Active');
    expect(loanStatusLabel('')).toBe('');
    expect(loanStatusLabel(null)).toBe('');
    expect(loanStatusLabel('mystery')).toBe('mystery');
  });

  it('counts remaining periods, then zero once the balance is gone', () => {
    expect(periodsRemaining(5000, 1000)).toBe(5);
    expect(periodsRemaining(0, 1000)).toBe(0);
    expect(periodsRemaining(-50, 1000)).toBe(0);
    expect(periodsRemaining(5000, 0)).toBe(0);
    expect(periodsRemaining(5000, -100)).toBe(0);
    expect(periodsRemaining(2500, 1000)).toBe(3);
    expect(periodsRemaining(1000, 1000)).toBe(1);
    expect(periodsRemaining(1000.01, 1000)).toBe(2);
    expect(periodsRemaining(Number.NaN, 1000)).toBe(0);
  });

  it('reports repaid progress from 0 to 1', () => {
    expect(loanProgress(5000, 5000)).toBe(0);
    expect(loanProgress(5000, 4000)).toBeCloseTo(0.2);
    expect(loanProgress(5000, 0)).toBe(1);
    expect(loanProgress(5000, -100)).toBe(1);
    expect(loanProgress(5000, 6000)).toBe(0);
    expect(loanProgress(0, 0)).toBe(0);
    expect(loanProgress(-1, 0)).toBe(0);
  });

  it('formats a compact instalment label', () => {
    expect(formatLoanPeso(1000)).toBe('₱1,000');
    expect(formatLoanPeso(1000.5)).toBe('₱1,000.50');
    expect(loanItemLabel({ kind: 'Cash advance', amount: 1000, balanceAfter: 3000 })).toBe(
      'Cash advance ₱1,000 (balance ₱3,000)',
    );
    expect(loanItemLabel({ kind: '  ', amount: 500, loanBalance: 500 })).toBe(
      'Loan ₱500 (balance ₱500)',
    );
  });

  it('uses shop kinds, or the default list when none have been used', () => {
    expect(loanKindSuggestions(null)).toEqual([
      'Cash advance',
      'SSS loan',
      'Pag-IBIG loan',
      'Company loan',
    ]);
    expect(loanKindSuggestions(['', '  '])).toEqual([
      'Cash advance',
      'SSS loan',
      'Pag-IBIG loan',
      'Company loan',
    ]);
    expect(loanKindSuggestions(['Meal loan', ' Cash advance ', 'cash advance'])).toEqual([
      'Meal loan',
      'Cash advance',
    ]);
  });

  it('builds the register override key', () => {
    expect(loanOverrideKey('loan-1')).toBe('loan:loan-1');
  });

  it('detects a register row that carries loans', () => {
    expect(rowHasLoans({})).toBe(false);
    expect(rowHasLoans({ loanDeductions: null, loanItems: [] })).toBe(false);
    expect(rowHasLoans({ loanDeductions: 0 })).toBe(true);
    expect(rowHasLoans({ loanItems: [{ loanId: 'l1' }] })).toBe(true);
  });

  it('allows delete only when no payment is known', () => {
    expect(loanDeleteAllowed({ paid: 0 })).toBe(true);
    expect(loanDeleteAllowed({ paid: 1000 })).toBe(false);
    expect(loanDeleteAllowed({ paid: 0 }, [])).toBe(true);
    expect(loanDeleteAllowed({ paid: 0 }, [{ id: 'p1' }])).toBe(false);
    expect(loanDeleteAllowed({ paid: 1000 }, [])).toBe(true);
  });
});
