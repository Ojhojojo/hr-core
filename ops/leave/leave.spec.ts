import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { afterEach, describe, expect, it } from 'vitest';
import { AuthMe, AuthService, ConfirmDialogData, OpsConfirmService } from '@hr-host';
import { LeaveBalance, LeaveRequestRow, StaffLeaveBalances } from './leave.api';
import { LeavePage } from './leave';
import { crossYearSplitHint } from './leave.dialogs';

const balances: LeaveBalance[] = [
  { leaveType: 'vacation', allotment: 10, used: 0, pending: 0, remaining: 10 },
  { leaveType: 'sick', allotment: 0, used: 0, pending: 0, remaining: 0 },
  { leaveType: 'unpaid', allotment: 0, used: 0, pending: 0, remaining: 0 },
];

const pending: LeaveRequestRow = {
  id: 'leave-1',
  staffId: 'tech-1',
  displayName: 'Shop Tech',
  leaveType: 'vacation',
  startDate: '2026-06-01',
  endDate: '2026-06-02',
  status: 'pending',
  note: null,
  halfDay: null,
  days: 1,
  cancelReason: null,
};

const approvedHistory: LeaveRequestRow = {
  id: 'leave-2',
  staffId: 'tech-1',
  displayName: 'Shop Tech',
  leaveType: 'vacation',
  startDate: '2026-05-01',
  endDate: '2026-05-02',
  status: 'approved',
  note: 'Vacation days',
  halfDay: null,
  days: 1,
  cancelReason: null,
};

const teamBalances: StaffLeaveBalances[] = [
  {
    staffId: 'tech-1',
    displayName: 'Shop Tech',
    role: 'technician',
    balances: [
      { leaveType: 'vacation', allotment: 10, used: 2, pending: 0, remaining: 8 },
      { leaveType: 'sick', allotment: 5, used: 0, pending: 0, remaining: 5 },
      { leaveType: 'unpaid', allotment: 0, used: 0, pending: 0, remaining: 0 },
    ],
  },
];

function me(role: AuthMe['staffRole'], subrole?: string | null): AuthMe {
  return {
    authUserId: role ?? 'staff',
    email: `${role}@rakkiicars.ph`,
    profileKind: 'staff',
    staffRole: role,
    subrole: subrole ?? null,
    branchId: 'branch-1',
    branchCode: 'main-roosevelt',
    isStaff: true,
    displayName: 'Tester',
    staffId: role ?? 'staff',
  };
}

function confirmStub(confirmed = true) {
  const calls: ConfirmDialogData[] = [];
  return {
    calls,
    service: {
      confirm: (data: ConfirmDialogData) => {
        calls.push(data);
        return of({ confirmed });
      },
    },
  };
}

