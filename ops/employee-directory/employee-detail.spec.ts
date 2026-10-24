import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { BehaviorSubject, of } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AuthMe, AuthService, OpsConfirmService } from '@hr-host';
import { EmployeeCard } from './employee-directory.api';
import { EmployeeDetailPage } from './employee-detail';

const testEmployee: EmployeeCard = {
  id: 'p-1',
  role: 'technician',
  displayName: 'Mario Speedwagon',
  email: 'mario@rakkiicars.ph',
  mobile: '09171234567',
  hireDate: '2023-05-15',
  active: true,
  emergencyContactName: 'Luigi',
  emergencyContactPhone: '09181234567',
  position: 'Master Tech',
  department: 'Mechanical',
  employeeNo: 'EMP-001',
  tin: '123-456-789',
  sss: '01-2345678-9',
  philHealth: '12-345678901-2',
  pagIbig: '1234-5678-9012',
  status: 'active',
};

function me(role: AuthMe['staffRole'], staffId?: string | null): AuthMe {
  return {
    authUserId: role ?? 'staff',
    email: `${role}@rakkiicars.ph`,
    profileKind: 'staff',
    staffRole: role,
    branchId: 'branch-1',
    branchCode: 'demo',
    isStaff: true,
    displayName: 'Tester',
    staffId,
  };
}

async function mountDetail(
  role: AuthMe['staffRole'],
  tab = 'overview',
  employee: EmployeeCard = testEmployee,
) {
  const routerSpy = {
    navigate: vi.fn(),
  };

  const queryParams$ = new BehaviorSubject(convertToParamMap({ tab }));
  const routeStub = {
    snapshot: {
      paramMap: convertToParamMap({ id: employee.id }),
      queryParamMap: convertToParamMap({ tab }),
      data: {},
    },
    queryParamMap: queryParams$.asObservable(),
  };

  const confirmSpy = {
    confirm: vi.fn().mockReturnValue(of({ confirmed: true })),
  };

  await TestBed.configureTestingModule({
    imports: [EmployeeDetailPage],
    providers: [
      provideZonelessChangeDetection(),
      provideNoopAnimations(),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: AuthService, useValue: { me: signal(me(role)) } },
      { provide: Router, useValue: routerSpy },
      { provide: ActivatedRoute, useValue: routeStub },
      { provide: OpsConfirmService, useValue: confirmSpy },
    ],
  }).compileComponents();

  const http = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(EmployeeDetailPage);
  fixture.detectChanges();

  // Initial get employee request
  const getReq = http.expectOne(`/api/employee-directory/${employee.id}`);
  expect(getReq.request.method).toBe('GET');
  getReq.flush(structuredClone(employee));

  // Branch and shop calls. The shop list is owner-only settings, so other roles skip it.
  if (role === 'owner') {
    http.expectOne('/api/settings/branches').flush([]);
  } else {
    http.expectNone('/api/settings/branches');
  }
  const shopReq = http.expectOne('/api/shop');
  shopReq.flush({
    tradingName: 'Demo Shop',
    legalName: 'Demo Cars Inc.',
    address: 'Quezon City',
    phone: '09170000000',
    tin: '000-000-000',
  });

  // Handle tab-specific requests triggered on mount
  if (tab === 'pay' && (role === 'owner' || role === 'admin')) {
    const payReq = http.expectOne(`/api/ops/payroll/rates/${employee.id}`);
    payReq.flush({
      staffId: employee.id,
      staffName: employee.displayName,
      basis: 'daily',
      ratePhp: 800,
      otAfterHoursPerDay: 8,
      otMultiplier: 1.25,
      history: [],
    });
    const allowanceReq = http.expectOne(
      (req) =>
        req.url === '/api/ops/payroll/allowances' && req.params.get('staffId') === employee.id,
    );
    expect(allowanceReq.request.method).toBe('GET');
    allowanceReq.flush({ allowances: [], suggestedNames: [] });
  } else if (tab === 'loans' && (role === 'owner' || role === 'admin')) {
    const loanReq = http.expectOne(
      (req) => req.url === '/api/ops/payroll/loans' && req.params.get('staffId') === employee.id,
    );
    expect(loanReq.request.method).toBe('GET');
    expect(loanReq.request.params.get('includeClosed')).toBe('true');
    loanReq.flush({ loans: [], suggestedKinds: [] });
  } else if (tab === 'documents') {
    const docReq = http.expectOne(`/api/employee-directory/${employee.id}/documents`);
    docReq.flush([]);
  } else if (tab === 'leave' && (role === 'owner' || role === 'admin')) {
    const balReq = http.expectOne((req) => req.url.includes('/api/leave/balances'));
    balReq.flush([]);
    const listReq = http.expectOne((req) => req.url.includes('/api/leave'));
    listReq.flush([]);
  } else if (tab === 'history' && (role === 'owner' || role === 'admin')) {
    const histReq = http.expectOne((req) => req.url.includes('/api/hr/change-log'));
    histReq.flush({ rows: [] });
  }

  fixture.detectChanges();
  await fixture.whenStable();

  return {
    http,
    fixture,
    host: fixture.nativeElement as HTMLElement,
    router: routerSpy,
    confirm: confirmSpy,
  };
}

