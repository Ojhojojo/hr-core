import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@hr-host';
import { Announcement } from './announcement-visibility';

export interface AnnouncementWrite {
  title: string;
  body: string;
  startDate: string;
  endDate: string;
  active: boolean;
}

@Injectable({ providedIn: 'root' })
export class AnnouncementsApi {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  listToday(): Observable<Announcement[]> {
    return this.http.get<Announcement[]>(`${this.base}/api/announcements/today`);
  }

  listAll(): Observable<Announcement[]> {
    return this.http.get<Announcement[]>(`${this.base}/api/announcements`);
  }

  create(body: AnnouncementWrite): Observable<Announcement> {
    return this.http.post<Announcement>(`${this.base}/api/announcements`, body);
  }

  update(id: string, body: AnnouncementWrite): Observable<Announcement> {
    return this.http.put<Announcement>(`${this.base}/api/announcements/${id}`, body);
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/api/announcements/${id}`);
  }
}
