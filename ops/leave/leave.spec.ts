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

const balances: LeaveBalance[] = [
  { leaveType: 'vacation', allotment: 10, used: 0, pending: 0, remaining: 10 },
  { leaveType: 'sick', allotment: 0, used: 0, pending: 0, remaining: 0 },
  { leaveType: 'unpaid', allotment: 0, used: 0, pending: 0, remaining: 0 },
];

const pending: LeaveRequestRow = {
  id: 'leave-1',
  staffId: 'tech-1',
  displayName: 'Demo Tech',
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
  displayName: 'Demo Tech',
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
    displayName: 'Demo Tech',
    role: 'technician',
    balances: [
      { leaveType: 'vacation', allotment: 10, used: 2, pending: 0, remaining: 8 },
      { leaveType: 'sick', allotment: 5, used: 0, pending: 0, remaining: 5 },
      { leaveType: 'unpaid', allotment: 0, used: 0, pending: 0, remaining: 0 },
    ],
  },
];

function me(role: AuthMe['staffRole']): AuthMe {
  return {
    authUserId: role ?? 'staff',
    email: `${role}@rakkiicars.ph`,
    profileKind: 'staff',
    staffRole: role,
    branchId: 'branch-1',
    branchCode: 'demo',
    isStaff: true,
    displayName: 'Tester',
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
) {
  await TestBed.configureTestingModule({
    imports: [LeavePage],
    providers: [
      provideZonelessChangeDetection(),
      provideNoopAnimations(),
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: AuthService, useValue: { me: signal(me(role)) } },
      { provide: OpsConfirmService, useValue: confirmObj.service },
    ],
  }).compileComponents();

  const http = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(LeavePage);
  fixture.detectChanges();

  const owner = role === 'owner';
  const decide = role === 'owner' || role === 'admin';
  const isList = (req: { method: string; url: string }) =>
    req.method === 'GET' && req.url.includes('/api/leave') && !req.url.includes('/balances');

  if (!owner) {
    const balanceReq = http.expectOne(
      (req) => req.url.includes('/api/leave/balances') && !req.urlWithParams.includes('shop=1'),
    );
    expect(balanceReq.request.method).toBe('GET');
    balanceReq.flush(structuredClone(balances));
  }
  if (decide) {
    const shopBalanceReq = http.expectOne(
      (req) => req.url.includes('/api/leave/balances') && req.urlWithParams.includes('shop=1'),
    );
    expect(shopBalanceReq.request.method).toBe('GET');
    shopBalanceReq.flush(structuredClone(teamBalances));
  }

  const shopRows = customRequests ?? [pending, approvedHistory];
  const ownRows = customRequests ?? [pending];
  if (decide) {
    const shopList = http.expectOne((req) => isList(req) && req.params.get('mine') !== 'true');
    expect(shopList.request.method).toBe('GET');
    shopList.flush(structuredClone(shopRows));
  }
  if (!owner) {
    const mineList = http.expectOne((req) => isList(req) && req.params.get('mine') === 'true');
    expect(mineList.request.method).toBe('GET');
    mineList.flush(structuredClone(ownRows));
  }

  if (role === 'owner') {
    const people = http.expectOne((req) => req.url.includes('/api/employee-directory'));
    expect(people.request.method).toBe('GET');
    people.flush([
      { id: 'tech-1', displayName: 'Demo Tech', role: 'technician', active: true },
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

  it('admin can approve, cannot open Set days / Add leave for someone else', async () => {
    const { host } = await mount('admin');
    expect(host.querySelector('button[name="approve"]')).toBeTruthy();
    expect(host.querySelector('button[name="reject"]')).toBeTruthy();
    expect(document.querySelector('[data-allotment]')).toBeNull();
    expect(document.querySelector('input[name="allotmentDays"]')).toBeNull();
    expect(host.querySelector('button[name="leaveMoreMenu"]')).toBeNull();
    expect(host.querySelector('button[name="addLeave"]')).toBeNull();
  });

  it('admin cannot see approve or reject buttons on their own request but can on others', async () => {
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
    const { host } = await mount('admin', [pending, adminPending]);
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
    const listReq = http.expectOne(
      (r) => r.method === 'GET' && r.url.includes('/api/leave') && !r.url.includes('/balances'),
    );
    listReq.flush([]);
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
});
