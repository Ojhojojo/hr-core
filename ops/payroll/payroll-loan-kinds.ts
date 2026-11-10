import { InjectionToken } from '@angular/core';

/**
 * Host opt-in for the loan kind list.
 * The same switch as `HrCoreFeatures.LoanKinds` on the API.
 * Omit the provider, or provide false, and the payroll screen hides the panel
 * and the loan dialog keeps a free-text kind.
 */
export const HR_LOAN_KINDS = new InjectionToken<boolean>('HR_LOAN_KINDS');
