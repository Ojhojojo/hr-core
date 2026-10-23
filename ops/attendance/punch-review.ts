import { StaffRole, StatusBadgeTone } from '@hr-host';
import { PunchApprovalStatus } from '../clock/clock.models';

/**
 * Owner and admin may approve or reject a pending punch.
 * Technicians, sales, and any other role cannot.
 * Approved and rejected punches cannot be reviewed again.
 */
export function canReviewPunch(
  role: StaffRole | string | null | undefined,
  status: PunchApprovalStatus | string | null | undefined,
): boolean {
  return (role === 'owner' || role === 'admin') && status === 'pending';
}

export function punchStatusLabel(status: PunchApprovalStatus | string | null | undefined): string {
  if (status === 'pending') {
    return 'Pending';
  }
  if (status === 'rejected') {
    return 'Rejected';
  }
  return 'Approved';
}

export function punchStatusTone(
  status: PunchApprovalStatus | string | null | undefined,
): StatusBadgeTone {
  if (status === 'pending') {
    return 'warn';
  }
  if (status === 'rejected') {
    return 'danger';
  }
  return 'ok';
}
