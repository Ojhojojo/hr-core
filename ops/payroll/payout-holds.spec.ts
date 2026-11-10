import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { afterEach, describe, expect, it } from 'vitest';
import { of } from 'rxjs';
import { OpsConfirmService } from '@hr-host';
import { PayoutHoldSettings, payoutHoldProblem } from './payout-holds';

describe('payoutHoldProblem', () => {
  it('reads a field error before the problem detail', () => {
    expect(
      payoutHoldProblem({
        error: {
          title: 'One or more validation errors occurred.',
          status: 400,
          errors: { reason: ['A reason is required.'] },
        },
      }),
    ).toBe('A reason is required.');
    expect(
      payoutHoldProblem({
        error: { detail: 'This screen is not available for your role.' },
      }),
    ).toContain('not available');
  });
});

describe('Payout hold screen', () => {
  afterEach(() => TestBed.resetTestingModule());

  async function mount() {
    await TestBed.configureTestingModule({
      imports: [PayoutHoldSettings],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: OpsConfirmService, useValue: { confirm: () => of({ confirmed: true }) } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(PayoutHoldSettings);
    fixture.componentRef.setInput('staff', [
      { staffId: 'staff-1', staffName: 'Casey Diaz' },
      { staffId: 'staff-2', staffName: 'Dana Ruiz' },
    ]);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    return { fixture, http, host: fixture.nativeElement as HTMLElement };
  }

  it('holds a person and shows a missing reason from the API', async () => {
    const { fixture, http, host } = await mount();
    fixture.componentInstance.load();
    fixture.detectChanges();
    http.expectOne((r) => r.url.endsWith('/api/ops/payroll/payout-holds')).flush({ holds: [] });
    fixture.detectChanges();
    expect(host.textContent).toContain('No payouts are on hold.');

    const cmp = fixture.componentInstance as unknown as {
      staffId: { set(value: string): void };
      reason: { set(value: string): void };
      hold(): void;
    };
    cmp.staffId.set('staff-1');
    cmp.reason.set('   ');
    cmp.hold();
    const rejected = http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/payout-holds'));
    expect(rejected.request.body).toEqual({ staffId: 'staff-1', reason: '   ' });
    rejected.flush(
      { status: 400, errors: { reason: ['A reason is required.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();
    expect(host.textContent).toContain('A reason is required.');

    cmp.reason.set('Clearance pending');
    cmp.hold();
    const saved = http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/payout-holds'));
    expect(saved.request.body).toEqual({ staffId: 'staff-1', reason: 'Clearance pending' });
    saved.flush({
      id: 'hold-1',
      staffId: 'staff-1',
      staffName: 'Casey Diaz',
      reason: 'Clearance pending',
      heldAtUtc: '2026-11-10T01:00:00Z',
      active: true,
    });
    const reload = http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/payout-holds'));
    reload.flush({
      holds: [
        {
          id: 'hold-1',
          staffId: 'staff-1',
          staffName: 'Casey Diaz',
          reason: 'Clearance pending',
          heldAtUtc: '2026-11-10T01:00:00Z',
          active: true,
        },
      ],
    });
    fixture.detectChanges();
    expect(host.textContent).toContain('Casey Diaz');
    expect(host.textContent).toContain('Clearance pending');
    http.verify();
  });

  it('releases a hold after confirm and shows the date', async () => {
    const { fixture, http, host } = await mount();
    fixture.componentInstance.load();
    fixture.detectChanges();
    http.expectOne((r) => r.method === 'GET').flush({
      holds: [
        {
          id: 'hold-1',
          staffId: 'staff-1',
          staffName: 'Casey Diaz',
          reason: 'Clearance pending',
          heldAtUtc: '2026-11-10T01:00:00Z',
          active: true,
        },
      ],
    });
    fixture.detectChanges();

    (host.querySelector('button[name="releasePayout"]') as HTMLButtonElement).click();
    const released = http.expectOne(
      (r) => r.method === 'POST' && r.url.endsWith('/payout-holds/staff-1/release'),
    );
    released.flush({
      id: 'hold-1',
      staffId: 'staff-1',
      staffName: 'Casey Diaz',
      reason: 'Clearance pending',
      heldAtUtc: '2026-11-10T01:00:00Z',
      releasedOn: '2026-11-10',
      releasedByName: 'Alex Rivera',
      active: false,
    });
    http.expectOne((r) => r.method === 'GET').flush({ holds: [] });
    fixture.detectChanges();
    expect(host.textContent).toContain('Released 2026-11-10 by Alex Rivera');
    expect(host.textContent).toContain('No payouts are on hold.');
    http.verify();
  });
});
