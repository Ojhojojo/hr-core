import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { afterEach, describe, expect, it } from 'vitest';
import { of } from 'rxjs';
import { AuthMe, AuthService, ConfirmDialogData, OpsConfirmService } from '@hr-host';
import { PayrollPage } from './payroll';
import { PayrollApi } from './payroll.api';
import { buildPayslipView, payslipAllowanceLines, payslipLoanLines } from './payroll-payslip';
import {
  csvTextWithLockLine,
  manilaLockDate,
  payrollLockLine,
  rateSliceLabels,
} from './payroll-lock';
import {
  PayrollRatesResponse,
  PayrollRegisterResponse,
  StatutorySuggestion,
} from './payroll.models';

const ownerMe: AuthMe = {
  authUserId: 'owner-id',
  email: 'owner@rakkiicars.ph',
  profileKind: 'staff',
  staffRole: 'owner',
  branchId: null,
  branchCode: null,
  isStaff: true,
  displayName: 'Owner Apple',
};

const adminMe: AuthMe = {
  ...ownerMe,
  authUserId: 'admin-id',
  staffRole: 'admin',
  branchId: 'branch-1',
  branchCode: 'main-roosevelt',
  displayName: 'Admin Rezza',
};

const techMe: AuthMe = {
  ...ownerMe,
  authUserId: 'tech-id',
  staffRole: 'technician',
  branchId: 'branch-1',
  branchCode: 'main-roosevelt',
  displayName: 'Danilo Santos',
};

const salesMe: AuthMe = {
  ...ownerMe,
  authUserId: 'sales-id',
  staffRole: 'sales',
  displayName: 'Sales Sarah',
};

function authStub(me: AuthMe) {
  return {
    me: signal(me),
    loading: signal(false),
    isSignedIn: () => true,
    isStaff: () => true,
    refreshMe: async () => {},
  };
}

const mockPayrollData: PayrollRegisterResponse = {
  from: '2026-09-14',
  to: '2026-09-20',
  rows: [
    {
      staffId: 'staff-1',
      staffName: "Danilo 'Danny' Santos",
      role: 'technician',
      branchId: 'branch-1',
      branchName: 'Roosevelt Main',
      payBasis: 'hourly',
      ratePhp: 120,
      regularHours: 40,
      otHours: 5,
      daysPaid: 5,
      amount: 5550,
      notes: '5h OT',
      active: true,
    },
    {
      staffId: 'staff-2',
      staffName: 'Rezza Desk',
      role: 'admin',
      branchId: 'branch-1',
      branchName: 'Roosevelt Main',
      payBasis: 'daily',
      ratePhp: 800,
      regularHours: 40,
      otHours: 0,
      daysPaid: 5,
      amount: 4000,
      notes: null,
      active: true,
    },
    {
      staffId: 'staff-3',
      staffName: 'Mina Office',
      role: 'admin',
      branchId: 'branch-1',
      branchName: 'Roosevelt Main',
      payBasis: 'monthly',
      ratePhp: 30000,
      regularHours: 0,
      otHours: 0,
      daysPaid: 15,
      amount: 15000,
      notes: null,
      active: true,
    },
  ],
};

function flushThirteenth(http: HttpTestingController, year = 2026): void {
  const req = http.expectOne((r) => r.url.includes('/api/ops/payroll/thirteenth-month'));
  req.flush({ year, rows: [], totalThirteenthMonth: 0 });
}

const lockedRegister: PayrollRegisterResponse = {
  ...mockPayrollData,
  source: 'locked',
  runId: 'run-1',
  lockedAtUtc: '2026-10-07T02:00:00Z',
  lockedByName: 'Owner Apple',
  rows: mockPayrollData.rows.map((row) => ({
    ...row,
    sss: 500,
    deductions: 500,
    net: row.amount - 500,
  })),
};

const ratesResponse: PayrollRatesResponse = {
  staffId: 'staff-1',
  staffName: "Danilo 'Danny' Santos",
  branchId: 'branch-1',
  branchName: 'Roosevelt Main',
  basis: 'hourly',
  ratePhp: 120,
  otAfterHoursPerDay: 8,
  otMultiplier: 1,
  history: [
    {
      id: 'h-1',
      effectiveFrom: '2026-01-01',
      basis: 'hourly',
      ratePhp: 100,
      otAfterHoursPerDay: 8,
      otMultiplier: 1,
      createdAtUtc: '2026-01-01T02:00:00Z',
      createdByName: 'Owner Apple',
      reason: 'Start rate',
    },
    {
      id: 'h-2',
      effectiveFrom: '2026-09-19',
      basis: 'hourly',
      ratePhp: 120,
      otAfterHoursPerDay: 8,
      otMultiplier: 1,
      createdAtUtc: '2026-09-19T02:00:00Z',
      createdByName: 'Owner Apple',
      reason: 'Raise',
    },
  ],
};

/** Auto-confirms the shared confirm dialog and records what it was asked. */
function confirmStub() {
  const calls: ConfirmDialogData[] = [];
  return {
    calls,
    service: {
      confirm: (data: ConfirmDialogData) => {
        calls.push(data);
        return of({ confirmed: true });
      },
    },
  };
}

