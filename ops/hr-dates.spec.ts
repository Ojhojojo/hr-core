import { afterEach, describe, expect, it, vi } from 'vitest';
import { formatDay, formatDayPrefix, formatDayRange, toIsoDate } from './hr-dates';

/**
 * Forces UTC getters and `toISOString` onto another calendar day than the local
 * getters, so a `toISOString().slice(0, 10)` regression fails in UTC too.
 */
function disagreeUtc(local: Date, utcIso: string): Date {
  const utc = new Date(utcIso);
  vi.spyOn(local, 'toISOString').mockReturnValue(utcIso);
  vi.spyOn(local, 'getUTCFullYear').mockReturnValue(utc.getUTCFullYear());
  vi.spyOn(local, 'getUTCMonth').mockReturnValue(utc.getUTCMonth());
  vi.spyOn(local, 'getUTCDate').mockReturnValue(utc.getUTCDate());
  vi.spyOn(local, 'getUTCDay').mockReturnValue(utc.getUTCDay());
  vi.spyOn(local, 'getUTCHours').mockReturnValue(utc.getUTCHours());
  vi.spyOn(local, 'getUTCMinutes').mockReturnValue(utc.getUTCMinutes());
  vi.spyOn(local, 'getUTCSeconds').mockReturnValue(utc.getUTCSeconds());
  vi.spyOn(local, 'getUTCMilliseconds').mockReturnValue(utc.getUTCMilliseconds());
  return local;
}

describe('toIsoDate', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('uses the local calendar day and leaves an iso day unchanged', () => {
    expect(toIsoDate(new Date(2026, 0, 5))).toBe('2026-01-05');
    expect(toIsoDate(new Date(2026, 0, 5, 23, 30))).toBe('2026-01-05');
    expect(toIsoDate('2026-01-05')).toBe('2026-01-05');
    expect(toIsoDate(null)).toBeNull();
    expect(toIsoDate(undefined)).toBeNull();
    expect(toIsoDate('')).toBeNull();
  });

  it('keeps the local day when toISOString and UTC getters report another day', () => {
    const midnight = disagreeUtc(new Date(2026, 0, 5), '2026-01-04T16:00:00.000Z');
    const late = disagreeUtc(new Date(2026, 0, 5, 23, 30), '2026-01-06T07:30:00.000Z');

    expect(toIsoDate(midnight)).toBe('2026-01-05');
    expect(toIsoDate(late)).toBe('2026-01-05');
    expect(midnight.toISOString).not.toHaveBeenCalled();
    expect(late.toISOString).not.toHaveBeenCalled();
    expect(vi.mocked(midnight.toISOString).getMockImplementation()?.()).toBe(
      '2026-01-04T16:00:00.000Z',
    );
    expect(vi.mocked(late.toISOString).getMockImplementation()?.()).toBe(
      '2026-01-06T07:30:00.000Z',
    );
  });
});

describe('hr dates', () => {
  it('formats a day and falls back to an em dash', () => {
    expect(formatDay('2026-10-03')).toBe('Oct 3, 2026');
    expect(formatDay('2026-10-29T01:30:00Z')).toBe('Oct 29, 2026');
    expect(formatDay('2026-10-29T20:00:00Z')).toBe('Oct 30, 2026');
    expect(formatDay(null)).toBe('—');
    expect(formatDay('soon')).toBe('—');
  });

  it('formats ranges compactly', () => {
    expect(formatDayRange('2026-11-10', '2026-11-10')).toBe('Nov 10, 2026');
    expect(formatDayRange('2026-10-26', '2026-10-28')).toBe('Oct 26 – 28, 2026');
    expect(formatDayRange('2026-10-30', '2026-11-02')).toBe('Oct 30 – Nov 2, 2026');
    expect(formatDayRange('2026-12-30', '2027-01-02')).toBe('Dec 30, 2026 – Jan 2, 2027');
    expect(formatDayRange('2026-10-26', null)).toBe('Oct 26, 2026');
  });

  it('rewrites a leading day in API text', () => {
    expect(formatDayPrefix('2026-10-29 4:45 PM')).toBe('Oct 29, 2026, 4:45 PM');
    expect(formatDayPrefix('4:45 PM')).toBe('4:45 PM');
    expect(formatDayPrefix(null)).toBe('—');
  });
});
