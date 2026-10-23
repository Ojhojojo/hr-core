import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { FALLBACK_SHOP } from '@hr-host';
import { buildPayslipPdf, payslipLoanLines, payslipViewFromApi } from '../payroll/payroll-payslip';
import { PayrollApi } from '../payroll/payroll.api';
import { MyPayslipDetail, MyPayslipSummary } from '../payroll/payroll.models';
import { myPayslipDetailToPayslip } from './my-payslip.format';

function setup() {
  TestBed.configureTestingModule({
    providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
  });
  return { api: TestBed.inject(PayrollApi), http: TestBed.inject(HttpTestingController) };
}

/** A saved payslip line from a locked run, frozen at lock time. */
const saved: MyPayslipDetail = {
  runId: 'run-1',
  lineId: 'line-1',
  periodFrom: '2026-09-14',
  periodTo: '2026-09-20',
  lockedAtUtc: '2026-09-21T02:00:00Z',
  lockedByName: 'Maria Reyes',
  staffId: 'tech-1',
  staffName: 'Danilo Santos',
  basis: 'hourly',
  ratePhp: 120,
  regularHours: 40,
  otHours: 2,
  otPay: 300,
  daysPaid: 5,
  gross: 5100,
  sss: 200,
  philHealth: 100,
  pagIbig: 50,
  wht: 0,
  other: 0,
  deductions: 750,
  net: 4350,
  holidayHours: 8,
  restDayHours: 0,
  nightHours: 4,
  premiumPay: 180,
  items: [
    { id: 'item-1', kind: 'allowance', label: 'Rice subsidy', amount: 500, taxable: true },
    { id: 'item-2', kind: 'loan', label: 'SSS loan', amount: 200, balanceAfter: 1800 },
    { id: 'item-3', kind: 'loan', label: 'Cash advance', amount: 100, balanceAfter: 900 },
  ],
};

const summaries: MyPayslipSummary[] = [
  {
    runId: 'run-1',
    lineId: 'line-1',
    periodFrom: '2026-09-14',
    periodTo: '2026-09-20',
    gross: 5100,
    deductions: 750,
    net: 4350,
    lockedAtUtc: '2026-09-21T02:00:00Z',
  },
  {
    runId: 'run-0',
    lineId: 'line-0',
    periodFrom: '2026-09-07',
    periodTo: '2026-09-13',
    gross: 4800,
    deductions: 350,
    net: 4450,
    lockedAtUtc: '2026-09-14T02:00:00Z',
  },
];

describe('PayrollApi my payslip history requests', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('GETs the caller locked payslip list from /me/payslips', () => {
    const { api, http } = setup();

    api.listMyPayslips().subscribe();
    const list = http.expectOne((r) => r.url === '/api/ops/payroll/me/payslips');
    expect(list.request.method).toBe('GET');
    expect(list.request.params.keys()).toEqual([]);
    list.flush(summaries);

    http.verify();
  });

  it('GETs one saved payslip from /me/payslips/{lineId}', () => {
    const { api, http } = setup();

    api.getMyPayslipLine('line-1').subscribe();
    const detail = http.expectOne((r) => r.url === '/api/ops/payroll/me/payslips/line-1');
    expect(detail.request.method).toBe('GET');
    detail.flush(saved);

    http.verify();
  });
});

describe('myPayslipDetailToPayslip', () => {
  it('maps the saved line to a locked payslip without recalculating anything', () => {
    const slip = myPayslipDetailToPayslip(saved);

    expect(slip.source).toBe('locked');
    expect(slip.from).toBe('2026-09-14');
    expect(slip.to).toBe('2026-09-20');
    expect(slip.staffName).toBe('Danilo Santos');
    expect(slip.gross).toBe(5100);
    expect(slip.net).toBe(4350);
    expect(slip.lockedByName).toBe('Maria Reyes');
    expect(slip.lockedAtUtc).toBe('2026-09-21T02:00:00Z');
  });

  it('keeps the frozen allowance and loan items with their stored amounts', () => {
    const slip = myPayslipDetailToPayslip(saved);

    expect(slip.allowanceItems).toEqual([{ name: 'Rice subsidy', amount: 500 }]);
    expect(slip.loanItems).toEqual([
      { label: 'SSS loan', instalment: 200, loanBalance: 1800 },
      { label: 'Cash advance', instalment: 100, loanBalance: 900 },
    ]);
    expect(payslipLoanLines(slip).map((line) => line.label)).toEqual([
      'SSS loan',
      'Loan balance',
      'Cash advance',
      'Loan balance',
    ]);
  });

  it('prints the mapped saved payslip as a payslip PDF', async () => {
    const slip = myPayslipDetailToPayslip(saved);
    const view = payslipViewFromApi(slip);
    expect(view.staffName).toBe('Danilo Santos');
    expect(view.basis).toBe('Hourly');
    expect(view.grossAmount).toBe(5100);

    const blob = buildPayslipPdf(FALLBACK_SHOP, slip.from, slip.to, [view], null);
    expect(blob.type).toBe('application/pdf');
    const header = new TextDecoder().decode(await blob.slice(0, 4).arrayBuffer());
    expect(header).toBe('%PDF');
  });
});