async function mountPayroll(
  me: AuthMe = ownerMe,
  register: PayrollRegisterResponse = mockPayrollData,
) {
  const confirm = confirmStub();
  await TestBed.configureTestingModule({
    imports: [PayrollPage],
    providers: [
      provideZonelessChangeDetection(),
      provideNoopAnimations(),
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: AuthService, useValue: authStub(me) },
      { provide: OpsConfirmService, useValue: confirm.service },
    ],
  }).compileComponents();

  const http = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(PayrollPage);
  fixture.detectChanges();
  http.expectOne((r) => r.url.endsWith('/api/ops/payroll')).flush(register);
  flushThirteenth(http);
  fixture.detectChanges();

  return {
    http,
    fixture,
    host: fixture.nativeElement as HTMLElement,
    confirm,
    text: () => fixture.nativeElement.textContent as string,
    button: (name: string) => {
      const inHost = fixture.nativeElement.querySelector(
        `button[name="${name}"]`,
      ) as HTMLButtonElement | null;
      if (inHost) return inHost;
      const inOverlay = document.body.querySelector(
        `button[name="${name}"]`,
      ) as HTMLButtonElement | null;
      if (inOverlay) return inOverlay;
      const rowMenuBtn = fixture.nativeElement.querySelector(
        'button[name="rowMenu"]',
      ) as HTMLButtonElement | null;
      if (rowMenuBtn) {
        rowMenuBtn.click();
        fixture.detectChanges();
        const fromRow = document.body.querySelector(
          `button[name="${name}"]`,
        ) as HTMLButtonElement | null;
        if (fromRow) return fromRow;
      }
      const moreMenuBtn = fixture.nativeElement.querySelector(
        'button[name="payrollMoreMenu"]',
      ) as HTMLButtonElement | null;
      moreMenuBtn?.click();
      fixture.detectChanges();
      return document.body.querySelector(`button[name="${name}"]`) as HTMLButtonElement | null;
    },
  };
}

function flushRegisterReload(http: HttpTestingController, register: PayrollRegisterResponse): void {
  http.expectOne((r) => r.url.endsWith('/api/ops/payroll')).flush(register);
}

describe('monthly payslip', () => {
  it('labels monthly salary, salary gross, and days paid', () => {
    const view = buildPayslipView(
      {
        staffId: 'office-1',
        staffName: 'Mina Office',
        role: 'admin',
        basis: 'monthly',
        ratePhp: 30000,
        regularHours: 0,
        otHours: 0,
        daysPaid: 15,
        amount: 15000,
      },
      undefined,
    );
    expect(view.basis).toBe('Monthly');
    expect(view.rateLabel).toBe('Monthly salary');
    expect(view.rate).toBe('₱30,000.00');
    expect(view.grossLabel).toBe('Salary gross');
    expect(view.gross).toBe('₱15,000.00');
    expect(view.daysPaid).toBe(15);
    expect(view.regularHours).toBe('0.00');
    expect(view.otHours).toBe('0.00');
    expect(view.allowanceLines).toEqual([]);
  });

  it('lists one allowance line per item and omits the block when absent', () => {
    expect(payslipAllowanceLines({})).toEqual([]);
    expect(payslipAllowanceLines({ allowances: null, allowanceItems: [] })).toEqual([]);
    expect(
      payslipAllowanceLines({
        allowanceItems: [
          { name: 'Rice', amount: 150 },
          { name: 'COLA', amount: 500 },
        ],
      }),
    ).toEqual([
      { label: 'Rice', amount: '₱150.00' },
      { label: 'COLA', amount: '₱500.00' },
    ]);
    expect(payslipAllowanceLines({ allowances: 80 })).toEqual([
      { label: 'COLA / allowances', amount: '₱80.00' },
    ]);
  });

  it('lists loan instalments and loan balance only when present', () => {
    expect(payslipLoanLines({})).toEqual([]);
    expect(payslipLoanLines({ loanDeductions: null, loanItems: [] })).toEqual([]);
    expect(
      payslipLoanLines({
        loanItems: [{ label: 'Cash advance', instalment: 1000, loanBalance: 3000 }],
      }),
    ).toEqual([
      { label: 'Cash advance', amount: '₱1,000.00' },
      { label: 'Loan balance', amount: '₱3,000.00' },
    ]);
    expect(payslipLoanLines({ loanItems: [{ kind: 'SSS loan', amount: 500 }] })).toEqual([
      { label: 'SSS loan', amount: '₱500.00' },
    ]);
    expect(payslipLoanLines({ loanDeductions: 1000 })).toEqual([
      { label: 'Loans / CA', amount: '₱1,000.00' },
    ]);

    const view = buildPayslipView(
      {
        staffId: 'office-1',
        staffName: 'Mina Office',
        role: 'admin',
        basis: 'monthly',
        ratePhp: 30000,
        regularHours: 0,
        otHours: 0,
        daysPaid: 15,
        amount: 15000,
        loanDeductions: 1000,
        loanItems: [
          {
            loanId: 'l1',
            kind: 'Cash advance',
            suggested: 1000,
            amount: 1000,
            balanceBefore: 4000,
            balanceAfter: 3000,
            capped: false,
          },
        ],
      },
      { sss: 100, philHealth: 0, pagIbig: 0, wht: 0, other: 0 },
    );
    expect(view.loanLines).toEqual([
      { label: 'Cash advance', amount: '₱1,000.00' },
      { label: 'Loan balance', amount: '₱3,000.00' },
    ]);
    expect(view.net).toBe('₱13,900.00');
  });
});

