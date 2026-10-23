import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@hr-host';
import {
  AttendanceRegisterQuery,
  AttendanceRegisterResponse,
  AttendanceStaffOption,
  ClosePunchBody,
  OvertimeListResponse,
  PunchForBody,
} from './attendance.models';
import { AttendancePunch } from '../clock/clock.models';

@Injectable({ providedIn: 'root' })
export class AttendanceApi {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  register(query: AttendanceRegisterQuery = {}): Observable<AttendanceRegisterResponse> {
    return this.http.get<AttendanceRegisterResponse>(`${this.base}/api/ops/attendance/register`, {
      params: toParams(query),
    });
  }

  downloadCsv(query: AttendanceRegisterQuery = {}): Observable<HttpResponse<Blob>> {
    return this.http.get(`${this.base}/api/ops/attendance/register.csv`, {
      params: toParams(query),
      observe: 'response',
      responseType: 'blob',
    });
  }

  listStaff(): Observable<AttendanceStaffOption[]> {
    return this.http.get<AttendanceStaffOption[]>(`${this.base}/api/staff`);
  }

  punchFor(body: PunchForBody): Observable<AttendancePunch> {
    return this.http.post<AttendancePunch>(`${this.base}/api/ops/attendance/punch-for`, body);
  }

  closePunch(punchId: string, body: ClosePunchBody): Observable<AttendancePunch> {
    return this.http.post<AttendancePunch>(
      `${this.base}/api/ops/attendance/punches/${punchId}/close`,
      body,
    );
  }

  approve(punchId: string, note?: string | null): Observable<AttendancePunch> {
    return this.http.post<AttendancePunch>(
      `${this.base}/api/ops/attendance/punches/${punchId}/approve`,
      reviewBody(note),
    );
  }

  reject(punchId: string, note?: string | null): Observable<AttendancePunch> {
    return this.http.post<AttendancePunch>(
      `${this.base}/api/ops/attendance/punches/${punchId}/reject`,
      reviewBody(note),
    );
  }

  overtime(from?: string, to?: string, mine?: boolean): Observable<OvertimeListResponse> {
    let params = new HttpParams();
    if (from) {
      params = params.set('from', from);
    }
    if (to) {
      params = params.set('to', to);
    }
    if (mine) {
      params = params.set('mine', 'true');
    }
    return this.http.get<OvertimeListResponse>(`${this.base}/api/ops/attendance/overtime`, {
      params,
    });
  }

  fileOvertime(body: { workDate: string; hours: number; reason: string }): Observable<unknown> {
    return this.http.post(`${this.base}/api/ops/attendance/overtime`, body);
  }

  approveOvertime(id: string, hours: number, note?: string): Observable<unknown> {
    return this.http.post(`${this.base}/api/ops/attendance/overtime/${id}/approve`, {
      hours,
      note,
    });
  }

  rejectOvertime(id: string, note?: string): Observable<unknown> {
    return this.http.post(`${this.base}/api/ops/attendance/overtime/${id}/reject`, { note });
  }

  photo(punchId: string, side: 'in' | 'out'): Observable<Blob> {
    return this.http.get(`${this.base}/api/ops/attendance/punches/${punchId}/photos/${side}`, {
      responseType: 'blob',
    });
  }
}

function toParams(query: AttendanceRegisterQuery): HttpParams {
  let params = new HttpParams();
  if (query.from) {
    params = params.set('from', query.from);
  }
  if (query.to) {
    params = params.set('to', query.to);
  }
  if (query.staffId) {
    params = params.set('staffId', query.staffId);
  }
  if (query.scope && query.scope !== 'shop') {
    params = params.set('scope', query.scope);
  }
  if (query.approval) {
    params = params.set('approval', query.approval);
  }
  return params;
}

function reviewBody(note?: string | null): { note?: string } {
  const trimmed = note?.trim() ?? '';
  return trimmed ? { note: trimmed } : {};
}
