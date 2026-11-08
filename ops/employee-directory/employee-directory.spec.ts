import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatDialog } from '@angular/material/dialog';
import { Router } from '@angular/router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { provideNativeDateAdapter } from '@angular/material/core';
import { AuthService } from '@hr-host';
import { MEMBER_ROLE, specUser, type SpecUser } from '../host-spec-fixtures';
import { EmployeeCard } from './employee-directory.api';
import { EmployeeDirectoryPage } from './employee-directory';
import { HR_DIRECTORY_ROLES, type DirectoryRoleOption } from './directory-roles';

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

function me(role: string): SpecUser {
  return specUser(role, {
    authUserId: role || 'user',
    email: `${role || 'user'}@rakkiicars.ph`,
    branchId: 'branch-1',
    branchCode: 'demo',
    displayName: 'Tester',
  });
}

async function mount(
  role: string,
  people: EmployeeCard[] = [activePerson, inactivePerson],
  healthIssues: any[] = [],
  beforeFlush?: (host: HTMLElement) => void,
  extraProviders: { provide: unknown; useValue: unknown }[] = [],
) {
  const routerSpy = {
    navigate: vi.fn(),
  };

  await TestBed.configureTestingModule({
    imports: [EmployeeDirectoryPage],
    providers: [
      provideZonelessChangeDetection(),
      provideNoopAnimations(),
      provideNativeDateAdapter(),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: AuthService, useValue: { me: signal(me(role)) } },
      { provide: Router, useValue: routerSpy },
      ...extraProviders,
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
  beforeFlush?.(fixture.nativeElement as HTMLElement);
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
    vi.restoreAllMocks();
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  it('shows Loading in the subtitle until the list arrives, never 0 active', async () => {
    let early = '';
    const { host } = await mount('owner', [activePerson], [], (el) => {
      early = el.querySelector('ops-page-header')?.textContent ?? '';
    });
    expect(early).toContain('Loading');
    expect(early).not.toContain('0 active');
    expect(host.querySelector('ops-page-header')?.textContent).toContain('1 active');
  });

  it('hides inactive people until include inactive is on', async () => {
    const { http, fixture, host } = await mount(MEMBER_ROLE);
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
    const { host } = await mount(MEMBER_ROLE, [techPerson]);
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
    expect(document.body.textContent).toContain('May canvass & pay suppliers');
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
    expect(patch.request.body.role).toBeUndefined();
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

  const hostRoles: DirectoryRoleOption[] = [
    { value: 'owner', label: 'Owner', hint: 'Full access, including staff and settings.' },
    { value: 'admin', label: 'Admin', hint: 'Runs the shop day to day.' },
    { value: 'engineer', label: 'Engineer', hint: 'Clocks in on site.' },
  ];

  async function openOwnerEdit(
    host: HTMLElement,
    fixture: { detectChanges: () => void; whenStable: () => Promise<unknown> },
  ): Promise<void> {
    (host.querySelector('button[name="rowMenu"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();
    (document.body.querySelector('button[name="edit"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('owner edit of a stored role outside the host list omits role', async () => {
    const { http, fixture, host } = await mount('owner', [activePerson], [], undefined, [
      { provide: HR_DIRECTORY_ROLES, useValue: hostRoles },
    ]);
    await openOwnerEdit(host, fixture);

    const form = document.body.querySelector('form#directory-person') as HTMLFormElement;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
    const patch = http.expectOne(
      (req) => req.method === 'PATCH' && req.url.includes('/api/employee-directory/p-active'),
    );
    expect(patch.request.body.role).toBeUndefined();
    expect(patch.request.body.displayName).toBe('Ana Active');
    patch.flush(activePerson);
    fixture.detectChanges();
    await fixture.whenStable();
    const reload = http.expectOne(
      (req) => req.url.includes('/api/employee-directory') && !req.url.includes('/documents'),
    );
    reload.flush({ headcount: 1, items: [activePerson], configHealth: [] });
  });

  it('owner edit sends role when it changed', async () => {
    const { http, fixture, host } = await mount('owner');
    await openOwnerEdit(host, fixture);
    const dialog = TestBed.inject(MatDialog).openDialogs[0].componentInstance as { role: string };
    dialog.role = 'admin';

    const form = document.body.querySelector('form#directory-person') as HTMLFormElement;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
    const patch = http.expectOne(
      (req) => req.method === 'PATCH' && req.url.includes('/api/employee-directory/p-active'),
    );
    expect(patch.request.body.role).toBe('admin');
    patch.flush({ ...activePerson, role: 'admin' });
    fixture.detectChanges();
    await fixture.whenStable();
    const reload = http.expectOne(
      (req) => req.url.includes('/api/employee-directory') && !req.url.includes('/documents'),
    );
    reload.flush({ headcount: 1, items: [{ ...activePerson, role: 'admin' }], configHealth: [] });
  });

  it('owner add sends the default technician role', async () => {
    const { http, fixture, host } = await mount('owner');
    const add = Array.from(host.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Add person'),
    ) as HTMLButtonElement;
    add.click();
    fixture.detectChanges();
    await fixture.whenStable();
    // Demo owner: no live-shop list, same as the untouched edit spec.
    http.expectNone((req) => req.url.includes('/api/settings/branches'));

    const dialog = TestBed.inject(MatDialog).openDialogs[0].componentInstance as {
      displayName: string;
      email: string;
      hireDate: string;
    };
    dialog.displayName = 'Nia New';
    dialog.email = 'nia.new@rakkiicars.ph';
    dialog.hireDate = '2026-04-01';
    fixture.detectChanges();
    await fixture.whenStable();

    const form = document.body.querySelector('form#directory-person') as HTMLFormElement;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
    const post = http.expectOne(
      (req) =>
        req.method === 'POST' &&
        req.url.includes('/api/employee-directory') &&
        !req.url.includes('/documents'),
    );
    expect(post.request.body.role).toBe('technician');
    expect(post.request.body.displayName).toBe('Nia New');
    expect(post.request.body.email).toBe('nia.new@rakkiicars.ph');
    expect(post.request.body.hireDate).toBe('2026-04-01');
    post.flush({
      ...activePerson,
      id: 'p-new',
      displayName: 'Nia New',
      email: 'nia.new@rakkiicars.ph',
    });
    fixture.detectChanges();
    await fixture.whenStable();
    const reload = http.expectOne(
      (req) => req.url.includes('/api/employee-directory') && !req.url.includes('/documents'),
    );
    reload.flush({
      headcount: 2,
      items: [activePerson, { ...activePerson, id: 'p-new', displayName: 'Nia New' }],
      configHealth: [],
    });
  });

  it('posts a picked hire date as the local calendar day', async () => {
    const { http, fixture, host } = await mount('owner');
    const add = Array.from(host.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Add person'),
    ) as HTMLButtonElement;
    add.click();
    fixture.detectChanges();
    await fixture.whenStable();
    http.expectNone((req) => req.url.includes('/api/settings/branches'));

    const dialog = TestBed.inject(MatDialog).openDialogs[0].componentInstance as {
      displayName: string;
      email: string;
      hireDate: Date | string;
    };
    dialog.displayName = 'Nia New';
    dialog.email = 'nia.new@rakkiicars.ph';
    dialog.hireDate = new Date(2026, 0, 5);
    fixture.detectChanges();
    await fixture.whenStable();

    const form = document.body.querySelector('form#directory-person') as HTMLFormElement;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
    const post = http.expectOne(
      (req) =>
        req.method === 'POST' &&
        req.url.includes('/api/employee-directory') &&
        !req.url.includes('/documents'),
    );
    expect(post.request.body.hireDate).toBe('2026-01-05');
    expect(String(post.request.body.hireDate)).not.toContain('T');
    post.flush({
      ...activePerson,
      id: 'p-new',
      displayName: 'Nia New',
      email: 'nia.new@rakkiicars.ph',
      hireDate: '2026-01-05',
    });
    fixture.detectChanges();
    await fixture.whenStable();
    const reload = http.expectOne(
      (req) => req.url.includes('/api/employee-directory') && !req.url.includes('/documents'),
    );
    reload.flush({
      headcount: 2,
      items: [activePerson, { ...activePerson, id: 'p-new', displayName: 'Nia New' }],
      configHealth: [],
    });
  });

  it('posts the local hire day when toISOString reports the previous day', async () => {
    const { http, fixture, host } = await mount('owner');
    const add = Array.from(host.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Add person'),
    ) as HTMLButtonElement;
    add.click();
    fixture.detectChanges();
    await fixture.whenStable();
    http.expectNone((req) => req.url.includes('/api/settings/branches'));

    const hireDate = new Date(2026, 0, 5);
    const utc = new Date('2026-01-04T16:00:00.000Z');
    const toIso = vi.spyOn(hireDate, 'toISOString').mockReturnValue('2026-01-04T16:00:00.000Z');
    vi.spyOn(hireDate, 'getUTCFullYear').mockReturnValue(utc.getUTCFullYear());
    vi.spyOn(hireDate, 'getUTCMonth').mockReturnValue(utc.getUTCMonth());
    vi.spyOn(hireDate, 'getUTCDate').mockReturnValue(utc.getUTCDate());
    vi.spyOn(hireDate, 'getUTCDay').mockReturnValue(utc.getUTCDay());
    vi.spyOn(hireDate, 'getUTCHours').mockReturnValue(utc.getUTCHours());
    vi.spyOn(hireDate, 'getUTCMinutes').mockReturnValue(utc.getUTCMinutes());
    vi.spyOn(hireDate, 'getUTCSeconds').mockReturnValue(utc.getUTCSeconds());
    vi.spyOn(hireDate, 'getUTCMilliseconds').mockReturnValue(utc.getUTCMilliseconds());

    const dialog = TestBed.inject(MatDialog).openDialogs[0].componentInstance as {
      displayName: string;
      email: string;
      hireDate: Date | string;
    };
    dialog.displayName = 'Nia New';
    dialog.email = 'nia.new@rakkiicars.ph';
    dialog.hireDate = hireDate;

    const form = document.body.querySelector('form#directory-person') as HTMLFormElement;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
    const post = http.expectOne(
      (req) =>
        req.method === 'POST' &&
        req.url.includes('/api/employee-directory') &&
        !req.url.includes('/documents'),
    );
    expect(post.request.body.hireDate).toBe('2026-01-05');
    expect(String(post.request.body.hireDate)).not.toContain('T');
    expect(toIso).not.toHaveBeenCalled();
    post.flush({
      ...activePerson,
      id: 'p-new',
      displayName: 'Nia New',
      email: 'nia.new@rakkiicars.ph',
      hireDate: '2026-01-05',
    });
    fixture.detectChanges();
    await fixture.whenStable();
    const reload = http.expectOne(
      (req) => req.url.includes('/api/employee-directory') && !req.url.includes('/documents'),
    );
    reload.flush({
      headcount: 2,
      items: [activePerson, { ...activePerson, id: 'p-new', displayName: 'Nia New' }],
      configHealth: [],
    });
  });

  it('patches picked birth and separation dates as local calendar days', async () => {
    const { http, fixture, host } = await mount('owner');
    await openOwnerEdit(host, fixture);
    const dialog = TestBed.inject(MatDialog).openDialogs[0].componentInstance as {
      hireDate: Date | string;
      birthDate: Date | string;
      separationDate: Date | string;
    };
    dialog.hireDate = new Date(2026, 0, 5);
    dialog.birthDate = new Date(1990, 3, 15);
    dialog.separationDate = new Date(2026, 0, 5, 23, 30);
    fixture.detectChanges();
    await fixture.whenStable();

    const form = document.body.querySelector('form#directory-person') as HTMLFormElement;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
    const patch = http.expectOne(
      (req) => req.method === 'PATCH' && req.url.includes('/api/employee-directory/p-active'),
    );
    expect(patch.request.body.hireDate).toBe('2026-01-05');
    expect(patch.request.body.birthDate).toBe('1990-04-15');
    expect(patch.request.body.separationDate).toBe('2026-01-05');
    expect(String(patch.request.body.hireDate)).not.toContain('T');
    expect(String(patch.request.body.birthDate)).not.toContain('T');
    expect(String(patch.request.body.separationDate)).not.toContain('T');
    patch.flush({
      ...activePerson,
      hireDate: '2026-01-05',
      birthDate: '1990-04-15',
      separationDate: '2026-01-05',
    });
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
