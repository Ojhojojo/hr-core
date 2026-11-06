import { InjectionToken } from '@angular/core';

/**
 * Host opt-in for payroll cut-offs, deduction schedules, and catch-ups.
 * The same switch as `HrCoreFeatures.PayrollSchedules` on the API.
 * Omit the provider, or provide false, and the payroll screen hides those panels
 * and does not call their routes.
 */
export const HR_PAYROLL_SCHEDULES = new InjectionToken<boolean>('HR_PAYROLL_SCHEDULES');
