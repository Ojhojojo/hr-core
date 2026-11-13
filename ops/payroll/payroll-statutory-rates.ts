import { InjectionToken } from '@angular/core';

/**
 * Host opt-in for effective-dated SSS, PhilHealth, Pag-IBIG, and withholding rows.
 * The same switch as `HrCoreFeatures.StatutoryRates` on the API.
 * Omit the provider, or provide false, and the payroll screen hides the panel
 * and payroll keeps using the compiled statutory pack.
 */
export const HR_STATUTORY_RATES = new InjectionToken<boolean>('HR_STATUTORY_RATES');
