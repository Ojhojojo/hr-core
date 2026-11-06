import { describe, expect, it } from 'vitest';
import {
  alignCutoffPeriod,
  firstFieldError,
  formatCutoffMonth,
  problemFieldErrors,
  shiftMonth,
} from './payroll-cutoff.format';
import { PayrollCutoffPeriod } from './payroll.models';

const november: PayrollCutoffPeriod[] = [
  { ordinal: 1, from: '2026-10-26', to: '2026-11-10' },
  { ordinal: 2, from: '2026-11-11', to: '2026-11-25' },
];

describe('payroll cut-off periods', () => {
  it('leaves a range that is already a cut-off', () => {
    expect(alignCutoffPeriod('2026-11-11', '2026-11-25', november, '2026-11-05')).toBeNull();
  });

  it('picks the cut-off that contains today', () => {
    expect(alignCutoffPeriod('2026-11-02', '2026-11-08', november, '2026-11-05')).toEqual(
      november[0],
    );
  });

  it('falls back to the first cut-off when today is in another month', () => {
    expect(alignCutoffPeriod('2026-12-01', '2026-12-07', november, '2026-12-03')).toEqual(
      november[0],
    );
  });

  it('rolls the year when the month shifts past December or January', () => {
    expect(shiftMonth('2026-12', 1)).toBe('2027-01');
    expect(shiftMonth('2026-01', -1)).toBe('2025-12');
  });

  it('names a month for the preview', () => {
    expect(formatCutoffMonth('2026-11')).toBe('November 2026');
    expect(formatCutoffMonth('nope')).toBe('nope');
  });

  it('reads field errors from problem details and ignores other bodies', () => {
    const errors = problemFieldErrors({
      error: { errors: { 'cutoffs[1].startDay': ['Day 10 is already in cut-off 1.'] } },
    });
    expect(firstFieldError(errors, 'cutoffs[1].startDay')).toBe('Day 10 is already in cut-off 1.');
    expect(problemFieldErrors({ error: { detail: 'no' } })).toEqual({});
    expect(problemFieldErrors(null)).toEqual({});
  });
});
