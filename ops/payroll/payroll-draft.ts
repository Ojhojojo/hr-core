import { PayrollDeductionDraft, UpdatePayrollPremiumSettings } from './payroll.models';

/** Amount shown in a deduction draft box. Null/invalid saved values become 0. */
export function rememberDeductionAmount(value: number | null | undefined): number {
  const amount = Number(value ?? 0);
  return Number.isFinite(amount) ? amount : 0;
}

/** True when the on-screen deduction draft differs from the last saved register amounts. */
export function deductionDraftDirty(
  draft: PayrollDeductionDraft | null | undefined,
  saved:
    | {
        sss?: number | null;
        philHealth?: number | null;
        pagIbig?: number | null;
        wht?: number | null;
        other?: number | null;
      }
    | null
    | undefined,
): boolean {
  if (!draft || !saved) {
    return false;
  }
  return (
    draft.sss !== rememberDeductionAmount(saved.sss) ||
    draft.philHealth !== rememberDeductionAmount(saved.philHealth) ||
    draft.pagIbig !== rememberDeductionAmount(saved.pagIbig) ||
    draft.wht !== rememberDeductionAmount(saved.wht) ||
    draft.other !== rememberDeductionAmount(saved.other)
  );
}

/** Normalize a time control value so "22:00" and "22:00:00" compare equal. */
export function normalizePremiumTime(value: string | null | undefined): string {
  const match = /^(\d{2}):(\d{2})/.exec((value ?? '').trim());
  return match ? `${match[1]}:${match[2]}` : (value ?? '').trim();
}

function sameNumber(left: unknown, right: unknown): boolean {
  const a = Number(left);
  const b = Number(right);
  if (!Number.isFinite(a) || !Number.isFinite(b)) {
    return left === right;
  }
  return a === b;
}

/** True when holiday / NSD / OT premium settings differ from the last saved copy. */
export function premiumSettingsDirty(
  draft: UpdatePayrollPremiumSettings | null | undefined,
  saved: UpdatePayrollPremiumSettings | null | undefined,
): boolean {
  if (!draft || !saved) {
    return false;
  }
  return (
    draft.holidayPremiumEnabled !== saved.holidayPremiumEnabled ||
    draft.restDayPremiumEnabled !== saved.restDayPremiumEnabled ||
    draft.nightDiffEnabled !== saved.nightDiffEnabled ||
    draft.adminsCanApproveOt !== saved.adminsCanApproveOt ||
    !sameNumber(draft.regularHolidayPercent, saved.regularHolidayPercent) ||
    !sameNumber(draft.specialDayPercent, saved.specialDayPercent) ||
    !sameNumber(draft.restDayPercent, saved.restDayPercent) ||
    !sameNumber(draft.nightDiffPercent, saved.nightDiffPercent) ||
    !sameNumber(draft.otGraceMinutes, saved.otGraceMinutes) ||
    !sameNumber(draft.otPremiumPercent, saved.otPremiumPercent) ||
    !sameNumber(draft.otMonthlyDayDivisor, saved.otMonthlyDayDivisor) ||
    normalizePremiumTime(draft.nightStart) !== normalizePremiumTime(saved.nightStart) ||
    normalizePremiumTime(draft.nightEnd) !== normalizePremiumTime(saved.nightEnd) ||
    normalizePremiumTime(draft.shiftEnd) !== normalizePremiumTime(saved.shiftEnd)
  );
}

/** True when a 13th-month override draft differs from the saved override. */
export function thirteenthDraftDirty(
  draftAmount: number | null | undefined,
  savedOverride: number | null | undefined,
): boolean {
  if (draftAmount == null && savedOverride == null) {
    return false;
  }
  if (draftAmount == null || savedOverride == null) {
    return true;
  }
  return !sameNumber(draftAmount, savedOverride);
}

export type UnsavedPayrollDraftKind = 'deductions' | 'settings' | 'thirteenth';

/**
 * Which editable payroll drafts differ from what is stored. Lock and print use this list
 * so any open edit (holiday/NSD toggles, deductions, 13th-month overrides) prompts save-first.
 */
export function unsavedPayrollDraftKinds(input: {
  deductionsDirty: boolean;
  settingsDirty: boolean;
  thirteenthDirty: boolean;
}): UnsavedPayrollDraftKind[] {
  const kinds: UnsavedPayrollDraftKind[] = [];
  if (input.deductionsDirty) {
    kinds.push('deductions');
  }
  if (input.settingsDirty) {
    kinds.push('settings');
  }
  if (input.thirteenthDirty) {
    kinds.push('thirteenth');
  }
  return kinds;
}
