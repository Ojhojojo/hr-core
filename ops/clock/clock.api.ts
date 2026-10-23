import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@hr-host';
import { AttendanceMe, AttendancePunch, KioskStaff } from './clock.models';

@Injectable({ providedIn: 'root' })
export class ClockApi {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  me(): Observable<AttendanceMe> {
    return this.http.get<AttendanceMe>(`${this.base}/api/ops/attendance/me`);
  }

  clockIn(
    coords?: { latitude: number; longitude: number } | null,
    photo?: File | null,
    offsiteReason?: string | null,
  ): Observable<AttendancePunch> {
    return this.http.post<AttendancePunch>(
      `${this.base}/api/ops/attendance/clock-in`,
      punchBody(coords, photo, offsiteReason),
    );
  }

  clockOut(
    coords?: { latitude: number; longitude: number } | null,
    photo?: File | null,
    offsiteReason?: string | null,
  ): Observable<AttendancePunch> {
    return this.http.post<AttendancePunch>(
      `${this.base}/api/ops/attendance/clock-out`,
      punchBody(coords, photo, offsiteReason),
    );
  }

  kiosk(): Observable<KioskStaff[]> {
    return this.http.get<KioskStaff[]>(`${this.base}/api/ops/attendance/kiosk`);
  }

  kioskClockIn(staffId: string, photo?: File | null): Observable<AttendancePunch> {
    return this.http.post<AttendancePunch>(
      `${this.base}/api/ops/attendance/kiosk/${staffId}/clock-in`,
      photoBody(photo),
    );
  }

  kioskClockOut(staffId: string, photo?: File | null): Observable<AttendancePunch> {
    return this.http.post<AttendancePunch>(
      `${this.base}/api/ops/attendance/kiosk/${staffId}/clock-out`,
      photoBody(photo),
    );
  }
}

function photoBody(photo?: File | null): FormData | Record<string, never> {
  if (!photo) {
    return {};
  }

  const form = new FormData();
  form.append('photo', photo, photo.name);
  return form;
}

function punchBody(
  coords?: { latitude: number; longitude: number } | null,
  photo?: File | null,
  offsiteReason?: string | null,
): FormData | Record<string, unknown> {
  const reason = offsiteReason?.trim() ?? '';
  if (photo) {
    const form = new FormData();
    form.append('photo', photo, photo.name);
    if (coords) {
      form.append('latitude', String(coords.latitude));
      form.append('longitude', String(coords.longitude));
    }
    if (reason) {
      form.append('offsiteReason', reason);
    }
    return form;
  }

  const body: Record<string, unknown> = {};
  if (coords) {
    body['latitude'] = coords.latitude;
    body['longitude'] = coords.longitude;
  }
  if (reason) {
    body['offsiteReason'] = reason;
  }
  return body;
}
