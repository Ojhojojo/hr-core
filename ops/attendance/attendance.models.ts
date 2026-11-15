import { PunchApprovalStatus } from '../clock/clock.models';

export type AttendanceApprovalFilter = PunchApprovalStatus;

export interface AttendanceRegisterRow {
  staffId: string;
  staffName: string;
  branchId: string;
  branchName: string;
  branchCode: string;
  branchKind?: 'owned' | 'franchise';
  inAtUtc: string;
  inAtManila: string;
  inDateManila: string;
  outAtUtc: string | null;
  outAtManila: string | null;
  hours: number | null;
  isOpen: boolean;
  punchId?: string;
  hasInPhoto?: boolean;
  hasOutPhoto?: boolean;
  closeReason?: string | null;
  inLatitude?: number | null;
  inLongitude?: number | null;
  outLatitude?: number | null;
  outLongitude?: number | null;
  approvalStatus?: PunchApprovalStatus;
  offsiteReason?: string | null;
  reviewNote?: string | null;
  reviewedBy?: string | null;
  reviewedAtUtc?: string | null;
}

export interface AttendanceRegisterResponse {
  from: string;
  to: string;
  rows: AttendanceRegisterRow[];
}

/** One page of the register, from `page` and `pageSize` on the request. */
export interface AttendanceRegisterPage {
  from: string;
  to: string;
  items: AttendanceRegisterRow[];
  total: number;
  pageNumber: number;
  pageSize: number;
  /** Punches waiting for review in the whole range, for the Pending badge. */
  pendingTotal: number;
}

export interface AttendanceRegisterQuery {
  from?: string;
  to?: string;
  staffId?: string | null;
  scope?: 'shop' | 'owned' | 'franchise' | 'all';
  approval?: AttendanceApprovalFilter | null;
  page?: number;
  pageSize?: number;
}

export interface ReviewPunchBody {
  note?: string;
}

export type OvertimeKind = 'punch' | 'request';
export type OvertimeStatus = 'pending' | 'approved' | 'rejected';

export interface OvertimeRow {
  id: string;
  staffId: string;
  staffName: string;
  workDate: string;
  kind: OvertimeKind;
  punchId?: string | null;
  claimedHours: number;
  approvedHours?: number | null;
  status: OvertimeStatus;
  reason?: string | null;
  decisionNote?: string | null;
  decidedByName?: string | null;
  decidedAtUtc?: string | null;
  canApprove: boolean;
}

export interface OvertimeListResponse {
  from: string;
  to: string;
  rows: OvertimeRow[];
}

export interface AttendanceStaffOption {
  id: string;
  displayName: string;
  role: string;
  branchName?: string | null;
  mustClock?: boolean;
}

export interface PunchForBody {
  staffId: string;
  inAtManila: string;
  outAtManila?: string;
}

export interface ClosePunchBody {
  outAtManila: string;
  reason: string;
}
