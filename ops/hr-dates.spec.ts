import { describe, expect, it } from 'vitest';
import { formatDay, formatDayPrefix, formatDayRange } from './hr-dates';

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
