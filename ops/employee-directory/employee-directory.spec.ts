import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router } from '@angular/router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AuthMe, AuthService } from '@hr-host';
import { EmployeeCard } from './employee-directory.api';
import { EmployeeDirectoryPage } from './employee-directory';

const activePerson: EmployeeCard = {
  id: 'p-active',
  role: 'technician',
  displayName: 'Ana Active',
  email: 'ana.active@rakkiicars.ph',
  mobile: '09170000001',
  hireDate: '2024-02-01',
  active: true,
  emergencyContactName: 'Jose',
  emergencyContactPhone: '09180000001',
  position: 'Lead painter',
  status: 'active',
  branchId: 'branch-1',
  canCanvassAndPaySuppliers: false,
  canOpenLoaPdf: true,
  canSeeLoaPesos: false,
  mustClock: true,
  payBasis: 'daily',
  ratePhp: 800,
};

const inactivePerson: EmployeeCard = {
  id: 'p-inactive',
  role: 'sales',
  displayName: 'Ida Away',
  email: 'ida.away@rakkiicars.ph',
  mobile: null,
  hireDate: '2022-08-12',
  active: false,
  emergencyContactName: null,
  emergencyContactPhone: null,
};

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

async function mount(
  role: AuthMe['staffRole'],
  people: EmployeeCard[] = [activePerson, inactivePerson],
  healthIssues: any[] = [],
) {
  const routerSpy = {
    navigate: vi.fn(),
  };

  await TestBed.configureTestingModule({
    imports: [EmployeeDirectoryPage],
    providers: [
      provideZonelessChangeDetection(),
      provideNoopAnimations(),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: AuthService, useValue: { me: signal(me(role)) } },
      { provide: Router, useValue: routerSpy },
    ],
  }).compileComponents();

  const http = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(EmployeeDirectoryPage);
  fixture.detectChanges();
  const initial = http.expectOne(
    (req) => req.url.includes('/api/employee-directory') && !req.url.includes('/documents'),
  );
  expect(initial.request.method).toBe('GET');
  expect(initial.request.params.get('includeInactive')).toBe('false');
  expect(initial.request.params.has('search')).toBe(false);
  initial.flush({
    headcount: people.filter((p) => p.active).length,
    items: structuredClone(people),
    configHealth: healthIssues,
  });

  fixture.detectChanges();
  await fixture.whenStable();
  return { http, fixture, host: fixture.nativeElement as HTMLElement, router: routerSpy };
}

