import { InjectionToken } from '@angular/core';
import { canViewRemittance } from '@hr-host';

/**
 * Host opt-in for employer shares and the monthly remittance report.
 * The same switch as `HrCoreFeatures.EmployerRemittance` on the API.
 * Omit the provider, or provide false, and the payroll screen hides the panel
 * and does not call its routes.
 */
export const HR_EMPLOYER_REMITTANCE = new InjectionToken<boolean>('HR_EMPLOYER_REMITTANCE');

/**
 * Host answer for who may open the remittance report. The default import is
 * `canViewRemittance` from `@hr-host`. Specs provide this token instead of mocking that module.
 */
export const HR_CAN_VIEW_REMITTANCE = new InjectionToken<
  (role: string | null | undefined) => boolean
>('HR_CAN_VIEW_REMITTANCE');

/** True when the host capability allows this caller. Tests pass their own function. */
export function mayViewRemittance(
  role: string | null | undefined,
  canView: (role: string | null | undefined) => boolean = canViewRemittance,
): boolean {
  return canView(role);
}
