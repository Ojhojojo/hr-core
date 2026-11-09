import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { afterEach, describe, expect, it } from 'vitest';
import { PayrollApi } from './payroll.api';
import { RemittanceReport as RemittanceReportModel } from './payroll.models';
import { RemittanceReport } from './remittance-report';
import { remittanceCsv } from './remittance-report.format';

const off: RemittanceReportModel = {
  branchId: 'branch-north',
  month: '2026-11',
  enabled: false,
  agencies: [],
  employeeShare: 0,
  employerShare: 0,
  total: 0,
};

const november: RemittanceReportModel = {
  branchId: 'branch-north',
  month: '2026-11',
  enabled: true,
  agencies: [
    { key: 'sss', label: 'SSS', employeeShare: 1000, employerShare: 2030, total: 3030 },
    { key: 'philhealth', label: 'PhilHealth', employeeShare: 500, employerShare: 500, total: 1000 },
    { key: 'pagibig', label: 'Pag-IBIG', employeeShare: 200, employerShare: 200, total: 400 },
  ],
  employeeShare: 1700,
  employerShare: 2730,
  total: 4430,
};

const empty: RemittanceReportModel = {
  branchId: 'branch-north',
  month: '2026-09',
  enabled: true,
  agencies: [
    { key: 'sss', label: 'SSS', employeeShare: 0, employerShare: 0, total: 0 },
    { key: 'philhealth', label: 'PhilHealth', employeeShare: 0, employerShare: 0, total: 0 },
    { key: 'pagibig', label: 'Pag-IBIG', employeeShare: 0, employerShare: 0, total: 0 },
  ],
  employeeShare: 0,
  employerShare: 0,
  total: 0,
  emptyMessage: 'There is nothing to remit yet.',
};

describe('remittance CSV', () => {
  it('matches the November 2025 salary of 20000 on screen and in the file', () => {
    const csv = remittanceCsv(november);
    expect(csv).toBe(
      'Agency,Employee share,Employer share,Total\n' +
        'SSS,1000.00,2030.00,3030.00\n' +
        'PhilHealth,500.00,500.00,1000.00\n' +
        'Pag-IBIG,200.00,200.00,400.00\n' +
        'Total,1700.00,2730.00,4430.00\n',
    );
    expect(sumColumn(csv, 1)).toBe(november.employeeShare);
    expect(sumColumn(csv, 2)).toBe(november.employerShare);
    expect(sumColumn(csv, 3)).toBe(november.total);
  });
});

describe('PayrollApi remittance', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('gets and saves the report and requests the CSV export', () => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    const api = TestBed.inject(PayrollApi);
    const http = TestBed.inject(HttpTestingController);

    api.getRemittance('2026-11').subscribe();
    const get = http.expectOne((r) => r.url.includes('/api/ops/payroll/remittance'));
    expect(get.request.method).toBe('GET');
    expect(get.request.params.get('month')).toBe('2026-11');
    get.flush(november);

    api.saveRemittance({ enabled: true }).subscribe();
    const put = http.expectOne((r) => r.url.endsWith('/api/ops/payroll/remittance'));
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ enabled: true });
    put.flush({ branchId: 'branch-north', enabled: true });

    api.exportRemittance('2026-11').subscribe();
    const exported = http.expectOne((r) => r.url.includes('/api/ops/payroll/remittance/export'));
    expect(exported.request.method).toBe('GET');
    expect(exported.request.params.get('month')).toBe('2026-11');
    exported.flush(new Blob([remittanceCsv(november)]));
    http.verify();
  });
});

