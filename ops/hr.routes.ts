import { inject } from '@angular/core';
import { Routes } from '@angular/router';
import { AuthService, canManageShopHr, shopHrDeskGuard } from '@hr-host';

/** Role-based landing page under `/hr`. Shop HR desk → Directory; others → Attendance. */
export function defaultHrPath(
  role: string | null | undefined,
  subrole?: string | null,
): 'employee-directory' | 'attendance' {
  return canManageShopHr(role, subrole) ? 'employee-directory' : 'attendance';
}

export function hrDefaultRedirect(
  auth: {
    me: () => { staffRole?: string | null; subrole?: string | null } | null;
  } = inject(AuthService),
): string {
  const me = auth.me();
  return defaultHrPath(me?.staffRole, me?.subrole);
}

export const hrRoutes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: () => hrDefaultRedirect(),
  },
  {
    path: 'clock',
    redirectTo: '/me/clock',
  },
  {
    path: 'payslip',
    redirectTo: '/me/payslip',
  },
  {
    path: 'clock-kiosk',
    data: { mode: 'kiosk' },
    canActivate: [shopHrDeskGuard],
    loadComponent: () => import('./clock/clock').then((m) => m.ClockPage),
  },
  {
    path: 'attendance',
    loadComponent: () => import('./attendance/attendance').then((m) => m.AttendancePage),
  },
  {
    path: 'payroll',
    canActivate: [shopHrDeskGuard],
    loadComponent: () => import('./payroll/payroll').then((m) => m.PayrollPage),
  },
  {
    path: 'employee-directory',
    canActivate: [shopHrDeskGuard],
    loadComponent: () =>
      import('./employee-directory/employee-directory').then((m) => m.EmployeeDirectoryPage),
  },
  {
    path: 'employee-directory/:id',
    canActivate: [shopHrDeskGuard],
    loadComponent: () =>
      import('./employee-directory/employee-detail').then((m) => m.EmployeeDetailPage),
  },
  {
    path: 'documents',
    canActivate: [shopHrDeskGuard],
    loadComponent: () => import('./documents/hr-documents').then((m) => m.HrDocumentsPage),
  },
  {
    path: 'leave',
    loadComponent: () => import('./leave/leave').then((m) => m.LeavePage),
  },
  {
    path: 'announcements',
    canActivate: [shopHrDeskGuard],
    loadComponent: () => import('./announcements/announcements').then((m) => m.AnnouncementsPage),
  },
];
