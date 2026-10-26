import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '@hr-host';
import { HrChangeLogList } from './hr-change-log';

export type DirectoryRole = 'owner' | 'admin' | 'technician' | 'sales';
export type DirectoryStatus = 'active' | 'on_leave_today' | 'inactive';
export type PayBasis = 'hourly' | 'daily' | 'monthly';

export interface EmployeeCard {
  id: string;
  role: DirectoryRole;
  displayName: string;
  email: string | null;
  mobile: string | null;
  hireDate: string | null;
  active: boolean;
  emergencyContactName: string | null;
  emergencyContactPhone: string | null;
  position?: string | null;
  department?: string | null;
  employeeNo?: string | null;
  status?: DirectoryStatus;
  tin?: string | null;
  sss?: string | null;
  philHealth?: string | null;
  pagIbig?: string | null;
  registeredAddress?: string | null;
  zipCode?: string | null;
  birthDate?: string | null;
  separationDate?: string | null;
  separationReason?: string | null;
  restDays?: number[] | null;
  branchId?: string | null;
  branchName?: string | null;
  canCanvassAndPaySuppliers?: boolean | null;
  canOpenLoaPdf?: boolean | null;
  canSeeLoaPesos?: boolean | null;
  mustClock?: boolean | null;
  payBasis?: PayBasis | null;
  ratePhp?: number | null;
  otAfterHoursPerDay?: number | null;
  otMultiplier?: number | null;
  /** Only present on the response to a deactivation. */
  deactivation?: DeactivationSummary | null;
}

/** What deactivation did besides flipping the flag (5.4). */
export interface DeactivationSummary {
  cancelledPendingLeave: number;
  closedOpenPunch: boolean;
  upcomingApprovedLeave: OpenWorkLeave[];
  notes: string[];
}

export interface OpenWorkJob {
  jobId: string;
  reference: string;
  status: string;
  plate: string;
  promisedDate: string | null;
  otherAssignees: { staffId: string; displayName: string }[];
}

export type OpenWorkLeaveType = 'vacation' | 'sick' | 'unpaid' | 'service_incentive';

export interface OpenWorkLeave {
  id: string;
  leaveType: OpenWorkLeaveType;
  startDate: string;
  endDate: string;
  halfDay: 'am' | 'pm' | null;
}

export interface OpenWorkPunch {
  id: string;
  inAtUtc: string;
  label: string;
}

/** A person's current work that blocks deactivation or needs handing over (5.4). */
export interface StaffOpenWork {
  staffId: string;
  displayName: string;
  active: boolean;
  jobs: OpenWorkJob[];
  pendingLeave: OpenWorkLeave[];
  approvedLeave: OpenWorkLeave[];
  openPunch: OpenWorkPunch | null;
}

export interface HandoverMove {
  jobId: string;
  toStaffId: string;
}

export type HandoverReason = 'separated' | 'rebalanced';

export interface HandoverResult {
  moved: { jobId: string; reference: string; toStaffId: string; toDisplayName: string }[];
}

export type DirectoryHealthCode =
  | 'missing_hire_date'
  | 'missing_pay_rate'
  | 'must_clock_no_rate'
  | 'not_clocking_paid_by_day_or_hour'
  | 'below_minimum_wage'
  | 'missing_statutory_ids'
  | 'sil_missing_allotment';

export interface DirectoryConfigIssue {
  staffId: string;
  displayName: string;
  issues: DirectoryHealthCode[];
}

export const HEALTH_LABELS: Record<DirectoryHealthCode, string> = {
  missing_hire_date: 'Missing hire date',
  missing_pay_rate: 'Missing pay rate',
  must_clock_no_rate: 'Must clock, no rate',
  not_clocking_paid_by_day_or_hour:
    'Paid by day or hour but not set to clock. Pay is ₱0 without day overrides.',
  below_minimum_wage: 'Rate below minimum wage',
  missing_statutory_ids: 'Missing statutory IDs',
  sil_missing_allotment: 'SIL allotment missing',
};

export interface EmployeeDirectoryList {
  headcount: number;
  items: EmployeeCard[];
  configHealth?: DirectoryConfigIssue[];
}

export interface CreateEmployee {
  email: string;
  displayName: string;
  role: DirectoryRole;
  hireDate: string;
  mobile?: string | null;
  emergencyContactName?: string | null;
  emergencyContactPhone?: string | null;
  position?: string | null;
  department?: string | null;
  employeeNo?: string | null;
  tin?: string | null;
  sss?: string | null;
  philHealth?: string | null;
  pagIbig?: string | null;
  branchId?: string | null;
  allShops?: boolean;
  canCanvassAndPaySuppliers?: boolean | null;
  canOpenLoaPdf?: boolean | null;
  canSeeLoaPesos?: boolean | null;
  mustClock?: boolean | null;
  payBasis?: PayBasis | null;
  ratePhp?: number | null;
  otAfterHoursPerDay?: number | null;
  otMultiplier?: number | null;
}

