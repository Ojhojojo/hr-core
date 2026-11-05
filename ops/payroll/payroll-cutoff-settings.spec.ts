import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { afterEach, describe, expect, it } from 'vitest';
import { PayrollApi } from './payroll.api';
import { PayrollCutoffSettings } from './payroll-cutoff-settings';
import { PayrollCutoffSchedule } from './payroll.models';

const freeSchedule: PayrollCutoffSchedule = {
  branchId: 'branch-north',
  mode: 'free',
  cutoffs: [],
  month: '2026-11',
  periods: [],
};

const scheduled: PayrollCutoffSchedule = {
  branchId: 'branch-north',
  mode: 'schedule',
  month: '2026-11',
  cutoffs: [
    { ordinal: 1, startDay: 26, endDay: 10 },
    { ordinal: 2, startDay: 11, endDay: 25 },
  ],
  periods: [
    { ordinal: 1, from: '2026-10-26', to: '2026-11-10' },
    { ordinal: 2, from: '2026-11-11', to: '2026-11-25' },
  ],
};

describe('PayrollApi cut-off schedule', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
  });

  function setup() {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    return { api: TestBed.inject(PayrollApi), http: TestBed.inject(HttpTestingController) };
  }

  it('gets the schedule for a month', () => {
    const { api, http } = setup();
    api.getCutoffSchedule('2026-11').subscribe();
    const req = http.expectOne((r) => r.url.includes('/api/ops/payroll/cutoff-schedule'));
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('month')).toBe('2026-11');
    req.flush(freeSchedule);
    http.verify();
  });

  it('puts the schedule', () => {
    const { api, http } = setup();
    const body = {
      mode: 'schedule' as const,
      cutoffs: scheduled.cutoffs,
    };
    api.saveCutoffSchedule(body, '2026-11').subscribe();
    const req = http.expectOne((r) => r.url.includes('/api/ops/payroll/cutoff-schedule'));
    expect(req.request.method).toBe('PUT');
    expect(req.request.params.get('month')).toBe('2026-11');
    expect(req.request.body).toEqual(body);
    req.flush(scheduled);
    http.verify();
  });
});

describe('PayrollCutoffSettings', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
  });

  async function mount() {
    await TestBed.configureTestingModule({
      imports: [PayrollCutoffSettings],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(PayrollCutoffSettings);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    return { fixture, http, host: fixture.nativeElement as HTMLElement };
  }

  it('shows a loading state, then free dates', async () => {
    const { fixture, http, host } = await mount();
    (fixture.componentInstance as { load(): void }).load();
    fixture.detectChanges();
    expect(host.querySelector('[data-cutoff-loading]')).toBeTruthy();

    const req = http.expectOne((r) => r.url.includes('/api/ops/payroll/cutoff-schedule'));
    req.flush(freeSchedule);
    fixture.detectChanges();

    expect(host.querySelector('[data-cutoff-loading]')).toBeNull();
    expect(host.textContent).toContain('Free dates');
    expect(host.textContent).toContain('November 2026');
    http.verify();
  });

  it('shows the load error inline and retries', async () => {
    const { fixture, http, host } = await mount();
    (fixture.componentInstance as { load(): void }).load();
    fixture.detectChanges();
    http
      .expectOne((r) => r.url.includes('/api/ops/payroll/cutoff-schedule'))
      .flush('down', { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();

    const retry = host.querySelector('button[name="retryCutoffSchedule"]') as HTMLButtonElement;
    expect(retry).toBeTruthy();
    expect(host.textContent).toContain('Retry');
    retry.click();
    fixture.detectChanges();

    const again = http.expectOne((r) => r.url.includes('/api/ops/payroll/cutoff-schedule'));
    again.flush(freeSchedule);
    fixture.detectChanges();
    expect(host.querySelector('button[name="retryCutoffSchedule"]')).toBeNull();
    expect(host.textContent).toContain('Free dates');
    http.verify();
  });

  it('shows a 400 field error under the cut-off', async () => {
    const { fixture, http, host } = await mount();
    const cmp = fixture.componentInstance as unknown as {
      load(): void;
      mode: { set(value: string): void };
      cutoffRows: { set(value: { startDay: number | null; endDay: number | null }[]): void };
      save(): void;
    };
    cmp.load();
    http.expectOne((r) => r.url.includes('/api/ops/payroll/cutoff-schedule')).flush(scheduled);
    fixture.detectChanges();

    cmp.mode.set('schedule');
    cmp.cutoffRows.set([
      { startDay: 26, endDay: 10 },
      { startDay: 10, endDay: 25 },
    ]);
    fixture.detectChanges();
    cmp.save();
    const put = http.expectOne((r) => r.method === 'PUT');
    expect(put.request.body).toEqual({
      mode: 'schedule',
      cutoffs: [
        { ordinal: 1, startDay: 26, endDay: 10 },
        { ordinal: 2, startDay: 10, endDay: 25 },
      ],
    });
    put.flush(
      { errors: { 'cutoffs[1].startDay': ['Day 10 is already in cut-off 1.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    const field = host.querySelector('[data-field="cutoffs[1].startDay"]');
    expect(field?.textContent).toContain('Day 10 is already in cut-off 1.');
    http.verify();
  });

  it('lists the generated periods for the month', async () => {
    const { fixture, http, host } = await mount();
    (fixture.componentInstance as { load(): void }).load();
    http.expectOne((r) => r.url.includes('/api/ops/payroll/cutoff-schedule')).flush(scheduled);
    fixture.detectChanges();

    const first = host.querySelector('[data-from="2026-10-26"]');
    expect(first?.getAttribute('data-to')).toBe('2026-11-10');
    expect(first?.textContent).toContain('Oct 26 – Nov 10, 2026');

    // Same month: formatDayRange prints "Nov 11 – 25, 2026".
    const second = host.querySelector('[data-to="2026-11-25"]');
    expect(second?.getAttribute('data-from')).toBe('2026-11-11');
    expect(second?.textContent).toContain('Nov 11 – 25, 2026');
    http.verify();
  });
});
