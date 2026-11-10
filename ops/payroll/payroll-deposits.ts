import { InjectionToken } from '@angular/core';

/**
 * Host opt-in for recording what was deposited on a locked payroll line.
 * The same switch as `HrCoreFeatures.Deposits` on the API.
 * Omit the provider, or provide false, and the payroll screen hides the panel
 * and does not call the deposit route.
 */
export const HR_PAYROLL_DEPOSITS = new InjectionToken<boolean>('HR_PAYROLL_DEPOSITS');