export interface UpdateEmployee {
  displayName?: string;
  mobile?: string | null;
  hireDate?: string | null;
  emergencyContactName?: string | null;
  emergencyContactPhone?: string | null;
  position?: string | null;
  department?: string | null;
  employeeNo?: string | null;
  tin?: string | null;
  sss?: string | null;
  philHealth?: string | null;
  pagIbig?: string | null;
  registeredAddress?: string | null;
  zipCode?: string | null;
  birthDate?: string | null;
  clearBirthDate?: boolean;
  role?: DirectoryRole;
  active?: boolean;
  separationDate?: string | null;
  separationReason?: string | null;
  clearSeparation?: boolean;
  restDays?: number[] | null;
  clearRestDays?: boolean;
  branchId?: string | null;
  clearHomeBranch?: boolean;
  canCanvassAndPaySuppliers?: boolean | null;
  canOpenLoaPdf?: boolean | null;
  canSeeLoaPesos?: boolean | null;
  mustClock?: boolean | null;
  payBasis?: PayBasis | null;
  ratePhp?: number | null;
  otAfterHoursPerDay?: number | null;
  otMultiplier?: number | null;
  clearPayBasis?: boolean;
  clearRate?: boolean;
}

export type EmploymentDocType = 'offer' | 'contract' | 'id' | 'other' | 'coe_request_note';

export interface EmploymentDocument {
  id: string;
  docType: EmploymentDocType;
  fileName: string;
  contentType: string;
  byteSize: number;
  note: string | null;
  createdAtUtc: string;
}

@Injectable({ providedIn: 'root' })
export class EmployeeDirectoryApi {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  list(includeInactive: boolean, search = ''): Observable<EmployeeDirectoryList> {
    let params = new HttpParams().set('includeInactive', String(includeInactive));
    const term = search.trim();
    if (term) {
      params = params.set('search', term);
    }
    return this.http
      .get<EmployeeDirectoryList | EmployeeCard[]>(`${this.base}/api/employee-directory`, {
        params,
      })
      .pipe(
        map((body) =>
          Array.isArray(body)
            ? {
                headcount: body.filter((person) => person.active).length,
                items: body,
                configHealth: [],
              }
            : {
                headcount: body.headcount,
                items: body.items ?? [],
                configHealth: body.configHealth ?? [],
              },
        ),
      );
  }

  get(id: string): Observable<EmployeeCard> {
    return this.http.get<EmployeeCard>(`${this.base}/api/employee-directory/${id}`);
  }

  create(body: CreateEmployee): Observable<EmployeeCard> {
    return this.http.post<EmployeeCard>(`${this.base}/api/employee-directory`, body);
  }

  update(id: string, body: UpdateEmployee): Observable<EmployeeCard> {
    return this.http.patch<EmployeeCard>(`${this.base}/api/employee-directory/${id}`, body);
  }

  /** Open jobs, pending and approved leave, and the open punch. Owner and admin. */
  openWork(staffId: string): Observable<StaffOpenWork> {
    return this.http.get<StaffOpenWork>(`${this.base}/api/ops/staff/${staffId}/open-work`);
  }

  /** Moves the listed jobs off this person in one server transaction. */
  handover(
    staffId: string,
    body: { moves: HandoverMove[]; reason: HandoverReason },
  ): Observable<HandoverResult> {
    return this.http.post<HandoverResult>(`${this.base}/api/ops/staff/${staffId}/handover`, body);
  }

  listDocuments(staffId: string): Observable<EmploymentDocument[]> {
    return this.http.get<EmploymentDocument[]>(
      `${this.base}/api/employee-directory/${staffId}/documents`,
    );
  }

  listMine(): Observable<EmploymentDocument[]> {
    return this.http.get<EmploymentDocument[]>(`${this.base}/api/employee-directory/me/documents`);
  }

  upload(
    staffId: string,
    file: File,
    docType: EmploymentDocType,
    note: string | null,
  ): Observable<EmploymentDocument> {
    const body = new FormData();
    body.append('file', file, file.name);
    body.append('docType', docType);
    if (note) {
      body.append('note', note);
    }
    return this.http.post<EmploymentDocument>(
      `${this.base}/api/employee-directory/${staffId}/documents`,
      body,
    );
  }

  download(id: string): Observable<Blob> {
    return this.http.get(`${this.base}/api/employee-documents/${id}`, { responseType: 'blob' });
  }

  /** Append-only people/HR trail for one person, newest first. */
  listChangeLog(staffId: string, take = 200): Observable<HrChangeLogList> {
    const params = new HttpParams().set('staffId', staffId).set('take', String(take));
    return this.http.get<HrChangeLogList>(`${this.base}/api/hr/change-log`, { params });
  }
}