describe('Remittance report screen', () => {
  afterEach(() => TestBed.resetTestingModule());

  async function mount() {
    await TestBed.configureTestingModule({
      imports: [RemittanceReport],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(RemittanceReport);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    fixture.componentInstance.load();
    fixture.detectChanges();
    return { fixture, http, host: fixture.nativeElement as HTMLElement };
  }

  it('shows a loading state, then the off switch', async () => {
    const { fixture, http, host } = await mount();
    expect(host.querySelector('[data-remittance-loading]')).toBeTruthy();
    http.expectOne((r) => r.url.includes('/api/ops/payroll/remittance')).flush(off);
    fixture.detectChanges();
    expect(host.querySelector('[data-remittance-loading]')).toBeNull();
    expect(host.querySelector('[data-remittance-off]')?.textContent).toContain(
      "Locking a run keeps today's lines and payslips.",
    );
    expect(host.querySelector('[data-remittance-table]')).toBeNull();
    expect(host.querySelector('[data-remittance-empty]')).toBeNull();
    http.verify();
  });

  it('shows the load error inline and retries', async () => {
    const { fixture, http, host } = await mount();
    http
      .expectOne((r) => r.url.includes('/api/ops/payroll/remittance'))
      .flush('nope', { status: 500, statusText: 'Server error' });
    fixture.detectChanges();
    const retry = host.querySelector<HTMLButtonElement>('[name="retryRemittance"]');
    expect(retry).toBeTruthy();
    retry?.click();
    fixture.detectChanges();
    http.expectOne((r) => r.url.includes('/api/ops/payroll/remittance')).flush(off);
    fixture.detectChanges();
    expect(host.querySelector('[data-remittance-off]')).toBeTruthy();
    http.verify();
  });

  it('shows November totals and the CSV matches those amounts', async () => {
    const { fixture, http, host } = await mount();
    http.expectOne((r) => r.url.includes('/api/ops/payroll/remittance')).flush(november);
    fixture.detectChanges();

    expect(host.querySelector('[data-remittance-month]')?.textContent).toContain('November 2026');
    expect(money(host, 'sss', 'employee')).toBe(1000);
    expect(money(host, 'sss', 'employer')).toBe(2030);
    expect(money(host, 'sss', 'total')).toBe(3030);
    expect(money(host, 'philhealth', 'employee')).toBe(500);
    expect(money(host, 'pagibig', 'employer')).toBe(200);
    const total = host.querySelector('[data-remittance-total]');
    expect(parseMoney(total?.querySelector('[data-total]')?.textContent)).toBe(4430);

    const csv = remittanceCsv(november);
    expect(sumColumn(csv, 1)).toBe(
      money(host, 'sss', 'employee') +
        money(host, 'philhealth', 'employee') +
        money(host, 'pagibig', 'employee'),
    );
    expect(sumColumn(csv, 2)).toBe(2030 + 500 + 200);
    expect(sumColumn(csv, 3)).toBe(parseMoney(total?.querySelector('[data-total]')?.textContent));

    const blobs: Blob[] = [];
    const previous = URL.createObjectURL?.bind(URL);
    URL.createObjectURL = (blob: Blob) => {
      blobs.push(blob);
      return previous ? previous(blob) : 'blob:remittance';
    };
    host.querySelector<HTMLButtonElement>('[name="exportRemittance"]')?.click();
    expect(blobs.length).toBe(1);
    expect(await blobs[0].text()).toBe(csv);
    http.verify();
  });

  it('says there is nothing to remit yet when the month has no locked run', async () => {
    const { fixture, http, host } = await mount();
    http.expectOne((r) => r.url.includes('/api/ops/payroll/remittance')).flush(empty);
    fixture.detectChanges();
    expect(host.querySelector('[data-remittance-empty]')?.textContent).toContain(
      'There is nothing to remit yet.',
    );
    expect(host.querySelector('[data-remittance-table]')).toBeNull();
    expect(host.querySelector('[name="exportRemittance"]')).toBeNull();
    http.verify();
  });

  it('turns the report on and moves to the next month', async () => {
    const { fixture, http, host } = await mount();
    http.expectOne((r) => r.url.includes('/api/ops/payroll/remittance')).flush(off);
    fixture.detectChanges();

    host.querySelector<HTMLElement>('mat-slide-toggle')?.click();
    fixture.detectChanges();
    const put = http.expectOne(
      (r) => r.method === 'PUT' && r.url.includes('/api/ops/payroll/remittance'),
    );
    expect(put.request.body).toEqual({ enabled: true });
    put.flush({ branchId: 'branch-north', enabled: true });
    fixture.detectChanges();
    const again = http.expectOne(
      (r) => r.method === 'GET' && r.url.includes('/api/ops/payroll/remittance'),
    );
    again.flush(november);
    fixture.detectChanges();
    expect(host.querySelector('[data-remittance-table]')).toBeTruthy();

    host.querySelector<HTMLButtonElement>('[name="remittanceNextMonth"]')?.click();
    fixture.detectChanges();
    const next = http.expectOne(
      (r) => r.method === 'GET' && r.url.includes('/api/ops/payroll/remittance'),
    );
    expect(next.request.params.get('month')).toBe('2026-12');
    next.flush({ ...empty, month: '2026-12' });
    fixture.detectChanges();
    expect(host.querySelector('[data-remittance-month]')?.textContent).toContain('December 2026');
    http.verify();
  });
});

function money(
  host: HTMLElement,
  agency: string,
  field: 'employee' | 'employer' | 'total',
): number {
  const cell = host.querySelector(`[data-agency="${agency}"] [data-${field}]`);
  return parseMoney(cell?.textContent);
}

function parseMoney(text: string | null | undefined): number {
  return Number((text ?? '').replace(/[₱,\s]/g, ''));
}

function sumColumn(csv: string, index: number): number {
  const lines = csv.trim().split('\n').slice(1, -1);
  return lines.reduce((sum, line) => sum + Number(line.split(',')[index]), 0);
}
