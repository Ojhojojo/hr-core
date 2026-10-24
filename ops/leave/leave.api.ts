import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '@hr-host';

export type LeaveType = 'vacation' | 'sick' | 'unpaid' | 'service_incentive';
export type LeaveStatus = 'pending' | 'approved' | 'rejected' | 'cancelled';
export type LeaveHalfDay = 'am' | 'pm';

export const LEAVE_TYPE_LABELS: Record<LeaveType, string> = {
  vacation: 'Vacation',
  sick: 'Sick',
  unpaid: 'Unpaid',
  service_incentive: 'Service incentive (SIL)',
};

/** Unpaid leave has no allotment to run out of (it just isn't paid), so screens show days taken. */
export function leaveHasDayLimit(type: LeaveType): boolean {
  return type !== 'unpaid';
}

export interface LeaveRequestRow {
  id: string;
  staffId: string;
  displayName: string;
  leaveType: LeaveType;
  startDate: string;
  endDate: string;
  status: LeaveStatus;
  note: string | null;
  halfDay: LeaveHalfDay | null;
  /** Working days charged (rest days and shop holidays excluded; 0.5 for a half day). */
  days: number;
  cancelReason: string | null;
}

export interface LeaveBalance {
  leaveType: LeaveType;
  allotment: number;
  used: number;
  pending: number;
  remaining: number;
}

export interface StaffLeaveBalances {
  staffId: string;
  displayName: string;
  role: string;
  balances: LeaveBalance[];
}

export interface LeavePerson {
  id: string;
  displayName: string;
  role?: string;
}

export interface LeaveAffectedJob {
  jobId: string;
  reference: string;
  status: string;
  plate: string;
  promisedDate: string | null;
  otherAssignees: { staffId: string; displayName: string }[];
}

export interface LeaveReassignment {
  jobId: string;
  /** Null keeps the job as it is. */
  toStaffId: string | null;
}

@Injectable({ providedIn: 'root' })
export class LeaveApi {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  list(year: number, mine = false): Observable<LeaveRequestRow[]> {
    const params: Record<string, string | number> = { year };
    if (mine) {
      params['mine'] = 'true';
    }
    return this.http.get<LeaveRequestRow[]>(`${this.base}/api/leave`, { params });
  }

  balances(year: number): Observable<LeaveBalance[]> {
    return this.http.get<LeaveBalance[]>(`${this.base}/api/leave/balances`, {
      params: { year },
    });
  }

  shopBalances(year: number): Observable<StaffLeaveBalances[]> {
    return this.http.get<StaffLeaveBalances[]>(`${this.base}/api/leave/balances`, {
      params: { year, shop: 1 },
    });
  }

  create(body: {
    leaveType: LeaveType;
    startDate: string;
    endDate: string;
    note?: string;
    halfDay?: LeaveHalfDay;
  }): Observable<LeaveRequestRow> {
    return this.http.post<LeaveRequestRow>(`${this.base}/api/leave`, body);
  }

  recordForStaff(body: {
    staffId: string;
    leaveType: LeaveType;
    startDate: string;
    endDate: string;
    note?: string;
    halfDay?: LeaveHalfDay;
  }): Observable<LeaveRequestRow> {
    return this.http.post<LeaveRequestRow>(`${this.base}/api/leave/for-staff`, body);
  }

  cancel(id: string, reason?: string): Observable<LeaveRequestRow> {
    return this.http.post<LeaveRequestRow>(`${this.base}/api/leave/${id}/cancel`, {
      reason: reason ?? null,
    });
  }

  /** Open jobs the requester is currently assigned to. Approval can move them. */
  affectedJobs(id: string): Observable<LeaveAffectedJob[]> {
    return this.http.get<LeaveAffectedJob[]>(`${this.base}/api/leave/${id}/affected-jobs`);
  }

  /** `toStaffId` null keeps the job with its current assignees. Sent only when there are entries. */
  decide(
    id: string,
    decision: 'approved' | 'rejected',
    reassignments?: LeaveReassignment[],
  ): Observable<LeaveRequestRow> {
    const body = reassignments?.length ? { decision, reassignments } : { decision };
    return this.http.post<LeaveRequestRow>(`${this.base}/api/leave/${id}/decision`, body);
  }

  saveAllotment(body: {
    staffId: string;
    year: number;
    leaveType: LeaveType;
    days: number;
  }): Observable<unknown> {
    return this.http.put(`${this.base}/api/leave/allotments`, body);
  }

  listPeople(): Observable<LeavePerson[]> {
    return this.http
      .get<LeavePerson[] | { items: LeavePerson[] }>(`${this.base}/api/employee-directory`, {
        params: { includeInactive: false },
      })
      .pipe(map((body) => (Array.isArray(body) ? body : body.items)));
  }
}
