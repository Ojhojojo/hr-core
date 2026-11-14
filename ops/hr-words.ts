import { InjectionToken } from '@angular/core';

/**
 * Page titles the HR screens show. Rakkii's current text is the default.
 */
export interface HrHostTitles {
  /** Employee directory page title, and the Directory back link on a record. */
  directory: string;
  /** Record title when a person's record fails to load and their name is not known. */
  employeeRecord: string;
  /** Own profile title (`/me/profile`) when the record fails to load and the name is not known. */
  myProfile: string;
  /** Time clock page title. */
  clock: string;
  /** Attendance title for managers. */
  attendance: string;
  /** Own attendance title on the self page, when the route is not the overtime one. */
  myWeek: string;
  /** Own attendance title on the self page when the route sets `data: { overtime: true }`. */
  myOvertime: string;
  /** Leave title on the HR route. */
  leave: string;
  /** Leave title on the self route when it sets `data: { self: true }`. */
  myLeave: string;
  /** Own payslip history title. */
  myPayslips: string;
}

/**
 * Words and links the host can change for its own business.
 * hr-core screens read them through {@link HR_HOST_WORDS}. Omit the provider and
 * {@link DEFAULT_HR_WORDS} (Rakkii's text) is used.
 */
export interface HrHostWords {
  /** Name of the place people work at, as a label: "Shop to lock". Rakkii: "Shop". */
  placeLabel: string;
  /** Column header for the branch name on attendance. Rakkii: "Branch". */
  branchColumnLabel: string;
  /** The same place in a sentence, lower case: "This shop requires a photo". Rakkii: "shop". */
  placeSentenceWord: string;
  /** Dashboard route linked from announcements. Null hides it. Rakkii: "/workshop". */
  dashboardLink: string | null;
  /** Text of the dashboard link. Rakkii: "Dashboard". */
  dashboardLabel: string;
  /** Announcements info tooltip about who sees them. */
  announcementAudienceHint: string;
  /** Documents info tooltip about which forms live where. */
  documentsTooltip: string;
  /** Menu item on an employee record that moves open work to someone else. Null hides it. */
  moveOpenJobsLabel: string | null;
  titles: HrHostTitles;
}

/** Rakkii's current text. A host that provides nothing sees no change. */
export const DEFAULT_HR_WORDS: HrHostWords = {
  placeLabel: 'Shop',
  branchColumnLabel: 'Branch',
  placeSentenceWord: 'shop',
  dashboardLink: '/workshop',
  dashboardLabel: 'Dashboard',
  announcementAudienceHint:
    'Visible on Dashboard for sales and technicians while active and within the date range.',
  documentsTooltip: 'Offer and COE stay in Directory; workshop forms stay on Forms.',
  moveOpenJobsLabel: 'Move open jobs to…',
  titles: {
    directory: 'Directory',
    employeeRecord: 'Employee record',
    myProfile: 'My profile',
    clock: 'Time clock',
    attendance: 'Attendance',
    myWeek: 'My week',
    myOvertime: 'My week',
    leave: 'Leave',
    myLeave: 'Leave',
    myPayslips: 'My payslip',
  },
};

/**
 * Host words for the HR screens. Provide it to change the text:
 *
 * ```ts
 * { provide: HR_HOST_WORDS, useValue: { ...DEFAULT_HR_WORDS, placeLabel: 'Site' } }
 * ```
 *
 * `titles` is nested, so a host that changes one title spreads `titles` too.
 */
export const HR_HOST_WORDS = new InjectionToken<HrHostWords>('HR_HOST_WORDS', {
  providedIn: 'root',
  factory: () => DEFAULT_HR_WORDS,
});
