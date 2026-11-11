import type { OpenPunchRow } from './attendance.models';

export interface StalePunch {
  punchId: string;
  staffName: string;
  /** Manila calendar date of the clock-in, YYYY-MM-DD. */
  openedOn: string;
  days: number;
  /** 14 days or more: payroll periods have passed with this punch open. */
  severe: boolean;
  label: string;
}

const DAY_MS = 24 * 60 * 60 * 1000;

function manilaDate(utcIso: string): string {
  // Manila is UTC+8 all year (no daylight saving).
  return new Date(new Date(utcIso).getTime() + 8 * 60 * 60 * 1000).toISOString().slice(0, 10);
}

/**
 * #685: open punches whose clock-in was on an earlier Manila day than today. A forgotten clock-out
 * blocks that person's next clock-in until someone closes it, so Attendance warns about each one.
 */
export function stalePunches(rows: readonly OpenPunchRow[], today: string): StalePunch[] {
  return rows
    .map((row) => {
      const openedOn = manilaDate(row.inAtUtc);
      const days = Math.round((Date.parse(today) - Date.parse(openedOn)) / DAY_MS);
      return { row, openedOn, days };
    })
    .filter((item) => item.days >= 1)
    .sort((a, b) => b.days - a.days)
    .map(({ row, openedOn, days }) => ({
      punchId: row.punchId,
      staffName: row.staffName,
      openedOn,
      days,
      severe: days >= 14,
      label: `${row.staffName}: open since ${row.inAtManila} (${days} day${days === 1 ? '' : 's'})`,
    }));
}
