import { canManageShopHr, StaffRole, StatusBadgeTone } from '@hr-host';
import { PunchApprovalStatus } from '../clock/clock.models';

/**
 * Owner or General Manager Access may approve or reject a pending punch (#885).
 * Purchaser / BDO / technicians / sales cannot. Settled punches stay closed.
 */
export function canReviewPunch(
  role: StaffRole | string | null | undefined,
  status: PunchApprovalStatus | string | null | undefined,
  subrole?: string | null,
): boolean {
  return canManageShopHr(role, subrole) && status === 'pending';
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
