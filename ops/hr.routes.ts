import { inject } from '@angular/core';
import { Routes } from '@angular/router';
import { ownerOrAdminGuard, AuthService } from '@hr-host';

/** Role-based landing page under `/hr`. Owners and admins see Directory; floor staff see Attendance. */
export function defaultHrPath(
  role: string | null | undefined,
): 'employee-directory' | 'attendance' {
  return role === 'owner' || role === 'admin' ? 'employee-directory' : 'attendance';
}

export function hrDefaultRedirect(
  auth: { me: () => { staffRole?: string | null } | null } = inject(AuthService),
): string {
  return defaultHrPath(auth.me()?.staffRole);
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
    canActivate: [ownerOrAdminGuard],
    loadComponent: () => import('./clock/clock').then((m) => m.ClockPage),
  },
  {
    path: 'attendance',
    loadComponent: () => import('./attendance/attendance').then((m) => m.AttendancePage),
  },
  {
    path: 'payroll',
    canActivate: [ownerOrAdminGuard],
    loadComponent: () => import('./payroll/payroll').then((m) => m.PayrollPage),
  },
  {
    path: 'employee-directory',
    canActivate: [ownerOrAdminGuard],
    loadComponent: () =>
      import('./employee-directory/employee-directory').then((m) => m.EmployeeDirectoryPage),
  },
  {
    path: 'employee-directory/:id',
    canActivate: [ownerOrAdminGuard],
    loadComponent: () =>
      import('./employee-directory/employee-detail').then((m) => m.EmployeeDetailPage),
  },
  {
    path: 'documents',
    canActivate: [ownerOrAdminGuard],
    loadComponent: () => import('./documents/hr-documents').then((m) => m.HrDocumentsPage),
  },
  {
    path: 'leave',
    loadComponent: () => import('./leave/leave').then((m) => m.LeavePage),
  },
  {
    path: 'announcements',
    canActivate: [ownerOrAdminGuard],
    loadComponent: () => import('./announcements/announcements').then((m) => m.AnnouncementsPage),
  },
];