describe('PayrollPage', () => {
  it('renders grid with data and calculates total amount on initial load', async () => {
    await TestBed.configureTestingModule({
      imports: [PayrollPage],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: authStub(ownerMe) },
      ],
    }).compileComponents();

    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PayrollPage);
    fixture.detectChanges();

    const req = http.expectOne((r) => r.url.endsWith('/api/ops/payroll'));
    expect(req.request.method).toBe('GET');
    req.flush(mockPayrollData);
    flushThirteenth(http);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain("Danilo 'Danny' Santos");
    expect(text).toContain('technician');
    expect(text).toContain('Hourly');
    expect(text).toContain('₱120.00');
    expect(text).toContain('40.00');
    expect(text).toContain('5.00');
    expect(text).toContain('₱5,550.00');
    expect(text).toContain('5h OT');

    expect(text).toContain('Rezza Desk');
    expect(text).toContain('admin');
    expect(text).toContain('Daily');
    expect(text).toContain('₱800.00');
    expect(text).toContain('₱4,000.00');

    expect(text).toContain('Mina Office');
    expect(text).toContain('Monthly');
    expect(text).toContain('₱30,000.00');
    expect(text).toContain('₱15,000.00');

    // Summary row
    expect(text).toContain('Total');
    expect(text).toContain('₱24,550.00');

    http.verify();
  });

  it('triggers API call with includeInactive=true when toggle is checked', async () => {
    await TestBed.configureTestingModule({
      imports: [PayrollPage],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: authStub(ownerMe) },
      ],
    }).compileComponents();

    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PayrollPage);
    fixture.detectChanges();

    const initialReq = http.expectOne((r) => r.url.endsWith('/api/ops/payroll'));
    expect(initialReq.request.params.has('includeInactive')).toBe(false);
    initialReq.flush(mockPayrollData);
    flushThirteenth(http);
    fixture.detectChanges();

    const checkbox = fixture.nativeElement.querySelector(
      'mat-checkbox[name="includeInactive"] input',
    ) as HTMLInputElement;
    expect(checkbox).toBeTruthy();
    checkbox.click();
    fixture.detectChanges();

    const inactiveReq = http.expectOne(
      (r) => r.url.endsWith('/api/ops/payroll') && r.params.get('includeInactive') === 'true',
    );
    expect(inactiveReq.request.method).toBe('GET');
    expect(inactiveReq.request.params.get('includeInactive')).toBe('true');
    inactiveReq.flush({ ...mockPayrollData, rows: [] });
    fixture.detectChanges();

    http.verify();
  });

  it('triggers CSV export download when button is clicked', async () => {
    await TestBed.configureTestingModule({
      imports: [PayrollPage],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: authStub(ownerMe) },
      ],
    }).compileComponents();

    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PayrollPage);
    fixture.detectChanges();

    http.expectOne((r) => r.url.endsWith('/api/ops/payroll')).flush(mockPayrollData);
    flushThirteenth(http);
    fixture.detectChanges();

    const moreBtn = fixture.nativeElement.querySelector(
      'button[name="payrollMoreMenu"]',
    ) as HTMLButtonElement;
    expect(moreBtn).toBeTruthy();
    moreBtn.click();
    fixture.detectChanges();

    const exportBtn = document.body.querySelector(
      'button[name="exportPayrollCsv"]',
    ) as HTMLButtonElement;
    expect(exportBtn).toBeTruthy();
    exportBtn.click();

    const req = http.expectOne((r) => r.url.endsWith('/api/ops/payroll/export'));
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['Staff Name,Role,Basis,Rate,Regular Hours,OT Hours,Days Paid,Amount\n']));
    fixture.detectChanges();

    http.verify();
  });

  it('period change reloads the register', async () => {
    const { http, fixture } = await mountPayroll(ownerMe, mockPayrollData);
    fixture.componentInstance.onPeriodChange({ from: '2026-10-01', to: '2026-10-07' });
    fixture.detectChanges();
    const reloadReq = http.expectOne(
      (r) => r.url.includes('/api/ops/payroll') && r.urlWithParams.includes('from=2026-10-01'),
    );
    reloadReq.flush(mockPayrollData);
  });
});

