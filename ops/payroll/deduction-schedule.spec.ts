import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { afterEach, describe, expect, it } from 'vitest';
import { DeductionSchedule } from './deduction-schedule';
import { PayrollApi } from './payroll.api';

describe('PayrollApi deduction schedule', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('gets and saves the schedule and a catch-up', () => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    const api = TestBed.inject(PayrollApi);
    const http = TestBed.inject(HttpTestingController);

    api.getDeductionSchedule().subscribe();
    const get = http.expectOne((r) => r.url.endsWith('/api/ops/payroll/deduction-schedule'));
    expect(get.request.method).toBe('GET');
    get.flush({ branchId: 'branch-north', cutoffCount: 2, rules: [] });

    api
      .saveDeductionSchedule({
        rules: [{ deductionKey: 'sss', mode: 'cutoff', cutoffOrdinal: 2 }],
      })
      .subscribe();
    const put = http.expectOne((r) => r.url.endsWith('/api/ops/payroll/deduction-schedule'));
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({
      rules: [{ deductionKey: 'sss', mode: 'cutoff', cutoffOrdinal: 2 }],
    });
    put.flush({
      branchId: 'branch-north',
      cutoffCount: 2,
      rules: [{ deductionKey: 'sss', mode: 'cutoff', cutoffOrdinal: 2 }],
    });

    api
      .saveCatchUp({
        staffId: 'staff-1',
        periodFrom: '2026-11-11',
        periodTo: '2026-11-25',
        deductionKey: 'sss',
        amount: 150.5,
        note: 'Missed the earlier period',
      })
      .subscribe();
    const catchUp = http.expectOne((r) => r.url.endsWith('/api/ops/payroll/catch-ups'));
    expect(catchUp.request.body.periodFrom).toBe('2026-11-11');
    expect(catchUp.request.body.note).toBe('Missed the earlier period');
    catchUp.flush({
      id: 'c1',
      staffId: 'staff-1',
      periodFrom: '2026-11-11',
      periodTo: '2026-11-25',
      deductionKey: 'sss',
      amount: 150.5,
      note: 'Missed the earlier period',
    });
    http.verify();
  });
});

describe('DeductionSchedule screen', () => {
  afterEach(() => TestBed.resetTestingModule());

  async function mount() {
    await TestBed.configureTestingModule({
      imports: [DeductionSchedule],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(DeductionSchedule);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    return { fixture, http, host: fixture.nativeElement as HTMLElement };
  }

  it('shows loading, then prorate, and a field error from a 400', async () => {
    const { fixture, http, host } = await mount();
    fixture.componentInstance.load();
    fixture.detectChanges();
    expect(host.querySelector('[data-deduction-loading]')).toBeTruthy();

    http
      .expectOne((r) => r.url.includes('/deduction-schedule'))
      .flush('down', { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    const retry = host.querySelector('button[name="retryDeductionSchedule"]') as HTMLButtonElement;
    expect(retry).toBeTruthy();
    retry.click();

    http
      .expectOne((r) => r.url.includes('/deduction-schedule'))
      .flush({
        branchId: 'branch-north',
        cutoffCount: 2,
        rules: [],
      });
    fixture.detectChanges();
    expect(host.textContent).toContain('SSS');
    expect(host.textContent).toContain('PhilHealth');
    expect(host.textContent).toContain('Pag-IBIG');
    expect(host.textContent).toContain('Withholding tax');
    expect(host.textContent).toContain('Prorate');

    const cmp = fixture.componentInstance as unknown as {
      save(): void;
      setMode(index: number, mode: string): void;
    };
    cmp.setMode(0, 'cutoff');
    fixture.detectChanges();
    cmp.save();
    const put = http.expectOne((r) => r.method === 'PUT');
    put.flush(
      { errors: { 'rules[0].cutoffOrdinal': ['Cut-off 3 does not exist. This shop has 2.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();
    // The form field reads errorState after the input's ngDoCheck, so refresh once more.
    fixture.detectChanges();
    const error = host.querySelector('mat-error');
    expect(error?.textContent).toContain('Cut-off 3 does not exist');
    expect(error?.closest('mat-form-field')?.textContent).toContain('Cut-off');
    expect(error?.getAttribute('role')).toBe('alert');
    expect(
      error?.closest('mat-form-field')?.querySelector('input')?.getAttribute('aria-invalid'),
    ).toBe('true');
    http.verify();
  });
});
