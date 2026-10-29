/**
 * Friendly calendar dates for the HR screens: "Oct 3, 2026", never "2026-10-03".
 * Kept here (not in the host app) so every hr-core host shows the same dates.
 */

const SHORT_MONTHS = [
  'Jan',
  'Feb',
  'Mar',
  'Apr',
  'May',
  'Jun',
  'Jul',
  'Aug',
  'Sep',
  'Oct',
  'Nov',
  'Dec',
];

const ISO_DAY = /^(\d{4})-(\d{2})-(\d{2})/;

interface DayParts {
  year: string;
  month: string;
  day: number;
}

function dayParts(value: string | null | undefined): DayParts | null {
  if (!value) {
    return null;
  }

  let iso = value;
  if (value.length > 10 && value[10] === 'T') {
    // A timestamp: take the calendar day in Manila.
    const parsed = new Date(value);
    if (!Number.isNaN(parsed.getTime())) {
      iso = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Manila' }).format(parsed);
    }
  }

  const match = ISO_DAY.exec(iso);
  if (!match) {
    return null;
  }

  const month = Number(match[2]);
  const day = Number(match[3]);
  if (month < 1 || month > 12 || day < 1 || day > 31) {
    return null;
  }

  return { year: match[1], month: SHORT_MONTHS[month - 1], day };
}

/** "Oct 3, 2026" for a `yyyy-MM-dd` day or a timestamp (Manila). Em dash when empty or unreadable. */
export function formatDay(value: string | null | undefined): string {
  const parts = dayParts(value);
  return parts ? `${parts.month} ${parts.day}, ${parts.year}` : '—';
}

/**
 * A day range: "Nov 10, 2026" for one day, "Oct 26 – 28, 2026" in one month,
 * "Oct 30 – Nov 2, 2026" in one year, otherwise both days in full.
 */
export function formatDayRange(
  start: string | null | undefined,
  end: string | null | undefined,
): string {
  const a = dayParts(start);
  const b = dayParts(end);
  if (!a || !b) {
    return a ? formatDay(start) : formatDay(end);
  }

  if (a.year === b.year && a.month === b.month && a.day === b.day) {
    return `${a.month} ${a.day}, ${a.year}`;
  }

  if (a.year === b.year && a.month === b.month) {
    return `${a.month} ${a.day} – ${b.day}, ${a.year}`;
  }

  if (a.year === b.year) {
    return `${a.month} ${a.day} – ${b.month} ${b.day}, ${a.year}`;
  }

  return `${a.month} ${a.day}, ${a.year} – ${b.month} ${b.day}, ${b.year}`;
}

/**
 * Text from the API that starts with a `yyyy-MM-dd` day, such as a punch "2026-10-29 4:45 PM":
 * the day becomes "Oct 29, 2026,". Other text is returned as is.
 */
export function formatDayPrefix(value: string | null | undefined): string {
  if (!value) {
    return '—';
  }

  const match = /^(\d{4}-\d{2}-\d{2})(\s+)(.*)$/.exec(value);
  if (!match) {
    return value;
  }

  const day = formatDay(match[1]);
  return day === '—' ? value : `${day}, ${match[3]}`;
}
