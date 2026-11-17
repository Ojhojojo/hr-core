/**
 * #685: an open clock-in counts as forgotten when it started on an earlier Manila day AND has been
 * open longer than this. The hours rule keeps a night shift that crosses midnight clocking out
 * normally; only a clock-in left open well past a shift is treated as forgotten.
 */
export const STALE_OPEN_HOURS = 16;

const HOUR_MS = 60 * 60 * 1000;

function manilaDate(utc: Date): string {
  // Manila is UTC+8 all year (no daylight saving).
  return new Date(utc.getTime() + 8 * HOUR_MS).toISOString().slice(0, 10);
}

/** True when the open clock-in from `inAtUtc` is from an earlier day and open too long to be a shift. */
export function isStaleOpenPunch(
  inAtUtc: string | null | undefined,
  now: Date = new Date(),
): boolean {
  if (!inAtUtc) {
    return false;
  }
  const opened = new Date(inAtUtc);
  if (Number.isNaN(opened.getTime())) {
    return false;
  }
  return (
    manilaDate(opened) < manilaDate(now) &&
    now.getTime() - opened.getTime() > STALE_OPEN_HOURS * HOUR_MS
  );
}

/** What the kiosk says when someone with a forgotten clock-in is tapped (same wording as the API). */
export function staleKioskMessage(name: string, inAtManila: string | null | undefined): string {
  const when = inAtManila?.trim() || 'an earlier day';
  return `${name} still has an open clock-in from ${when}. Close it on Attendance with the real time, then clock them in.`;
}

/** What Me › Time clock says when your own clock-in was left open. */
export function staleSelfMessage(inAtManila: string | null | undefined): string {
  const when = inAtManila?.trim() || 'an earlier day';
  return `Your clock-in from ${when} is still open. Ask the desk to close it on Attendance with the real time, then clock in again.`;
}
