import { InjectionToken } from '@angular/core';

/**
 * Host opt-in for employer shares and the monthly remittance report.
 * The same switch as `HrCoreFeatures.EmployerRemittance` on the API.
 * Omit the provider, or provide false, and the payroll screen hides the panel
 * and does not call its routes.
 */
export const HR_EMPLOYER_REMITTANCE = new InjectionToken<boolean>('HR_EMPLOYER_REMITTANCE');