describe('PayrollPage lock controls', () => {
  it('locks the period after confirming the totals and reloads the register', async () => {
    const { http, fixture, host, confirm, button } = await mountPayroll();

    button('lockPeriod')!.click();
    fixture.detectChanges();
    // The lock first asks for open punches in the period; none are open here.
    http.expectOne((r) => r.url.endsWith('/api/ops/attendance/open-punches')).flush([]);

    expect(confirm.calls).toHaveLength(1);
    const message = confirm.calls[0].message ?? '';
    expect(confirm.calls[0].title).toBe('Lock period');
    expect(message).toContain('Total gross ₱24,550.00');
    expect(message).toContain('deductions ₱0.00');
    expect(message).toContain('net ₱24,550.00');
    expect(message).toContain('Roosevelt Main');

    const lock = http.expectOne((r) => r.url.endsWith('/api/ops/payroll/runs'));
    expect(lock.request.method).toBe('POST');
    expect(lock.request.body).toEqual({
      branchId: 'branch-1',
      from: '2026-09-14',
      to: '2026-09-20',
      note: null,
    });
    lock.flush({
      id: 'run-1',
      branchId: 'branch-1',
      branchName: 'Roosevelt Main',
      from: '2026-09-14',
      to: '2026-09-20',
      status: 'locked',
      note: null,
      totalGross: 24550,
      totalDeductions: 0,
      totalNet: 24550,
      lineCount: 3,
      createdAtUtc: '2026-10-07T02:00:00Z',
      createdByName: 'Owner Apple',
      lockedAtUtc: '2026-10-07T02:00:00Z',
      lockedByName: 'Owner Apple',
      voidedAtUtc: null,
      voidedByName: null,
    });

    flushRegisterReload(http, lockedRegister);
    fixture.detectChanges();

    expect((host.querySelector('[data-lock-status]') as HTMLElement).textContent?.trim()).toBe(
      'Locked 7 Oct 2026 by Owner Apple',
    );

    http.verify();
  });

  it('disables deduction inputs and the save button while the period is locked', async () => {
    const { http, host } = await mountPayroll(ownerMe, lockedRegister);

    for (const name of [
      'sss-staff-1',
      'philHealth-staff-1',
      'pagIbig-staff-1',
      'wht-staff-1',
      'other-staff-1',
    ]) {
      const input = host.querySelector(`input[name="${name}"]`) as HTMLInputElement;
      expect(input, name).toBeTruthy();
      expect(input.disabled, name).toBe(true);
    }

    expect(
      (host.querySelector('button[name="saveDeductions"]') as HTMLButtonElement).disabled,
    ).toBe(true);

    http.verify();
  });

  it('shows the server message when a deduction save is refused with 409', async () => {
    const { http, fixture, host, text, button } = await mountPayroll();

    button('saveDeductions')!.click();
    fixture.detectChanges();

    const save = http.expectOne((r) => r.url.endsWith('/api/ops/payroll/deductions'));
    expect(save.request.method).toBe('PUT');
    expect(save.request.body.staffId).toBe('staff-1');
    save.flush(
      { error: 'This period is locked. Post an adjustment in the next period.' },
      { status: 409, statusText: 'Conflict' },
    );
    fixture.detectChanges();

    expect(text()).toContain('This period is locked. Post an adjustment in the next period.');
    expect(
      (host.querySelector('button[name="saveDeductions"]') as HTMLButtonElement).disabled,
    ).toBe(false);

    http.verify();
  });

  it('voids the locked run for the owner after confirming and reloads', async () => {
    const { http, fixture, host, confirm, button } = await mountPayroll(ownerMe, lockedRegister);

    button('voidRun')!.click();
    fixture.detectChanges();

    expect(confirm.calls).toHaveLength(1);
    expect(confirm.calls[0].title).toBe('Void payroll run');
    expect(confirm.calls[0].tone).toBe('danger');

    const voidReq = http.expectOne((r) => r.url.endsWith('/api/ops/payroll/runs/run-1/void'));
    expect(voidReq.request.method).toBe('POST');
    voidReq.flush({
      id: 'run-1',
      branchId: 'branch-1',
      branchName: 'Roosevelt Main',
      from: '2026-09-14',
      to: '2026-09-20',
      status: 'void',
      note: null,
      totalGross: 24550,
      totalDeductions: 1500,
      totalNet: 23050,
      lineCount: 3,
      createdAtUtc: '2026-10-07T02:00:00Z',
      createdByName: 'Owner Apple',
      lockedAtUtc: '2026-10-07T02:00:00Z',
      lockedByName: 'Owner Apple',
      voidedAtUtc: '2026-10-07T03:00:00Z',
      voidedByName: 'Owner Apple',
    });

    flushRegisterReload(http, mockPayrollData);
    fixture.detectChanges();

    expect((host.querySelector('[data-lock-status]') as HTMLElement).textContent?.trim()).toBe(
      'Not locked',
    );
    expect(host.querySelector('button[name="voidRun"]')).toBeNull();

    http.verify();
  });

  it('hides void run from an admin on a locked period', async () => {
    const { http, host } = await mountPayroll(adminMe, lockedRegister);

    expect(host.querySelector('button[name="voidRun"]')).toBeNull();
    expect((host.querySelector('[data-lock-status]') as HTMLElement).textContent?.trim()).toBe(
      'Locked 7 Oct 2026 by Owner Apple',
    );

    http.verify();
  });

  describe('PayrollPage rate history', () => {
    it('opens the rate dialog with history and saves a dated change', async () => {
      const { http, fixture, host, button } = await mountPayroll();

      button('rate')!.click();
      fixture.detectChanges();
      await fixture.whenStable();

      const rates = http.expectOne((r) => r.url.endsWith('/api/ops/payroll/rates/staff-1'));
      expect(rates.request.method).toBe('GET');
      rates.flush(ratesResponse);
      fixture.detectChanges();
      await fixture.whenStable();

      const dialog = document.body.querySelector('mat-dialog-container') as HTMLElement;
      expect(dialog.textContent).toContain("Pay rate — Danilo 'Danny' Santos");
      expect(dialog.textContent).toContain('Hourly');
      expect(dialog.textContent).toContain('₱120.00');
      const history = dialog.querySelector('[data-rate-history]') as HTMLElement;
      expect(history.textContent).toContain('2026-01-01');
      expect(history.textContent).toContain('Start rate');
      expect(history.textContent).toContain('Raise');
      expect(history.textContent).toContain('Owner Apple');

      const rateInput = dialog.querySelector('input[name="ratePhp"]') as HTMLInputElement;
      rateInput.value = '150';
      rateInput.dispatchEvent(new Event('input'));
      const reasonInput = dialog.querySelector('input[name="rateReason"]') as HTMLInputElement;
      reasonInput.value = 'Mid-year raise';
      reasonInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      await fixture.whenStable();

      (dialog.querySelector('form#payroll-rate') as HTMLFormElement).dispatchEvent(
        new Event('submit', { bubbles: true, cancelable: true }),
      );
      fixture.detectChanges();

      const save = http.expectOne((r) => r.url.endsWith('/api/ops/payroll/rates/staff-1'));
      expect(save.request.method).toBe('PUT');
      expect(save.request.body.ratePhp).toBe(150);
      expect(save.request.body.basis).toBe('hourly');
      expect(save.request.body.reason).toBe('Mid-year raise');
      expect(save.request.body.effectiveFrom).toBeTruthy();
      save.flush({
        ...ratesResponse,
        ratePhp: 150,
        history: [
          ...ratesResponse.history,
          {
            id: 'h-3',
            effectiveFrom: '2026-10-01',
            basis: 'hourly',
            ratePhp: 150,
            otAfterHoursPerDay: 8,
            otMultiplier: 1,
            createdAtUtc: '2026-10-01T02:00:00Z',
            createdByName: 'Owner Apple',
            reason: 'Mid-year raise',
          },
        ],
      });
      fixture.detectChanges();
      await fixture.whenStable();

      expect((dialog.querySelector('[data-rate-amount]') as HTMLElement).textContent).toContain(
        '₱150.00',
      );
      expect((dialog.querySelector('[data-rate-history]') as HTMLElement).textContent).toContain(
        'Mid-year raise',
      );

      (dialog.querySelector('mat-dialog-actions button') as HTMLButtonElement).click();
      fixture.detectChanges();
      await fixture.whenStable();

      flushRegisterReload(http, mockPayrollData);
      fixture.detectChanges();

      expect((host.querySelector('[data-lock-status]') as HTMLElement).textContent?.trim()).toBe(
        'Not locked',
      );
      http.verify();
    });
  });

  describe('PayrollPage rate dialog errors', () => {
    it('shows a 409 from the rate save inside the dialog', async () => {
      const { http, fixture, button } = await mountPayroll();

      button('rate')!.click();
      fixture.detectChanges();
      await fixture.whenStable();

      http.expectOne((r) => r.url.endsWith('/api/ops/payroll/rates/staff-1')).flush(ratesResponse);
      fixture.detectChanges();
      await fixture.whenStable();

      const dialog = document.body.querySelector('mat-dialog-container') as HTMLElement;
      (dialog.querySelector('form#payroll-rate') as HTMLFormElement).dispatchEvent(
        new Event('submit', { bubbles: true, cancelable: true }),
      );
      fixture.detectChanges();

      const save = http.expectOne((r) => r.url.endsWith('/api/ops/payroll/rates/staff-1'));
      save.flush(
        { error: 'A locked payroll run covers this effective date.' },
        { status: 409, statusText: 'Conflict' },
      );
      fixture.detectChanges();
      await fixture.whenStable();

      expect(dialog.textContent).toContain('A locked payroll run covers this effective date.');

      (dialog.querySelector('mat-dialog-actions button') as HTMLButtonElement).click();
      fixture.detectChanges();
      await fixture.whenStable();

      http.verify();
    });
  });

  describe('payroll lock helpers', () => {
    it('builds the lock line for a locked period only', () => {
      expect(payrollLockLine('live', '2026-10-07T02:00:00Z', 'Owner Apple')).toBe('');
      expect(payrollLockLine('locked', '2026-10-07T02:00:00Z', 'Owner Apple')).toBe(
        'Locked 7 Oct 2026 by Owner Apple',
      );
      expect(payrollLockLine('locked', null, null)).toBe('Locked');
      expect(payrollLockLine('locked', '2026-10-07T02:00:00Z', null)).toBe('Locked 7 Oct 2026');
    });

    it('formats the lock date in Manila time', () => {
      expect(manilaLockDate('2026-10-06T20:30:00Z')).toBe('7 Oct 2026');
      expect(manilaLockDate(null)).toBe('');
    });

    it('prepends the lock line to the exported CSV', async () => {
      const csv = new Blob(['Name,Amount\nDanilo,5550\n'], { type: 'text/csv; charset=utf-8' });
      const withLock = await csvTextWithLockLine(csv, 'Locked 7 Oct 2026 by Owner Apple');
      expect(await withLock.text()).toBe(
        'Locked 7 Oct 2026 by Owner Apple\nName,Amount\nDanilo,5550\n',
      );

      const live = await csvTextWithLockLine(csv, '');
      expect(await live.text()).toBe('Name,Amount\nDanilo,5550\n');
    });

    it('labels the rate spans of a mid-period change', () => {
      expect(
        rateSliceLabels([
          { from: '2026-09-14', to: '2026-09-18', basis: 'hourly', ratePhp: 120, otMultiplier: 1 },
          {
            from: '2026-09-19',
            to: '2026-09-20',
            basis: 'daily',
            ratePhp: 800,
            otAfterHoursPerDay: 8,
          },
        ]),
      ).toEqual([
        '2026-09-14 to 2026-09-18 · Hourly · ₱120.00 · ×1',
        '2026-09-19 to 2026-09-20 · Daily · ₱800.00 · OT after 8h',
      ]);
      expect(rateSliceLabels(null)).toEqual([]);
    });
  });

  describe('PayrollPage locked print header', () => {
    it('adds the lock line to the payslip header when the period is locked', async () => {
      const { http, fixture, button } = await mountPayroll(ownerMe, lockedRegister);

      button('payslip')!.click();
      fixture.detectChanges();
      await fixture.whenStable();

      http
        .expectOne((r) => r.url.endsWith('/api/shop'))
        .flush({
          tradingName: 'Rakkii Cars',
          legalName: 'Rakkii Cars OPC',
          address: '1 Roosevelt Ave',
          phone: '0917-000-0000',
          facebook: '',
          email: '',
          tin: '111-222-333-000',
        });
      fixture.detectChanges();
      await fixture.whenStable();

      const lock = document.body.querySelector('[data-payslip-lock]') as HTMLElement;
      expect(lock).toBeTruthy();
      expect(lock.textContent?.trim()).toBe('Locked 7 Oct 2026 by Owner Apple');

      http.verify();
    });
  });

  describe('PayrollPage statutory deduction schedule and bulk actions', () => {
    const sampleSuggestion: StatutorySuggestion = {
      staffId: 'staff-1',
      staffName: "Danilo 'Danny' Santos",
      month: '2026-09',
      base: 24000,
      sssMsc: 24000,
      sss: 1080,
      philHealthBase: 24000,
      philHealth: 600,
      pagIbigBase: 5000,
      pagIbig: 100,
      whtTaxable: 22220,
      wht: 400,
      statutoryTotal: 2180,
      notes: ['SSS 2025 table: EE ₱1,080.00', 'PhilHealth 5%: EE ₱600.00', 'Pag-IBIG: EE ₱100.00'],
    };

    it('suggestStatutory scales deductions by 50% when statutorySchedule is semi_monthly', async () => {
      const { http, fixture, host, button } = await mountPayroll();
      const cmp = fixture.componentInstance as any;
      expect(cmp.statutorySchedule()).toBe('semi_monthly');

      button('suggestStatutory')!.click();
      fixture.detectChanges();

      const req = http.expectOne(
        (r) =>
          r.url.includes('/api/ops/payroll/statutory-suggested') &&
          r.params.get('staffId') === 'staff-1',
      );
      expect(req.request.method).toBe('GET');
      req.flush(sampleSuggestion);
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(cmp.draftAmount('staff-1', 'sss')).toBe(540);
      expect(cmp.draftAmount('staff-1', 'philHealth')).toBe(300);
      expect(cmp.draftAmount('staff-1', 'pagIbig')).toBe(50);
      expect(cmp.draftAmount('staff-1', 'wht')).toBe(200);

      const sssInput = host.querySelector('input[name="sss-staff-1"]') as HTMLInputElement;
      const philHealthInput = host.querySelector(
        'input[name="philHealth-staff-1"]',
      ) as HTMLInputElement;
      const pagIbigInput = host.querySelector('input[name="pagIbig-staff-1"]') as HTMLInputElement;
      const whtInput = host.querySelector('input[name="wht-staff-1"]') as HTMLInputElement;

      expect(sssInput.value).toBe('540');
      expect(philHealthInput.value).toBe('300');
      expect(pagIbigInput.value).toBe('50');
      expect(whtInput.value).toBe('200');

      expect(host.querySelector('[data-suggest-notes]')?.textContent).toContain(
        'semi-monthly rate (50% of monthly',
      );

      http.verify();
    });

    it('suggestStatutory scales deductions by 100% when statutorySchedule is full_month', async () => {
      const { http, fixture, host, button } = await mountPayroll();
      const cmp = fixture.componentInstance as any;

      cmp.statutorySchedule.set('full_month');
      fixture.detectChanges();

      button('suggestStatutory')!.click();
      fixture.detectChanges();

      const req = http.expectOne(
        (r) =>
          r.url.includes('/api/ops/payroll/statutory-suggested') &&
          r.params.get('staffId') === 'staff-1',
      );
      req.flush(sampleSuggestion);
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(cmp.draftAmount('staff-1', 'sss')).toBe(1080);
      expect(cmp.draftAmount('staff-1', 'philHealth')).toBe(600);
      expect(cmp.draftAmount('staff-1', 'pagIbig')).toBe(100);
      expect(cmp.draftAmount('staff-1', 'wht')).toBe(400);

      const sssInput = host.querySelector('input[name="sss-staff-1"]') as HTMLInputElement;
      const philHealthInput = host.querySelector(
        'input[name="philHealth-staff-1"]',
      ) as HTMLInputElement;
      const pagIbigInput = host.querySelector('input[name="pagIbig-staff-1"]') as HTMLInputElement;
      const whtInput = host.querySelector('input[name="wht-staff-1"]') as HTMLInputElement;

      expect(sssInput.value).toBe('1080');
      expect(philHealthInput.value).toBe('600');
      expect(pagIbigInput.value).toBe('100');
      expect(whtInput.value).toBe('400');

      expect(host.querySelector('[data-suggest-notes]')?.textContent).toContain(
        'full monthly rate',
      );

      http.verify();
    });

    it('suggestAllStatutory prefills suggestions for all rated rows', async () => {
      const { http, fixture, host, button } = await mountPayroll();
      const cmp = fixture.componentInstance as any;

      const suggestAllBtn = button('suggestAllStatutory');
      expect(suggestAllBtn).toBeTruthy();
      suggestAllBtn!.click();
      fixture.detectChanges();

      const reqs = http.match((r) => r.url.includes('/api/ops/payroll/statutory-suggested'));
      expect(reqs).toHaveLength(3);

      reqs.forEach((r) => {
        const staffId = r.request.params.get('staffId')!;
        r.flush({ ...sampleSuggestion, staffId });
      });
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(cmp.draftAmount('staff-1', 'sss')).toBe(540);
      expect(cmp.draftAmount('staff-2', 'sss')).toBe(540);
      expect(cmp.draftAmount('staff-3', 'sss')).toBe(540);

      expect((host.querySelector('input[name="sss-staff-1"]') as HTMLInputElement).value).toBe(
        '540',
      );
      expect((host.querySelector('input[name="sss-staff-2"]') as HTMLInputElement).value).toBe(
        '540',
      );
      expect((host.querySelector('input[name="sss-staff-3"]') as HTMLInputElement).value).toBe(
        '540',
      );
      expect(host.querySelector('[data-suggest-notes]')?.textContent).toContain(
        'Prefilled semi-monthly statutory suggestions (50%) for 3 staff member(s)',
      );

      http.verify();
    });

    it('auto-selects semi_monthly for cutoffs <= 16 days and full_month for full calendar month', async () => {
      const { http, fixture } = await mountPayroll();
      const cmp = fixture.componentInstance as any;

      cmp.onPeriodChange({ from: '2026-10-01', to: '2026-10-15' });
      http.expectOne((r) => r.url.includes('/api/ops/payroll')).flush(mockPayrollData);
      expect(cmp.statutorySchedule()).toBe('semi_monthly');

      cmp.onPeriodChange({ from: '2026-10-01', to: '2026-10-31' });
      http.expectOne((r) => r.url.includes('/api/ops/payroll')).flush(mockPayrollData);
      expect(cmp.statutorySchedule()).toBe('full_month');

      http.verify();
    });
  });

  describe('PayrollApi premium requests', () => {
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

    const settingsBody = {
      regularHolidayPercent: 200,
      specialDayPercent: 130,
      restDayPercent: 130,
      holidayPremiumEnabled: true,
      restDayPremiumEnabled: true,
      nightDiffEnabled: false,
      nightDiffPercent: 10,
      nightStart: '22:00:00',
      nightEnd: '06:00:00',
      shiftEnd: '17:00:00',
      otGraceMinutes: 30,
      adminsCanApproveOt: false,
      otPremiumPercent: 125,
      otMonthlyDayDivisor: 313,
    };

    it('GETs and PUTs premium settings', () => {
      const { api, http } = setup();

      api.getSettings().subscribe();
      const get = http.expectOne('/api/ops/payroll/settings');
      expect(get.request.method).toBe('GET');
      get.flush({ branchId: 'branch-1', ...settingsBody });

      api.saveSettings(settingsBody).subscribe();
      const put = http.expectOne('/api/ops/payroll/settings');
      expect(put.request.method).toBe('PUT');
      expect(put.request.body).toEqual(settingsBody);
      put.flush({ branchId: 'branch-1', ...settingsBody });

      http.verify();
    });

    it('lists, creates, updates, and deletes holidays', () => {
      const { api, http } = setup();
      const holiday = { date: '2026-06-12', name: 'Independence Day', kind: 'regular' as const };

      api.listHolidays(2026).subscribe();
      const list = http.expectOne((r) => r.url === '/api/ops/payroll/holidays');
      expect(list.request.method).toBe('GET');
      expect(list.request.params.get('year')).toBe('2026');
      list.flush({ year: 2026, holidays: [] });

      api.createHoliday(holiday).subscribe();
      const post = http.expectOne('/api/ops/payroll/holidays');
      expect(post.request.method).toBe('POST');
      expect(post.request.body).toEqual(holiday);
      post.flush({ id: 'h1', branchId: null, ...holiday });

      const updated = { ...holiday, kind: 'special' as const, name: 'Special day' };
      api.updateHoliday('h1', updated).subscribe();
      const put = http.expectOne('/api/ops/payroll/holidays/h1');
      expect(put.request.method).toBe('PUT');
      expect(put.request.body).toEqual(updated);
      put.flush({ id: 'h1', branchId: null, ...updated });

      api.deleteHoliday('h1').subscribe();
      const del = http.expectOne('/api/ops/payroll/holidays/h1');
      expect(del.request.method).toBe('DELETE');
      del.flush(null);

      http.verify();
    });

    it('POSTs copy-year with fromYear and toYear', () => {
      const { api, http } = setup();
      const body = { fromYear: 2025, toYear: 2026 };

      api.copyHolidayYear(body).subscribe();
      const req = http.expectOne('/api/ops/payroll/holidays/copy-year');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);
      req.flush({ created: 3, skipped: 1 });

      http.verify();
    });

    it('PUTs a premium override', () => {
      const { api, http } = setup();

      const save = {
        staffId: 'staff-1',
        from: '2026-10-01',
        to: '2026-10-15',
        key: 'premium',
        amount: 125 as number | null,
        reason: 'Agreed holiday amount',
      };
      api.saveOverride(save).subscribe();
      const put = http.expectOne('/api/ops/payroll/overrides');
      expect(put.request.method).toBe('PUT');
      expect(put.request.body).toEqual(save);
      put.flush({
        id: 'o1',
        staffId: save.staffId,
        from: save.from,
        to: save.to,
        key: 'premium',
        amount: 125,
        reason: save.reason,
        setAtUtc: '2026-10-10T00:00:00Z',
      });

      const clear = { ...save, amount: null, reason: 'Clear the edit' };
      api.saveOverride(clear).subscribe();
      const cleared = http.expectOne('/api/ops/payroll/overrides');
      expect(cleared.request.method).toBe('PUT');
      expect(cleared.request.body).toEqual(clear);
      cleared.flush({
        staffId: 'staff-1',
        from: '2026-10-01',
        to: '2026-10-15',
        key: 'premium',
        cleared: true,
      });

      http.verify();
    });
  });

  describe('PayrollApi allowance requests', () => {
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

    const saved = {
      id: 'a1',
      staffId: 'staff-1',
      name: 'Rice',
      amount: 50,
      per: 'day' as const,
      taxable: true,
      effectiveFrom: '2026-10-01',
      effectiveTo: null,
      note: 'Daily rice',
      active: true,
      createdAtUtc: '2026-10-10T00:00:00Z',
    };

    it('GETs allowances for one worker', () => {
      const { api, http } = setup();

      api.listAllowances().subscribe();
      const all = http.expectOne((r) => r.url === '/api/ops/payroll/allowances');
      expect(all.request.method).toBe('GET');
      expect(all.request.params.has('staffId')).toBe(false);
      all.flush({ allowances: [], suggestedNames: [] });

      api.listAllowances('staff-1').subscribe();
      const list = http.expectOne((r) => r.url === '/api/ops/payroll/allowances');
      expect(list.request.method).toBe('GET');
      expect(list.request.params.get('staffId')).toBe('staff-1');
      list.flush({ allowances: [saved], suggestedNames: ['Rice', 'COLA'] });

      http.verify();
    });

    it('POSTs, PUTs, and DELETEs an allowance', () => {
      const { api, http } = setup();
      const create = {
        staffId: 'staff-1',
        name: 'Rice',
        amount: 50,
        per: 'day' as const,
        taxable: true,
        effectiveFrom: '2026-10-01',
        effectiveTo: null,
        note: 'Daily rice',
        active: true,
      };

      api.createAllowance(create).subscribe();
      const post = http.expectOne('/api/ops/payroll/allowances');
      expect(post.request.method).toBe('POST');
      expect(post.request.body).toEqual(create);
      post.flush(saved);

      const update = {
        name: 'Rice',
        amount: 60,
        per: 'period' as const,
        taxable: false,
        effectiveFrom: null,
        effectiveTo: null,
        note: null,
        active: true,
      };
      api.updateAllowance('a1', update).subscribe();
      const put = http.expectOne('/api/ops/payroll/allowances/a1');
      expect(put.request.method).toBe('PUT');
      expect(put.request.body).toEqual(update);
      put.flush({ ...saved, ...update });

      api.deleteAllowance('a1').subscribe();
      const del = http.expectOne('/api/ops/payroll/allowances/a1');
      expect(del.request.method).toBe('DELETE');
      del.flush(null);

      http.verify();
    });
  });

  describe('PayrollApi loan requests', () => {
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

    const saved = {
      id: 'loan-1',
      staffId: 'staff-1',
      kind: 'Cash advance',
      principal: 5000,
      perPeriodAmount: 1000,
      startPeriod: '2026-10-01',
      note: null,
      createdAtUtc: '2026-10-10T00:00:00Z',
      cancelledAtUtc: null,
      cancelReason: null,
      paid: 0,
      balance: 5000,
      status: 'active' as const,
    };

    it('GETs loans for one worker and one loan with payments', () => {
      const { api, http } = setup();

      api.listLoans().subscribe();
      const all = http.expectOne((r) => r.url === '/api/ops/payroll/loans');
      expect(all.request.method).toBe('GET');
      expect(all.request.params.has('staffId')).toBe(false);
      expect(all.request.params.has('includeClosed')).toBe(false);
      all.flush({ loans: [], suggestedKinds: ['Cash advance'] });

      api.listLoans('staff-1').subscribe();
      const open = http.expectOne((r) => r.url === '/api/ops/payroll/loans');
      expect(open.request.params.get('staffId')).toBe('staff-1');
      expect(open.request.params.has('includeClosed')).toBe(false);
      open.flush({ loans: [saved], suggestedKinds: ['Cash advance', 'SSS loan'] });

      api.listLoans('staff-1', true).subscribe();
      const closed = http.expectOne((r) => r.url === '/api/ops/payroll/loans');
      expect(closed.request.params.get('staffId')).toBe('staff-1');
      expect(closed.request.params.get('includeClosed')).toBe('true');
      closed.flush({ loans: [saved], suggestedKinds: ['Cash advance'] });

      api.getLoan('loan-1').subscribe();
      const one = http.expectOne('/api/ops/payroll/loans/loan-1');
      expect(one.request.method).toBe('GET');
      one.flush({
        ...saved,
        payments: [
          {
            id: 'pay-1',
            loanId: 'loan-1',
            runId: 'run-1',
            staffId: 'staff-1',
            periodFrom: '2026-10-01',
            periodTo: '2026-10-15',
            amount: 1000,
            createdAtUtc: '2026-10-16T00:00:00Z',
            reversedAtUtc: '2026-10-20T00:00:00Z',
          },
        ],
      });

      http.verify();
    });

    it('POSTs, PUTs, cancels, and DELETEs a loan', () => {
      const { api, http } = setup();
      const create = {
        staffId: 'staff-1',
        kind: 'Cash advance',
        principal: 5000,
        perPeriodAmount: 1000,
        startPeriod: '2026-10-01',
        note: null,
      };

      api.createLoan(create).subscribe();
      const post = http.expectOne('/api/ops/payroll/loans');
      expect(post.request.method).toBe('POST');
      expect(post.request.body).toEqual(create);
      post.flush(saved);

      const update = {
        kind: 'Cash advance',
        perPeriodAmount: 500,
        startPeriod: '2026-10-16',
        principal: 5000,
        note: 'Half',
      };
      api.updateLoan('loan-1', update).subscribe();
      const put = http.expectOne('/api/ops/payroll/loans/loan-1');
      expect(put.request.method).toBe('PUT');
      expect(put.request.body).toEqual(update);
      put.flush({ ...saved, ...update });

      api.cancelLoan('loan-1', 'Recovered in cash').subscribe();
      const cancel = http.expectOne('/api/ops/payroll/loans/loan-1/cancel');
      expect(cancel.request.method).toBe('POST');
      expect(cancel.request.body).toEqual({ reason: 'Recovered in cash' });
      cancel.flush({ ...saved, status: 'cancelled', cancelReason: 'Recovered in cash' });

      api.deleteLoan('loan-1').subscribe();
      const del = http.expectOne('/api/ops/payroll/loans/loan-1');
      expect(del.request.method).toBe('DELETE');
      del.flush(null);

      http.verify();
    });
  });

  describe('PayrollApi statutory suggestion', () => {
    afterEach(() => {
      TestBed.resetTestingModule();
    });

    it('sends the staff, the month, and the register period', () => {
      TestBed.configureTestingModule({
        providers: [
          provideZonelessChangeDetection(),
          provideHttpClient(),
          provideHttpClientTesting(),
        ],
      });
      const api = TestBed.inject(PayrollApi);
      const http = TestBed.inject(HttpTestingController);

      api
        .getStatutorySuggested('staff-1', '2026-10', { from: '2026-10-11', to: '2026-10-17' })
        .subscribe();
      const req = http.expectOne((r) => r.url.includes('/api/ops/payroll/statutory-suggested'));
      expect(req.request.method).toBe('GET');
      expect(req.request.params.get('staffId')).toBe('staff-1');
      expect(req.request.params.get('month')).toBe('2026-10');
      expect(req.request.params.get('from')).toBe('2026-10-11');
      expect(req.request.params.get('to')).toBe('2026-10-17');
      req.flush({
        staffId: 'staff-1',
        staffName: 'Partial',
        month: '2026-10',
        base: 200000,
        sssMsc: 35000,
        sss: 395.16,
        philHealthBase: 100000,
        philHealth: 564.52,
        pagIbigBase: 10000,
        pagIbig: 45.16,
        whtTaxable: 195550,
        wht: 11307.46,
        statutoryTotal: 12312.3,
        notes: ['Prorated to the period: 2026-10-11–2026-10-17 (22.58% of the month)'],
        periodFrom: '2026-10-11',
        periodTo: '2026-10-17',
        fraction: 0.22580645,
      } satisfies StatutorySuggestion);

      http.verify();
    });
  });
});
