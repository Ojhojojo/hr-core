import { describe, expect, it } from 'vitest';
import {
  deductionDraftDirty,
  normalizePremiumTime,
  premiumSettingsDirty,
  rememberDeductionAmount,
  thirteenthDraftDirty,
  unsavedPayrollDraftKinds,
} from './payroll-draft';
import { UpdatePayrollPremiumSettings } from './payroll.models';

function settings(
  partial: Partial<UpdatePayrollPremiumSettings> = {},
): UpdatePayrollPremiumSettings {
  return {
    regularHolidayPercent: 200,
    specialDayPercent: 130,
    restDayPercent: 130,
    holidayPremiumEnabled: true,
    restDayPremiumEnabled: true,
    nightDiffEnabled: false,
    nightDiffPercent: 10,
    nightStart: '22:00',
    nightEnd: '06:00',
    shiftEnd: '17:00',
    otGraceMinutes: 30,
    adminsCanApproveOt: false,
    otPremiumPercent: 125,
    otMonthlyDayDivisor: 313,
    ...partial,
  };
}

describe('rememberDeductionAmount', () => {
  it('treats null and invalid as zero', () => {
    expect(rememberDeductionAmount(null)).toBe(0);
    expect(rememberDeductionAmount(undefined)).toBe(0);
    expect(rememberDeductionAmount(Number.NaN)).toBe(0);
    expect(rememberDeductionAmount(250.5)).toBe(250.5);
  });
});

describe('deductionDraftDirty', () => {
  it('is quiet when draft matches the saved register amounts', () => {
    expect(
      deductionDraftDirty(
        { sss: 100, philHealth: 50, pagIbig: 0, wht: 10, other: 0 },
        { sss: 100, philHealth: 50, pagIbig: null, wht: 10, other: undefined },
      ),
    ).toBe(false);
  });

  it('flags any statutory field that differs', () => {
    const saved = { sss: 100, philHealth: 50, pagIbig: 0, wht: 10, other: 0 };
    expect(
      deductionDraftDirty({ sss: 250, philHealth: 50, pagIbig: 0, wht: 10, other: 0 }, saved),
    ).toBe(true);
    expect(
      deductionDraftDirty({ sss: 100, philHealth: 50, pagIbig: 0, wht: 10, other: 5 }, saved),
    ).toBe(true);
    expect(deductionDraftDirty(null, saved)).toBe(false);
  });
});

describe('premiumSettingsDirty', () => {
  it('is quiet when draft matches the saved settings', () => {
    const saved = settings();
    expect(premiumSettingsDirty({ ...saved }, saved)).toBe(false);
  });

  it('flags holiday and night-diff toggles (smoke: unsaved NSD before lock)', () => {
    const saved = settings({ nightDiffEnabled: false, holidayPremiumEnabled: true });
    expect(premiumSettingsDirty(settings({ nightDiffEnabled: true }), saved)).toBe(true);
    expect(premiumSettingsDirty(settings({ holidayPremiumEnabled: false }), saved)).toBe(true);
  });

  it('flags percent and time edits, treating HH:MM and HH:MM:SS as equal', () => {
    const saved = settings({ nightStart: '22:00:00', nightDiffPercent: 10 });
    expect(
      premiumSettingsDirty(settings({ nightStart: '22:00', nightDiffPercent: 10 }), saved),
    ).toBe(false);
    expect(premiumSettingsDirty(settings({ nightDiffPercent: 12.5 }), saved)).toBe(true);
    expect(premiumSettingsDirty(settings({ nightStart: '21:00' }), saved)).toBe(true);
  });

  it('is quiet when either side is missing', () => {
    expect(premiumSettingsDirty(null, settings())).toBe(false);
    expect(premiumSettingsDirty(settings(), null)).toBe(false);
  });
});

describe('normalizePremiumTime', () => {
  it('keeps HH:MM for comparison', () => {
    expect(normalizePremiumTime('22:00:00')).toBe('22:00');
    expect(normalizePremiumTime('22:00')).toBe('22:00');
    expect(normalizePremiumTime(' 06:30:00 ')).toBe('06:30');
  });
});

describe('thirteenthDraftDirty', () => {
  it('compares override drafts to the saved override', () => {
    expect(thirteenthDraftDirty(null, null)).toBe(false);
    expect(thirteenthDraftDirty(1000, 1000)).toBe(false);
    expect(thirteenthDraftDirty(1000, null)).toBe(true);
    expect(thirteenthDraftDirty(null, 1000)).toBe(true);
    expect(thirteenthDraftDirty(1200, 1000)).toBe(true);
  });
});

describe('unsavedPayrollDraftKinds', () => {
  it('lists every dirty kind lock/print must save first', () => {
    expect(
      unsavedPayrollDraftKinds({
        deductionsDirty: false,
        settingsDirty: false,
        thirteenthDirty: false,
      }),
    ).toEqual([]);
    expect(
      unsavedPayrollDraftKinds({
        deductionsDirty: true,
        settingsDirty: true,
        thirteenthDirty: false,
      }),
    ).toEqual(['deductions', 'settings']);
    expect(
      unsavedPayrollDraftKinds({
        deductionsDirty: false,
        settingsDirty: true,
        thirteenthDirty: true,
      }),
    ).toEqual(['settings', 'thirteenth']);
  });
});
