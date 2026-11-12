import { describe, expect, it } from 'vitest';
import { formatDay, formatDayRange } from '../hr-dates';
import { defaultPayrollPeriod, earlyPayFromWarning } from './pay-from';

describe('defaultPayrollPeriod', () => {
  it('starts the first week on the pay-from date and keeps that week end', () => {
    // Thu 12 Nov 2026 is in the week Mon 9 – Sun 15. Pay-from Sun 15 moves From only.
    expect(defaultPayrollPeriod('2026-11-12', '2026-11-15')).toEqual({
      from: '2026-11-15',
      to: '2026-11-15',
    });
  });

  it('leaves a later week alone', () => {
    // Tue 1 Dec 2026 is in Mon 30 Nov – Sun 6 Dec, which already starts after 15 Nov.
    expect(defaultPayrollPeriod('2026-12-01', '2026-11-15')).toEqual({
      from: '2026-11-30',
      to: '2026-12-06',
    });
  });

  it('keeps the usual week when pay-from is unset or already passed', () => {
    expect(defaultPayrollPeriod('2026-11-12', null)).toEqual({
      from: '2026-11-09',
      to: '2026-11-15',
    });
    expect(defaultPayrollPeriod('2026-11-12', '2026-11-01')).toEqual({
      from: '2026-11-09',
      to: '2026-11-15',
    });
  });

  it('uses the week that contains a pay-from date after this week', () => {
    // Tue 1 Dec 2026 falls in the week ending Sun 6 Dec. From is the pay-from date, not 30 Nov.
    expect(defaultPayrollPeriod('2026-11-12', '2026-12-01')).toEqual({
      from: '2026-12-01',
      to: '2026-12-06',
    });
  });
});

describe('earlyPayFromWarning', () => {
  it('names the calendar days before the pay-from date', () => {
    expect(earlyPayFromWarning('2026-11-01', '2026-11-30', '2026-11-15')).toBe(
      `This period starts before ${formatDay('2026-11-15')}, the first day this system should pay. ` +
        `Monthly staff will be paid for ${formatDayRange('2026-11-01', '2026-11-14')} even with no clock data.`,
    );
  });

  it('is quiet when From is on or after the pay-from date, or the date is unset', () => {
    expect(earlyPayFromWarning('2026-11-15', '2026-11-30', '2026-11-15')).toBeNull();
    expect(earlyPayFromWarning('2026-12-01', '2026-12-15', '2026-11-15')).toBeNull();
    expect(earlyPayFromWarning('2026-11-01', '2026-11-30', null)).toBeNull();
  });
});
