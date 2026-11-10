import { InjectionToken } from '@angular/core';

/**
 * Host opt-in for holding a payout until clearance.
 * The same switch as `HrCoreFeatures.PayoutHold` on the API.
 * Omit the provider, or provide false, and the payroll screen hides the panel
 * and does not call the hold routes.
 */
export const HR_PAYOUT_HOLD = new InjectionToken<boolean>('HR_PAYOUT_HOLD');
