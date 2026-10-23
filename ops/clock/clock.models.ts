export type PunchApprovalStatus = 'pending' | 'approved' | 'rejected';

export interface AttendancePunch {
  id: string;
  staffId: string;
  branchId: string;
  branchName: string;
  inAtUtc: string;
  outAtUtc: string | null;
  inAtManila: string;
  outAtManila: string | null;
  source: string;
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

export interface KioskStaff {
  id: string;
  displayName: string;
  mustClock: boolean;
  clockedIn: boolean;
  openPunchId?: string | null;
  inAtUtc?: string | null;
  inAtManila?: string | null;
}

export interface AttendanceMe {
  clockedIn: boolean;
  status: string;
  open: AttendancePunch | null;
  today: AttendancePunch[];
  requireClockPhoto?: boolean;
  allowOffsitePunchWithApproval?: boolean;
}
