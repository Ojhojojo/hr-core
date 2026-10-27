import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { OpsConfirmService } from '@hr-host';
import { Announcement } from './announcement-visibility';
import { AnnouncementsPage } from './announcements';

const mockAnnouncements: Announcement[] = [
  {
    id: 'ann-1',
    title: 'Holiday Schedule',
    body: 'We are closed on Monday.',
    startDate: '2026-10-01',
    endDate: '2026-10-10',
    active: true,
  },
];

describe('AnnouncementsPage', () => {
  afterEach(() => {
    try {
      const http = TestBed.inject(HttpTestingController, null, { optional: true });
      http?.verify();
    } finally {
      TestBed.resetTestingModule();
      document.querySelectorAll('.cdk-overlay-container').forEach((el) => el.remove());
    }
  });

  async function mount() {
    await TestBed.configureTestingModule({
      imports: [AnnouncementsPage],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    }).compileComponents();

    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(AnnouncementsPage);
    fixture.detectChanges();

    const req = http.expectOne('/api/announcements');
    req.flush(mockAnnouncements);
    fixture.detectChanges();
    await fixture.whenStable();

    return { http, fixture, host: fixture.nativeElement as HTMLElement };
  }

  it('removes an announcement when Remove is clicked', async () => {
    const { http, fixture, host } = await mount();

    const removeBtn = Array.from(host.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Remove'),
    );
    expect(removeBtn).toBeTruthy();
    const confirm = vi
      .spyOn(TestBed.inject(OpsConfirmService), 'confirm')
      .mockReturnValue(of({ confirmed: true }));
    removeBtn!.click();
    expect(confirm.mock.calls[0][0]).toMatchObject({ tone: 'danger', confirmLabel: 'Remove' });
    fixture.detectChanges();

    const delReq = http.expectOne(
      (r) => r.method === 'DELETE' && r.url === '/api/announcements/ann-1',
    );
    delReq.flush({ ok: true });

    const reloadReq = http.expectOne('/api/announcements');
    reloadReq.flush([]);
  });
});