async function mount(
  role: AuthMe['staffRole'],
  customRequests?: LeaveRequestRow[],
  confirmObj = confirmStub(true),
  subrole?: string | null,
  customBalances?: LeaveBalance[],
  customTeamBalances?: StaffLeaveBalances[],
) {
  await TestBed.configureTestingModule({
    imports: [LeavePage],
    providers: [
      provideZonelessChangeDetection(),
      provideNoopAnimations(),
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: AuthService, useValue: { me: signal(me(role, subrole)) } },
      { provide: OpsConfirmService, useValue: confirmObj.service },
    ],
  }).compileComponents();

  const http = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(LeavePage);
  fixture.detectChanges();

  const owner = role === 'owner';
  // Shop leave desk: Owner or General Manager Access only (#885).
  const decide = role === 'owner' || (role === 'admin' && subrole === 'general_manager');
  const isList = (req: { method: string; url: string }) =>
    req.method === 'GET' && req.url.includes('/api/leave') && !req.url.includes('/balances');

  if (!owner) {
    const balanceReq = http.expectOne(
      (req) => req.url.includes('/api/leave/balances') && !req.urlWithParams.includes('shop=1'),
    );
    expect(balanceReq.request.method).toBe('GET');
    expect(balanceReq.request.params.get('year')).toBeNull();
    balanceReq.flush(structuredClone(customBalances ?? balances));
  }
  if (decide) {
    const shopBalanceReq = http.expectOne(
      (req) => req.url.includes('/api/leave/balances') && req.urlWithParams.includes('shop=1'),
    );
    expect(shopBalanceReq.request.method).toBe('GET');
    expect(shopBalanceReq.request.params.get('year')).toBeNull();
    shopBalanceReq.flush(structuredClone(customTeamBalances ?? teamBalances));
  }

  const shopRows = customRequests ?? [pending, approvedHistory];
  const ownRows = customRequests ?? [pending];
  if (decide) {
    const pendingList = http.expectOne(
      (req) =>
        isList(req) && req.params.get('mine') !== 'true' && req.params.get('status') === 'pending',
    );
    expect(pendingList.request.method).toBe('GET');
    expect(pendingList.request.params.get('year')).toBeNull();
    pendingList.flush(structuredClone(shopRows.filter((r) => r.status === 'pending')));

    const historyList = http.expectOne(
      (req) =>
        isList(req) && req.params.get('mine') !== 'true' && req.params.get('status') === 'decided',
    );
    expect(historyList.request.method).toBe('GET');
    expect(historyList.request.params.get('year')).toBe(String(new Date().getFullYear()));
    historyList.flush(structuredClone(shopRows.filter((r) => r.status !== 'pending')));
  }
  if (!owner) {
    const mineList = http.expectOne((req) => isList(req) && req.params.get('mine') === 'true');
    expect(mineList.request.method).toBe('GET');
    expect(mineList.request.params.get('year')).toBeNull();
    mineList.flush(structuredClone(ownRows));
  }

  if (role === 'owner') {
    const people = http.expectOne((req) => req.url.includes('/api/employee-directory'));
    expect(people.request.method).toBe('GET');
    people.flush([
      { id: 'tech-1', displayName: 'Shop Tech', role: 'technician', active: true },
      { id: 'own-1', displayName: 'Owner Apple', role: 'owner', active: true },
    ]);
  } else {
    const people = http.match((req) => req.url.includes('/api/employee-directory'));
    expect(people).toHaveLength(0);
  }

  fixture.detectChanges();
  await fixture.whenStable();
  return { http, fixture, host: fixture.nativeElement as HTMLElement, confirm: confirmObj };
}