describe('Employee directory', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  it('hides inactive people until include inactive is on', async () => {
    const { http, fixture, host } = await mount('technician');
    expect(host.textContent).toContain('Include inactive');
    expect(host.textContent).toContain('Ana Active');
    expect(host.textContent).toContain('Active');
    expect(host.textContent).toContain('1 active');
    expect(host.textContent).toContain('Position');
    expect(host.textContent).toContain('Lead painter');
    expect(host.textContent).not.toContain('Ida Away');

    const box = host.querySelector(
      'mat-checkbox[name="includeInactive"] input',
    ) as HTMLInputElement;
    box.click();
    fixture.detectChanges();

    const again = http.expectOne(
      (req) =>
        req.url.includes('/api/employee-directory') && req.params.get('includeInactive') === 'true',
    );
    again.flush({
      headcount: 1,
      items: structuredClone([activePerson, inactivePerson]),
      configHealth: [],
    });
    fixture.detectChanges();
    await fixture.whenStable();

    expect(host.textContent).toContain('Ida Away');
    expect(host.textContent).toContain('Inactive');
  });

  it('technician sees their row and template has no edit or add button', async () => {
    const techPerson: EmployeeCard = {
      ...activePerson,
      displayName: 'Tech Worker',
    };
    const { host } = await mount('technician', [techPerson]);
    expect(host.querySelector('button[name="edit"]')).toBeNull();
    expect(host.querySelector('button[name="deactivate"]')).toBeNull();
    expect(host.textContent).not.toContain('Add person');
    expect(host.textContent).toContain('Tech Worker');
  });

  it('admin edit shows role read-only and sends no role', async () => {
    const { http, fixture, host } = await mount('admin');
    (host.querySelector('button[name="rowMenu"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();
    (document.body.querySelector('button[name="edit"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(document.body.querySelector('mat-select[name="role"]')).toBeNull();
    const roleInput = document.body.querySelector('input[name="roleReadonly"]') as HTMLInputElement;
    expect(roleInput.value).toBe('Technician');
    expect(roleInput.disabled).toBe(true);

    const form = document.body.querySelector('form#directory-person') as HTMLFormElement;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
    const patch = http.expectOne(
      (req) => req.method === 'PATCH' && req.url.includes('/api/employee-directory/p-active'),
    );
    expect(patch.request.body.role).toBeUndefined();
    expect(patch.request.body.position).toBe('Lead painter');
    patch.flush(activePerson);
    fixture.detectChanges();
    await fixture.whenStable();

    const reload = http.expectOne(
      (req) => req.url.includes('/api/employee-directory') && !req.url.includes('/documents'),
    );
    reload.flush({ headcount: 1, items: [activePerson], configHealth: [] });
  });

  it('owner edit prefills pay and permissions and omits clear flags when untouched', async () => {
    const { http, fixture, host } = await mount('owner');
    (host.querySelector('button[name="rowMenu"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();
    (document.body.querySelector('button[name="edit"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    // The demo owner works the Demo shop only, so no live-shop list is fetched for the picker.
    http.expectNone((req) => req.url.includes('/api/settings/branches'));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(document.body.querySelector('mat-select[name="role"]')).toBeTruthy();
    expect(document.body.textContent).toContain('Pay rate');
    expect(document.body.textContent).toContain('Can canvass & pay suppliers');
    expect(document.body.textContent).toContain('Uses time clock');

    const rateInput = document.body.querySelector('input[name="ratePhp"]') as HTMLInputElement;
    expect(rateInput.value).toBe('800');
    const canvass = document.body.querySelector(
      'mat-checkbox[name="canCanvassAndPaySuppliers"] input',
    ) as HTMLInputElement;
    expect(canvass.checked).toBe(false);

    const form = document.body.querySelector('form#directory-person') as HTMLFormElement;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
    const patch = http.expectOne(
      (req) => req.method === 'PATCH' && req.url.includes('/api/employee-directory/p-active'),
    );
    expect(patch.request.body.role).toBe('technician');
    expect(patch.request.body.clearPayBasis).toBeUndefined();
    expect(patch.request.body.clearRate).toBeUndefined();
    expect(patch.request.body.payBasis).toBeUndefined();
    expect(patch.request.body.ratePhp).toBeUndefined();
    expect(patch.request.body.canCanvassAndPaySuppliers).toBeUndefined();
    patch.flush(activePerson);
    fixture.detectChanges();
    await fixture.whenStable();

    const reload = http.expectOne(
      (req) => req.url.includes('/api/employee-directory') && !req.url.includes('/documents'),
    );
    reload.flush({ headcount: 1, items: [activePerson], configHealth: [] });
  });

  it('owner row has no edit button for admin, but has edit button for owner', async () => {
    const ownerPerson: EmployeeCard = {
      ...activePerson,
      id: 'p-owner',
      role: 'owner',
      displayName: 'Boss Owner',
    };
    const { fixture: adminFix, host: adminHost } = await mount('admin', [ownerPerson]);
    (adminHost.querySelector('button[name="rowMenu"]') as HTMLButtonElement).click();
    adminFix.detectChanges();
    await adminFix.whenStable();
    expect(document.body.querySelector('button[name="edit"]')).toBeNull();

    TestBed.resetTestingModule();

    const { fixture: ownerFix, host: ownerHost } = await mount('owner', [ownerPerson]);
    (ownerHost.querySelector('button[name="rowMenu"]') as HTMLButtonElement).click();
    ownerFix.detectChanges();
    await ownerFix.whenStable();
    expect(document.body.querySelector('button[name="edit"]')).not.toBeNull();
  });
});
