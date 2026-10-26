import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ShopInfo } from '@hr-host';
import {
  PayslipDialog,
  buildPayslipPdf,
  buildPayslipView,
  payslipFileName,
  payslipViewFromApi,
} from './payroll-payslip';
import { Payslip } from './payroll.models';
import { buildThirteenthPdf, thirteenthFileName } from './payroll-thirteenth-print';

const shop: ShopInfo = {
  tradingName: 'Rakkii Cars',
  legalName: 'RAKKII CARS OPC',
  address: '249 Roosevelt',
  phone: '(02) 7001-9011',
  facebook: '',
  email: '',
};

describe('payslip PDF helpers', () => {
  it('builds a PDF blob and names the file with a .pdf suffix', async () => {
    const slip = buildPayslipView(
      {
        staffId: 'staff-1',
        staffName: 'Danilo Santos',
        role: 'technician',
        basis: 'hourly',
        ratePhp: 120,
        regularHours: 40,
        otHours: 2,
        daysPaid: 5,
        amount: 5000,
      },
      { sss: 200, philHealth: 100, pagIbig: 50, wht: 0, other: 0 },
    );
    expect(payslipFileName('2026-09-14', [slip])).toBe('payslip-danilo-santos-2026-09-14.pdf');
    expect(payslipFileName('2026-09-14', [slip, slip])).toBe('payslips-2026-09-14.pdf');

    const blob = buildPayslipPdf(shop, '2026-09-14', '2026-09-20', [slip], null);
    expect(blob.type).toBe('application/pdf');
    expect(blob.size).toBeGreaterThan(100);
    const header = new TextDecoder().decode(await blob.slice(0, 4).arrayBuffer());
    expect(header).toBe('%PDF');
  });

  it('maps a stored Payslip into a printable PayslipView', () => {
    const slip: Payslip = {
      staffId: 'staff-1',
      staffName: 'Danilo Santos',
      from: '2026-09-14',
      to: '2026-09-20',
      basis: 'monthly',
      ratePhp: 30000,
      regularHours: 0,
      otHours: 0,
      daysPaid: 15,
      gross: 15000,
      sss: 500,
      philHealth: 200,
      pagIbig: 100,
      wht: 0,
      other: 0,
      deductions: 800,
      net: 14200,
      source: 'locked',
    };
    const view = payslipViewFromApi(slip);
    expect(view.rateLabel).toBe('Monthly salary');
    expect(view.grossLabel).toBe('Salary gross');
    expect(view.grossAmount).toBe(15000);
    expect(view.lines).toHaveLength(3);
  });

  it('builds a 13th-month PDF blob', async () => {
    expect(thirteenthFileName(2026)).toBe('thirteenth-month-2026.pdf');
    const blob = buildThirteenthPdf({
      shop,
      year: 2026,
      rows: [
        {
          staffId: 's1',
          staffName: 'Mina Office',
          role: 'admin',
          branch: 'Roosevelt',
          hireDate: '2024-01-01',
          payBasis: 'monthly',
          ratePhp: 30000,
          basicEarnedAuto: 30000,
          basicEarned: 30000,
          thirteenthMonth: 2500,
          usedOverride: false,
        },
      ],
      lockLine: null,
    });
    expect(blob.type).toBe('application/pdf');
    const header = new TextDecoder().decode(await blob.slice(0, 4).arrayBuffer());
    expect(header).toBe('%PDF');
  });
});

describe('PayslipDialog', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
    document.querySelectorAll('.cdk-overlay-container').forEach((el) => el.remove());
  });

  it('issues a payslip from the modal and has no Download button', async () => {
    const close = vi.fn();
    const slip = buildPayslipView(
      {
        staffId: 'staff-1',
        staffName: 'Danilo Santos',
        role: 'technician',
        basis: 'hourly',
        ratePhp: 120,
        regularHours: 40,
        otHours: 0,
        daysPaid: 5,
        amount: 4800,
      },
      { sss: 100, philHealth: 0, pagIbig: 0, wht: 0, other: 0 },
    );

    await TestBed.configureTestingModule({
      imports: [PayslipDialog],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: MAT_DIALOG_DATA,
          useValue: {
            shop,
            periodFrom: '2026-09-14',
            periodTo: '2026-09-20',
            slips: [slip],
            issue: { staffId: 'staff-1', issuedLine: '' },
          },
        },
        { provide: MatDialogRef, useValue: { close } },
      ],
    }).compileComponents();

    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PayslipDialog);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(document.body.querySelector('button[name="downloadPayslip"]')).toBeNull();
    expect(document.body.querySelector('button[name="printPayslip"]')).toBeTruthy();
    expect(document.body.querySelector('button[name="issuePayslip"]')).toBeTruthy();

    (document.body.querySelector('button[name="issuePayslip"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    const req = http.expectOne(
      (r) => r.method === 'POST' && r.url.includes('/api/ops/payroll/payslips/issue'),
    );
    expect(req.request.body).toEqual({
      staffId: 'staff-1',
      from: '2026-09-14',
      to: '2026-09-20',
    });
    req.flush({
      staffId: 'staff-1',
      staffName: 'Danilo Santos',
      from: '2026-09-14',
      to: '2026-09-20',
      regularHours: 40,
      otHours: 0,
      daysPaid: 5,
      gross: 4800,
      issuedAtUtc: '2026-09-21T02:00:00Z',
    });
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Issued');
  });

  it('hides Issue on bulk payslips', async () => {
    const slip = buildPayslipView(
      {
        staffId: 'staff-1',
        staffName: 'Danilo Santos',
        role: 'technician',
        basis: 'hourly',
        ratePhp: 120,
        regularHours: 40,
        otHours: 0,
        daysPaid: 5,
        amount: 4800,
      },
      undefined,
    );

    await TestBed.configureTestingModule({
      imports: [PayslipDialog],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: MAT_DIALOG_DATA,
          useValue: {
            shop,
            periodFrom: '2026-09-14',
            periodTo: '2026-09-20',
            slips: [slip, slip],
          },
        },
        { provide: MatDialogRef, useValue: { close: vi.fn() } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(PayslipDialog);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(document.body.querySelector('button[name="issuePayslip"]')).toBeNull();
    expect(document.body.querySelector('button[name="printPayslip"]')).toBeTruthy();
    TestBed.inject(HttpTestingController).verify();
  });
});
