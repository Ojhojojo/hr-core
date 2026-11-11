import { describe, expect, it } from 'vitest';
import type { OpenPunchRow } from './attendance.models';
import { stalePunches } from './stale-punch';

const TODAY = '2026-11-12';

describe('stalePunches', () => {
  it('flags a punch opened two weeks ago as severe', () => {
    const rows: OpenPunchRow[] = [
      {
        punchId: 'punch-old',
        staffId: 'staff-1',
        staffName: 'Int QA Tech',
        inAtUtc: '2026-10-29T08:45:00Z',
        inAtManila: 'Oct 29, 2026 4:45 PM',
      },
    ];

    expect(stalePunches(rows, TODAY)).toEqual([
      {
        punchId: 'punch-old',
        staffName: 'Int QA Tech',
        openedOn: '2026-10-29',
        days: 14,
        severe: true,
        label: 'Int QA Tech: open since Oct 29, 2026 4:45 PM (14 days)',
      },
    ]);
  });

  it('counts a clock-in from yesterday evening as one day, not severe', () => {
    const [item] = stalePunches(
      [
        {
          punchId: 'punch-yesterday',
          staffId: 'staff-2',
          staffName: 'Danny',
          inAtUtc: '2026-11-11T15:30:00Z',
          inAtManila: 'Nov 11, 2026 11:30 PM',
        },
      ],
      TODAY,
    );

    expect(item.days).toBe(1);
    expect(item.severe).toBe(false);
    expect(item.label.endsWith('(1 day)')).toBe(true);
  });

  it('does not list a punch opened earlier today in Manila', () => {
    // 2026-11-11T20:01Z is Nov 12, 4:01 AM in Manila.
    expect(
      stalePunches(
        [
          {
            punchId: 'punch-today',
            staffId: 'staff-3',
            staffName: 'Maria',
            inAtUtc: '2026-11-11T20:01:00Z',
            inAtManila: 'Nov 12, 2026 4:01 AM',
          },
        ],
        TODAY,
      ),
    ).toEqual([]);
  });

  it('uses the Manila date, so a 1:00 AM Manila clock-in counts as the previous day', () => {
    // 2026-11-10T17:00Z is Nov 11, 1:00 AM in Manila, one day before today.
    const [item] = stalePunches(
      [
        {
          punchId: 'punch-early',
          staffId: 'staff-4',
          staffName: 'Jun',
          inAtUtc: '2026-11-10T17:00:00Z',
          inAtManila: 'Nov 11, 2026 1:00 AM',
        },
      ],
      TODAY,
    );

    expect(item.openedOn).toBe('2026-11-11');
    expect(item.days).toBe(1);
  });

  it('sorts the oldest open punch first', () => {
    const result = stalePunches(
      [
        {
          punchId: 'punch-recent',
          staffId: 'staff-2',
          staffName: 'Danny',
          inAtUtc: '2026-11-11T15:30:00Z',
          inAtManila: 'Nov 11, 2026 11:30 PM',
        },
        {
          punchId: 'punch-old',
          staffId: 'staff-1',
          staffName: 'Int QA Tech',
          inAtUtc: '2026-10-29T08:45:00Z',
          inAtManila: 'Oct 29, 2026 4:45 PM',
        },
      ],
      TODAY,
    );

    expect(result.map((item) => item.punchId)).toEqual(['punch-old', 'punch-recent']);
  });
});
