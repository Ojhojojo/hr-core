import { InjectionToken } from '@angular/core';

/**
 * Host opt-in for employer shares and the monthly remittance report.
 * The same switch as `HrCoreFeatures.EmployerRemittance` on the API.
 * Omit the provider, or provide false, and the payroll screen hides the panel
 * and does not call its routes.
 */
export const HR_EMPLOYER_REMITTANCE = new InjectionToken<boolean>('HR_EMPLOYER_REMITTANCE');

/** The signed-in person, as far as these capabilities need to see them. */
export interface HrCapabilityUser {
  staffRole?: string | null;
}

/** Who may open the remittance report. The default refuses every caller. */
export interface HrRemittanceAccess {
  canView(user: HrCapabilityUser | null | undefined): boolean;
}

/** Who may confirm employer shares. The default refuses every caller. */
export interface HrEmployerShareReview {
  canReview(user: HrCapabilityUser | null | undefined): boolean;
}

/**
 * Host answer for who may open the remittance report.
 * Omit the provider and nobody sees the report. Specs pass a neutral stub.
 */
export const HR_REMITTANCE_ACCESS = new InjectionToken<HrRemittanceAccess>('HR_REMITTANCE_ACCESS', {
  providedIn: 'root',
  factory: () => ({
    canView: () => false,
  }),
});

/**
 * Host answer for who may confirm employer shares.
 * Omit the provider and the review panel stays hidden. Specs pass a neutral stub.
 */
export const HR_EMPLOYER_SHARE_REVIEW = new InjectionToken<HrEmployerShareReview>(
  'HR_EMPLOYER_SHARE_REVIEW',
  {
    providedIn: 'root',
    factory: () => ({
      canReview: () => false,
    }),
  },
);
