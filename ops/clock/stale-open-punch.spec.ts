import { describe, expect, it } from 'vitest';
import { isStaleOpenPunch, staleKioskMessage, staleSelfMessage } from './stale-open-punch';

// All times UTC; Manila is UTC+8.
const at = (iso: string) => new Date(iso);

describe('isStaleOpenPunch (#685)', () => {
  it('flags a clock-in left open from days ago', () => {
    // Oct 29 4:45 PM Manila, checked Nov 12 noon Manila.
    expect(isStaleOpenPunch('2026-10-29T08:45:00Z', at('2026-11-12T04:00:00Z'))).toBe(true);
  });

  it('flags yesterday morning once it has been open more than 16 hours', () => {
    // Nov 11 8:00 AM Manila, checked Nov 12 9:00 AM Manila (25 hours).
    expect(isStaleOpenPunch('2026-11-11T00:00:00Z', at('2026-11-12T01:00:00Z'))).toBe(true);
  });

  it('keeps a night shift across midnight normal', () => {
    // Nov 11 10:00 PM Manila, checked Nov 12 6:00 AM Manila (8 hours).
    expect(isStaleOpenPunch('2026-11-11T14:00:00Z', at('2026-11-11T22:00:00Z'))).toBe(false);
  });

  it('keeps a clock-in from today normal, however long', () => {
    // Nov 12 1:00 AM Manila, checked Nov 12 11:00 PM Manila.
    expect(isStaleOpenPunch('2026-11-11T17:00:00Z', at('2026-11-12T15:00:00Z'))).toBe(false);
  });

  it('ignores a missing or unreadable time', () => {
    expect(isStaleOpenPunch(null)).toBe(false);
    expect(isStaleOpenPunch('not a date')).toBe(false);
  });
});

describe('stale clock-in messages', () => {
  it('names the date and what to do next', () => {
    expect(staleKioskMessage('Int QA Tech', 'Oct 29, 4:45 PM')).toBe(
      'Int QA Tech still has an open clock-in from Oct 29, 4:45 PM. Close it on Attendance with the real time, then clock them in.',
    );
    expect(staleSelfMessage('Oct 29, 4:45 PM')).toContain('Oct 29, 4:45 PM is still open');
    expect(staleSelfMessage(null)).toContain('an earlier day');
  });
});