describe('EmployeeDetailPage', () => {
  afterEach(() => {
    document.querySelectorAll('.cdk-overlay-container').forEach((el) => el.remove());
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  it('shows Edit button for owner and admin, but hides Edit for technician', async () => {
    const { host: ownerHost } = await mountDetail('owner');
    const ownerEditBtn = ownerHost.querySelector('ops-page-header button[matButton="filled"]');
    expect(ownerEditBtn).toBeTruthy();
    expect(ownerEditBtn?.textContent?.trim()).toBe('Edit');

    TestBed.resetTestingModule();

    const { host: techHost } = await mountDetail('technician');
    const techEditBtn = techHost.querySelector('ops-page-header button[matButton="filled"]');
    expect(techEditBtn).toBeNull();
  });

  it('shows 6 tabs for owner/admin and only 2 tabs for technician (hiding Pay, Loans, Leave, History)', async () => {
    const { host: ownerHost } = await mountDetail('owner');
    const ownerTabs = Array.from(ownerHost.querySelectorAll('[role="tab"]')).map((t) =>
      t.textContent?.trim(),
    );
    expect(ownerTabs).toEqual([
      'Overview',
      'Pay',
      'Loans / Cash advance',
      'Documents',
      'Leave',
      'Scorecards',
      'History',
    ]);

    TestBed.resetTestingModule();

    const { host: techHost } = await mountDetail('technician');
    const techTabs = Array.from(techHost.querySelectorAll('[role="tab"]')).map((t) =>
      t.textContent?.trim(),
    );
    expect(techTabs).toEqual(['Overview', 'Documents']);
  });

  it('selects the Documents tab when query param ?tab=documents is passed', async () => {
    const { host } = await mountDetail('owner', 'documents');
    const activeTab = host.querySelector('[role="tab"][aria-selected="true"]');
    expect(activeTab?.textContent?.trim()).toBe('Documents');
    expect(host.textContent).toContain('Employee documents');
  });

  it('deactivate asks for confirmation via OpsConfirmService before patching API', async () => {
    const { http, fixture, host, confirm } = await mountDetail('owner');
    const moreBtn = host.querySelector('button[aria-label="More actions"]') as HTMLButtonElement;
    expect(moreBtn).toBeTruthy();
    moreBtn.click();
    fixture.detectChanges();
    await fixture.whenStable();

    const deactivateBtn = Array.from(document.body.querySelectorAll('button[mat-menu-item]')).find(
      (b) => b.textContent?.includes('Deactivate'),
    ) as HTMLButtonElement;
    expect(deactivateBtn).toBeTruthy();
    deactivateBtn.click();
    fixture.detectChanges();
    await fixture.whenStable();

    // Deactivation checks open work first. No jobs here, so the plain confirm follows.
    const workReq = http.expectOne(`/api/ops/staff/${testEmployee.id}/open-work`);
    workReq.flush({
      staffId: testEmployee.id,
      displayName: testEmployee.displayName,
      active: true,
      jobs: [],
      pendingLeave: [],
      approvedLeave: [],
      openPunch: null,
    });
    fixture.detectChanges();
    await fixture.whenStable();

    expect(confirm.confirm).toHaveBeenCalledWith(
      expect.objectContaining({
        title: expect.stringContaining('Deactivate Mario Speedwagon'),
        tone: 'danger',
      }),
    );

    const patchReq = http.expectOne(`/api/employee-directory/${testEmployee.id}`);
    expect(patchReq.request.method).toBe('PATCH');
    expect(patchReq.request.body).toEqual({ active: false });
    patchReq.flush({ ...testEmployee, active: false });

    // Reload after update
    const reloadReq = http.expectOne(`/api/employee-directory/${testEmployee.id}`);
    reloadReq.flush({ ...testEmployee, active: false });
  });

  it('hides Edit button for admin viewing owner, but shows Edit button for admin viewing tech', async () => {
    const ownerPerson: EmployeeCard = {
      ...testEmployee,
      id: 'p-owner',
      role: 'owner',
      displayName: 'Boss Owner',
    };
    const { host: adminViewingOwner } = await mountDetail('admin', 'overview', ownerPerson);
    const editBtnOwner = adminViewingOwner.querySelector(
      'ops-page-header button[matButton="filled"]',
    );
    expect(editBtnOwner).toBeNull();

    TestBed.resetTestingModule();

    const { host: adminViewingTech } = await mountDetail('admin', 'overview', testEmployee);
    const editBtnTech = adminViewingTech.querySelector(
      'ops-page-header button[matButton="filled"]',
    );
    expect(editBtnTech).toBeTruthy();
    expect(editBtnTech?.textContent?.trim()).toBe('Edit');
  });

  it.each(['owner', 'admin', 'sales', 'technician'] as const)(
    'loads the %s caller profile read-only and does not stay on the skeleton',
    async (role) => {
      const staffId = `self-${role}`;
      const person: EmployeeCard = {
        ...testEmployee,
        id: staffId,
        role,
        displayName: `${role} person`,
      };

      await TestBed.configureTestingModule({
        imports: [EmployeeDetailPage],
        providers: [
          provideZonelessChangeDetection(),
          provideNoopAnimations(),
          provideHttpClient(),
          provideHttpClientTesting(),
          { provide: AuthService, useValue: { me: signal(me(role, staffId)) } },
          { provide: Router, useValue: { navigate: vi.fn() } },
          {
            provide: ActivatedRoute,
            useValue: {
              snapshot: {
                paramMap: convertToParamMap({}),
                queryParamMap: convertToParamMap({}),
                data: { self: true },
              },
              queryParamMap: of(convertToParamMap({})),
            },
          },
          { provide: OpsConfirmService, useValue: { confirm: vi.fn() } },
        ],
      }).compileComponents();

      const http = TestBed.inject(HttpTestingController);
      const fixture = TestBed.createComponent(EmployeeDetailPage);
      fixture.detectChanges();

      const getReq = http.expectOne(`/api/employee-directory/${staffId}`);
      expect(getReq.request.method).toBe('GET');
      getReq.flush(person);
      if (role === 'owner') {
        http.expectOne('/api/settings/branches').flush([]);
      } else {
        http.expectNone('/api/settings/branches');
      }
      http.expectOne('/api/shop').flush({ tradingName: 'Demo Shop' });
      fixture.detectChanges();
      await fixture.whenStable();

      const host = fixture.nativeElement as HTMLElement;
      expect(host.querySelector('ops-skeleton')).toBeNull();
      expect(host.textContent).toContain(`${role} person`);
      expect(host.textContent).toContain('Overview');
      expect(host.querySelector('button[aria-label="More actions"]')).toBeNull();
      expect(host.textContent).not.toContain('Edit');
      expect(host.textContent).not.toContain('Change rate');
      expect(host.textContent).not.toContain('Deactivate');
      const tabs = Array.from(host.querySelectorAll('[role="tab"]')).map((t) =>
        t.textContent?.trim(),
      );
      expect(tabs).toEqual(['Overview', 'Documents', 'Scorecards']);
      expect(host.querySelector('a[href="/hr/employee-directory"]')).toBeNull();
    },
  );

  it('stops loading when the caller has no staff id', async () => {
    await TestBed.configureTestingModule({
      imports: [EmployeeDetailPage],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { me: signal(me('sales', null)) } },
        { provide: Router, useValue: { navigate: vi.fn() } },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: convertToParamMap({}),
              queryParamMap: convertToParamMap({}),
              data: { self: true },
            },
            queryParamMap: of(convertToParamMap({})),
          },
        },
        { provide: OpsConfirmService, useValue: { confirm: vi.fn() } },
      ],
    }).compileComponents();

    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(EmployeeDetailPage);
    fixture.detectChanges();
    http.expectNone('/api/settings/branches');
    http.expectOne('/api/shop').flush({ tradingName: 'Demo Shop' });
    fixture.detectChanges();
    await fixture.whenStable();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('ops-skeleton')).toBeNull();
    expect(host.textContent).toContain('Your profile is not available.');
    http.expectNone((req) => req.url.includes('/api/employee-directory/'));
  });
});