describe('Leave', () => {
  afterEach(() => {
    try {
      TestBed.inject(HttpTestingController).verify();
    } finally {
      TestBed.resetTestingModule();
      document.querySelectorAll('.cdk-overlay-container').forEach((el) => el.remove());
    }
  });

  it('technician cannot approve, reject, add leave for others, or set days', async () => {
    const { host } = await mount('technician');
    expect(host.querySelector('button[name="approve"]')).toBeNull();
    expect(host.querySelector('button[name="reject"]')).toBeNull();
    expect(host.querySelector('button[name="addLeave"]')).toBeNull();
    expect(host.querySelector('button[name="leaveMoreMenu"]')).toBeNull();
    expect(document.querySelector('[data-allotment]')).toBeNull();
  });

  it('purchaser Admin cannot approve or see team balances', async () => {
    const { host } = await mount('admin', undefined, confirmStub(true), 'purchaser');
    expect(host.querySelector('button[name="approve"]')).toBeNull();
    expect(host.querySelector('button[name="reject"]')).toBeNull();
    expect(host.textContent).not.toContain('Team balances');
    expect(host.textContent).not.toContain('Needs a decision');
  });

  it('general manager can approve, cannot open Set days / Add leave for someone else', async () => {
    const { host } = await mount('admin', undefined, confirmStub(true), 'general_manager');
    expect(host.querySelector('button[name="approve"]')).toBeTruthy();
    expect(host.querySelector('button[name="reject"]')).toBeTruthy();
    expect(document.querySelector('[data-allotment]')).toBeNull();
    expect(document.querySelector('input[name="allotmentDays"]')).toBeNull();
    expect(host.querySelector('button[name="leaveMoreMenu"]')).toBeNull();
    expect(host.querySelector('button[name="addLeave"]')).toBeNull();
  });

  it('general manager cannot see approve or reject on their own request but can on others', async () => {
    const adminPending: LeaveRequestRow = {
      id: 'leave-admin-1',
      staffId: 'admin',
      displayName: 'Tester',
      leaveType: 'vacation',
      startDate: '2026-06-01',
      endDate: '2026-06-02',
      status: 'pending',
      note: null,
      halfDay: null,
      days: 1,
      cancelReason: null,
    };
    const { host } = await mount(
      'admin',
      [pending, adminPending],
      confirmStub(true),
      'general_manager',
    );
    const approveButtons = host.querySelectorAll('button[name="approve"]');
    const rejectButtons = host.querySelectorAll('button[name="reject"]');
    expect(approveButtons).toHaveLength(1);
    expect(rejectButtons).toHaveLength(1);
  });

  it('rejecting a pending request confirms with danger tone and calls API', async () => {
    const confirm = confirmStub(true);
    const { http, fixture, host } = await mount('owner', undefined, confirm);

    const rejectBtn = host.querySelector('button[name="reject"]') as HTMLButtonElement;
    expect(rejectBtn).toBeTruthy();
    rejectBtn.click();
    fixture.detectChanges();

    expect(confirm.calls).toHaveLength(1);
    expect(confirm.calls[0].tone).toBe('danger');
    expect(confirm.calls[0].title).toContain('Reject leave request?');

    const decideReq = http.expectOne(
      (r) => r.method === 'POST' && r.url.includes('/api/leave/leave-1/decision'),
    );
    expect(decideReq.request.body).toEqual({ decision: 'rejected' });
    decideReq.flush({ ok: true });

    // Triggers reload
    const shopReq = http.expectOne((r) => r.url.includes('/api/leave/balances'));
    shopReq.flush([]);
    const pendingListReq = http.expectOne(
      (r) =>
        r.method === 'GET' &&
        r.url.includes('/api/leave') &&
        !r.url.includes('/balances') &&
        r.params.get('status') === 'pending',
    );
    pendingListReq.flush([]);
    const historyListReq = http.expectOne(
      (r) =>
        r.method === 'GET' &&
        r.url.includes('/api/leave') &&
        !r.url.includes('/balances') &&
        r.params.get('status') === 'decided',
    );
    historyListReq.flush([]);
  });

  it('paginates leave history for owner across pages', async () => {
    const manyHistory: LeaveRequestRow[] = Array.from({ length: 15 }, (_, i) => ({
      id: `hist-${i + 1}`,
      staffId: 'tech-1',
      displayName: `Technician ${String(i + 1).padStart(2, '0')}`,
      leaveType: 'vacation',
      startDate: '2026-05-01',
      endDate: '2026-05-02',
      status: 'approved',
      note: `Approved note ${String(i + 1).padStart(2, '0')}`,
      halfDay: null,
      days: 1,
      cancelReason: null,
    }));

    const { fixture, host } = await mount('owner', manyHistory);

    expect(host.textContent).toContain('Technician 01');
    expect(host.textContent).toContain('Technician 10');
    expect(host.textContent).not.toContain('Technician 11');

    fixture.componentInstance.onHistoryPage({ pageIndex: 1, pageSize: 10, length: 15 });
    fixture.detectChanges();

    expect(host.textContent).not.toContain('Technician 01');
    expect(host.textContent).toContain('Technician 11');
    expect(host.textContent).toContain('Technician 15');
  });

  it('keeps the balance cards at a placeholder until the balances request answers', async () => {
    await TestBed.configureTestingModule({
      imports: [LeavePage],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { me: signal(me('technician')) } },
        { provide: OpsConfirmService, useValue: confirmStub(true).service },
      ],
    }).compileComponents();

    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(LeavePage);
    fixture.detectChanges();

    // Balances load in parallel with the request list, so the cards must not fake a 0 first.
    const host = fixture.nativeElement as HTMLElement;
    const before = host.querySelector('[data-balance="vacation"]');
    expect(before?.textContent).toContain('—');
    expect(before?.textContent).not.toContain('Allotted');

    http
      .expectOne(
        (req) => req.url.includes('/api/leave/balances') && !req.urlWithParams.includes('shop=1'),
      )
      .flush(structuredClone(balances));
    http
      .expectOne(
        (req) =>
          req.method === 'GET' && req.url.includes('/api/leave') && !req.url.includes('/balances'),
      )
      .flush([structuredClone(pending)]);

    fixture.detectChanges();
    await fixture.whenStable();

    expect(host.querySelector('[data-balance="vacation"]')?.textContent).toContain('Allotted: 10');
  });

  it('shows the date parse message for a bad typed leave start date', async () => {
    const { fixture, host } = await mount('technician');

    (host.querySelector('button[name="requestLeaveOpen"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();

    const dialog = document.body.querySelector('mat-dialog-container') as HTMLElement;
    const startInput = dialog.querySelector('input[name="startDate"]') as HTMLInputElement;
    startInput.value = '13/25/2026';
    startInput.dispatchEvent(new Event('input'));
    startInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(dialog.textContent).toContain('Enter a date as MM/DD/YYYY.');
  });

  it('shows pending next-year requests in Needs a decision and My requests', async () => {
    const nextYearPending: LeaveRequestRow = {
      id: 'leave-next-year',
      staffId: 'tech-1',
      displayName: 'Shop Tech',
      leaveType: 'vacation',
      startDate: '2027-01-01',
      endDate: '2027-01-05',
      status: 'pending',
      note: 'New year trip',
      halfDay: null,
      days: 3,
      cancelReason: null,
    };

    const { host } = await mount(
      'admin',
      [pending, nextYearPending],
      confirmStub(true),
      'general_manager',
    );
    expect(host.textContent).toContain('New year trip');
    expect(host.textContent).toContain('Jan 1 – 5, 2027');
    expect(host.querySelectorAll('button[name="approve"]')).toHaveLength(2);
  });

  it('switches the history year picker and reloads decided history for that year', async () => {
    const { http, fixture, host } = await mount('owner', [approvedHistory]);

    fixture.componentInstance.onHistoryYearChange(2027);
    const req2027 = http.expectOne(
      (req) =>
        req.method === 'GET' &&
        req.url.includes('/api/leave') &&
        !req.url.includes('/balances') &&
        req.params.get('status') === 'decided' &&
        req.params.get('year') === '2027',
    );
    req2027.flush([]);
    fixture.detectChanges();

    expect(host.textContent).toContain('No decision history for 2027.');
  });

  it('shows expiry subtext on balance cards and warns when expired', async () => {
    const expiringBalances: LeaveBalance[] = [
      {
        leaveType: 'vacation',
        allotment: 10,
        used: 6,
        pending: 0,
        remaining: 0,
        expiresOn: '2026-06-30',
        expired: 4,
        convertibleToCash: false,
        hasCustomExpiry: true,
      },
      {
        leaveType: 'sick',
        allotment: 5,
        used: 0,
        pending: 0,
        remaining: 5,
        expiresOn: '2099-12-31',
        expired: 0,
        convertibleToCash: false,
      },
      {
        leaveType: 'unpaid',
        allotment: 0,
        used: 0,
        pending: 0,
        remaining: 0,
        expiresOn: null,
        expired: 0,
        convertibleToCash: false,
      },
      {
        leaveType: 'service_incentive',
        allotment: 5,
        used: 1,
        pending: 0,
        remaining: 4,
        expiresOn: '2026-12-31',
        expired: 0,
        convertibleToCash: true,
      },
    ];

    const { host } = await mount(
      'technician',
      undefined,
      confirmStub(true),
      null,
      expiringBalances,
    );

    const vacCard = host.querySelector('[data-balance="vacation"]');
    expect(vacCard?.textContent).toContain('4 expired on Jun 30, 2026');
    expect(vacCard?.querySelector('.ops-tile.warn')).toBeTruthy();

    const silCard = host.querySelector('[data-balance="service_incentive"]');
    expect(silCard?.textContent).toContain('Cycle ends Dec 31, 2026 · convertible to cash');
    expect(silCard?.querySelector('.ops-tile.warn')).toBeNull();
  });

  it('shows compact expiry subtext in team balances and prefills custom expiry in Set allotment', async () => {
    const teamWithExpiry: StaffLeaveBalances[] = [
      {
        staffId: 'tech-1',
        displayName: 'Shop Tech',
        role: 'technician',
        balances: [
          {
            leaveType: 'vacation',
            allotment: 10,
            used: 2,
            pending: 0,
            remaining: 8,
            expiresOn: '2099-09-30',
            expired: 0,
            convertibleToCash: false,
            hasCustomExpiry: true,
            cycleYear: 2099,
          },
          {
            leaveType: 'sick',
            allotment: 5,
            used: 0,
            pending: 0,
            remaining: 5,
            expiresOn: '2099-07-14',
            expired: 0,
            convertibleToCash: false,
            usesHireAnniversary: true,
            cycleYear: 2099,
          },
          { leaveType: 'unpaid', allotment: 0, used: 0, pending: 0, remaining: 0, cycleYear: 2099 },
          {
            leaveType: 'service_incentive',
            allotment: 5,
            used: 0,
            pending: 0,
            remaining: 5,
            expiresOn: '2099-07-14',
            expired: 0,
            convertibleToCash: true,
            usesHireAnniversary: true,
            cycleYear: 2099,
          },
        ],
      },
    ];

    const { http, fixture, host } = await mount(
      'owner',
      undefined,
      confirmStub(true),
      null,
      undefined,
      teamWithExpiry,
    );

    expect(host.textContent).toContain('Expires Sep 30, 2099 (custom)');
    expect(host.textContent).toContain('Expires Jul 14, 2099');
    expect(host.textContent).toContain('Cycle ends Jul 14, 2099');

    // Open Set allotment dialog and verify custom expiry prefill and submission.
    const moreBtn = host.querySelector('button[name="leaveMoreMenu"]') as HTMLButtonElement;
    moreBtn.click();
    fixture.detectChanges();
    await fixture.whenStable();

    const setAllotmentBtn = document.body.querySelector(
      'button[name="setAllotment"]',
    ) as HTMLButtonElement;
    setAllotmentBtn.click();
    fixture.detectChanges();
    await fixture.whenStable();

    const dialog = document.body.querySelector('mat-dialog-container') as HTMLElement;
    const yearInput = dialog.querySelector('input[name="allotmentYear"]') as HTMLInputElement;
    const daysInput = dialog.querySelector('input[name="allotmentDays"]') as HTMLInputElement;
    const expiresInput = dialog.querySelector(
      'input[name="allotmentExpiresOn"]',
    ) as HTMLInputElement;
    expect(yearInput.value).toBe('2099');
    expect(daysInput.value).toBe('10');
    expect(expiresInput).toBeTruthy();
    expect(expiresInput.value).toBe('Sep 30, 2099');

    // Changing year away from active cycle clears custom expiry and hides active-cycle hint.
    yearInput.value = '2100';
    yearInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await fixture.whenStable();
    expect(expiresInput.value).toBe('');
    expect(dialog.textContent).not.toContain('Current allotted days:');

    // Switching back to active cycle year restores prefill and hint.
    yearInput.value = '2099';
    yearInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await fixture.whenStable();
    expect(dialog.textContent).toContain('Current allotted days:');

    // Switching to SIL hides custom expiry picker and shows cash-conversion note.
    const typeSelect = dialog.querySelector('select[name="allotmentType"]') as HTMLSelectElement;
    typeSelect.value = 'service_incentive';
    typeSelect.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    await fixture.whenStable();
    expect(dialog.querySelector('input[name="allotmentExpiresOn"]')).toBeNull();
    expect(dialog.textContent).toContain('Service incentive leave (SIL) does not expire');
    expect(daysInput.value).toBe('5');

    // Switch back to vacation to test parse error and custom expiry submission.
    typeSelect.value = 'vacation';
    typeSelect.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    await fixture.whenStable();
    const vacExpiresInput = dialog.querySelector(
      'input[name="allotmentExpiresOn"]',
    ) as HTMLInputElement;
    expect(vacExpiresInput).toBeTruthy();

    // Bad typed date shows parse error.
    vacExpiresInput.value = '13/45/2099';
    vacExpiresInput.dispatchEvent(new Event('input'));
    vacExpiresInput.dispatchEvent(new Event('change'));
    vacExpiresInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    await fixture.whenStable();
    expect(dialog.textContent).toContain('Enter a date as MM/DD/YYYY.');

    // Valid date submits custom expiresOn without wiping allotted days.
    vacExpiresInput.value = '09/30/2099';
    vacExpiresInput.dispatchEvent(new Event('input'));
    vacExpiresInput.dispatchEvent(new Event('change'));
    vacExpiresInput.dispatchEvent(new Event('blur'));
    daysInput.value = '12';
    daysInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await fixture.whenStable();

    const saveBtn = dialog.querySelector('button[name="saveAllotment"]') as HTMLButtonElement;
    saveBtn.click();
    fixture.detectChanges();

    const putReq = http.expectOne(
      (req) => req.method === 'PUT' && req.url.includes('/api/leave/allotments'),
    );
    expect(putReq.request.body).toEqual({
      staffId: 'tech-1',
      year: 2099,
      leaveType: 'vacation',
      days: 12,
      expiresOn: '2099-09-30',
    });
    putReq.flush({
      staffId: 'tech-1',
      year: 2099,
      leaveType: 'vacation',
      days: 12,
      expiresOn: '2099-09-30',
    });
    fixture.detectChanges();
    await fixture.whenStable();

    // Reload after dialog closes.
    http
      .expectOne((r) => r.url.includes('/api/leave/balances') && r.urlWithParams.includes('shop=1'))
      .flush([]);
    http
      .expectOne((r) => r.url.includes('/api/leave') && r.params.get('status') === 'pending')
      .flush([]);
    http
      .expectOne((r) => r.url.includes('/api/leave') && r.params.get('status') === 'decided')
      .flush([]);
  });
});

describe('crossYearSplitHint', () => {
  it('explains how to split cross-year ranges and returns null for same-year dates', () => {
    expect(crossYearSplitHint('2026-12-28', '2026-12-31')).toBeNull();
    expect(crossYearSplitHint('2026-12-31', '2026-12-28')).toBeNull();
    expect(crossYearSplitHint('', '2027-01-03')).toBeNull();

    const hint = crossYearSplitHint('2026-12-28', '2027-01-03');
    expect(hint).toContain('Dec 28, 2026–Dec 31, 2026');
    expect(hint).toContain('Jan 1–Jan 3, 2027');

    const multiYear = crossYearSplitHint('2026-12-28', '2028-01-03');
    expect(multiYear).toContain('calendar year');
    expect(multiYear).toContain('Dec 28, 2026–Dec 31, 2026');
  });
});
