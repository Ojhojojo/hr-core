import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { afterEach, describe, expect, it } from 'vitest';
import { AuthMe, AuthService } from '@hr-host';
import { AttendancePage, manilaWeekRange } from './attendance';
import { AttendanceRegisterResponse, OvertimeListResponse } from './attendance.models';

const ownerMe: AuthMe = {
  authUserId: 'owner',
  email: 'owner@rakkiicars.ph',
  profileKind: 'staff',
  staffRole: 'owner',
  branchId: null,
  branchCode: null,
  isStaff: true,
  displayName: 'Apple',
};

const techMe: AuthMe = {
  ...ownerMe,
  authUserId: 'tech',
  staffRole: 'technician',
  branchId: 'branch-1',
  branchCode: 'main-roosevelt',
  displayName: "Danilo 'Danny' Santos",
};

const adminMe: AuthMe = {
  ...ownerMe,
  authUserId: 'admin',
  staffRole: 'admin',
  branchId: 'branch-1',
  branchCode: 'main-roosevelt',
  displayName: 'Admin Staff',
};

function authStub(me: AuthMe) {
  return {
    me: signal(me),
  };
}

const openRow = {
  staffId: 'staff-1',
  staffName: "Danilo 'Danny' Santos",
  branchId: 'branch-1',
  branchName: 'Roosevelt Main',
  branchCode: 'main-roosevelt',
  inAtUtc: '2026-09-17T00:02:00.000Z',
  inAtManila: '2026-09-17 8:02 AM',
  inDateManila: '2026-09-17',
  outAtUtc: null,
  outAtManila: null,
  hours: null,
  isOpen: true,
};

const closedRow = {
  ...openRow,
  staffId: 'staff-2',
  staffName: 'Fairview Tech',
  branchId: 'branch-2',
  branchName: 'Fairview',
  branchCode: 'fairview',
  inAtUtc: '2026-09-17T01:00:00.000Z',
  inAtManila: '2026-09-17 9:00 AM',
  outAtUtc: '2026-09-17T09:00:00.000Z',
  outAtManila: '2026-09-17 5:00 PM',
  hours: 8,
  isOpen: false,
};

const register: AttendanceRegisterResponse = {
  from: '2026-09-13',
  to: '2026-09-19',
  rows: [openRow, closedRow],
};

const emptyOvertime = (from: string, to: string): OvertimeListResponse => ({
  from,
  to,
  rows: [],
});

/** After register succeeds, AttendancePage calls GET /api/ops/attendance/overtime. */
function flushOvertime(
  http: HttpTestingController,
  from: string,
  to: string,
  body: OvertimeListResponse = emptyOvertime(from, to),
): void {
  const req = http.expectOne(
    (r) =>
      r.url === '/api/ops/attendance/overtime' &&
      r.params.get('from') === from &&
      r.params.get('to') === to &&
      !r.params.has('mine'),
  );
  expect(req.request.method).toBe('GET');
  req.flush(body);
}

describe('AttendancePage', () => {
  afterEach(() => {
    try {
      const http = TestBed.inject(HttpTestingController, null, { optional: true });
      http?.verify();
    } finally {
      TestBed.resetTestingModule();
      document.querySelectorAll('.cdk-overlay-container').forEach((el) => el.remove());
    }
  });

  it('staff sees own week without manager filters or primary action button', async () => {
    await TestBed.configureTestingModule({
      imports: [AttendancePage],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: authStub(techMe) },
      ],
    }).compileComponents();

    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(AttendancePage);
    fixture.detectChanges();

    http.expectOne('/api/ops/attendance/register').flush({
      from: '2026-09-13',
      to: '2026-09-19',
      rows: [{ ...openRow, punchId: 'punch-1', hasInPhoto: true }],
    } satisfies AttendanceRegisterResponse);
    flushOvertime(http, '2026-09-13', '2026-09-19');
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('My week');
    expect(text).toContain('Open');
    expect(text).toContain('In photo');
    expect(text).not.toContain('All staff');
    expect(text).not.toContain('Punch for');
    expect(text).not.toContain('Close');
    expect(text).not.toContain('Apply');
    // Staff (non-owner) can file OT; manager-only filled action "Punch for" stays hidden.
    const filled = Array.from(
      fixture.nativeElement.querySelectorAll('button[matButton="filled"]'),
    ) as HTMLButtonElement[];
    expect(filled.map((b) => b.textContent?.trim())).toEqual(['Request OT']);
    expect(http.match('/api/staff')).toEqual([]);
  });

  it('downloads CSV from overflow menu with the same date range', async () => {
    await TestBed.configureTestingModule({
      imports: [AttendancePage],
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
    const fixture = TestBed.createComponent(AttendancePage);
    fixture.detectChanges();
    http.expectOne('/api/staff').flush([]);
    http.expectOne('/api/ops/attendance/register').flush(register);
    flushOvertime(http, '2026-09-13', '2026-09-19');
    fixture.detectChanges();

    const menuBtn = fixture.nativeElement.querySelector(
      'button[name="attendanceMoreMenu"]',
    ) as HTMLButtonElement;
    expect(menuBtn).toBeTruthy();
    menuBtn.click();
    fixture.detectChanges();
    await fixture.whenStable();

    const csvBtn = Array.from(document.querySelectorAll('button')).find((btn) =>
      btn.textContent?.includes('Download CSV'),
    );
    expect(csvBtn).toBeTruthy();
    csvBtn!.click();

    const req = http.expectOne('/api/ops/attendance/register.csv?from=2026-09-13&to=2026-09-19');
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['Name,Branch,In,Out,Hours\n']));
  });

  it('period nav changes reload the register', async () => {
    await TestBed.configureTestingModule({
      imports: [AttendancePage],
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
    const fixture = TestBed.createComponent(AttendancePage);
    fixture.detectChanges();
    http.expectOne('/api/staff').flush([]);
    http.expectOne('/api/ops/attendance/register').flush(register);
    flushOvertime(http, '2026-09-13', '2026-09-19');
    fixture.detectChanges();

    // Trigger period change
    fixture.componentInstance.onPeriodChange({ from: '2026-10-01', to: '2026-10-07' });
    fixture.detectChanges();

    const reloadReq = http.expectOne(
      (r) =>
        r.url.includes('/api/ops/attendance/register') &&
        r.urlWithParams.includes('from=2026-10-01'),
    );
    expect(reloadReq.request.params.get('from')).toBe('2026-10-01');
    expect(reloadReq.request.params.get('to')).toBe('2026-10-07');
    reloadReq.flush({
      from: '2026-10-01',
      to: '2026-10-07',
      rows: [],
    });
    flushOvertime(http, '2026-10-01', '2026-10-07');
  });

  it('manilaWeekRange is Sunday through Saturday', () => {
    expect(manilaWeekRange('2026-09-17')).toEqual({ from: '2026-09-13', to: '2026-09-19' });
  });

  it('paginates punches across multiple pages', async () => {
    await TestBed.configureTestingModule({
      imports: [AttendancePage],
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
    const fixture = TestBed.createComponent(AttendancePage);
    fixture.detectChanges();

    http.expectOne('/api/staff').flush([]);
    const manyRows = Array.from({ length: 15 }, (_, i) => ({
      ...closedRow,
      staffId: `staff-${i + 1}`,
      staffName: `Worker ${String(i + 1).padStart(2, '0')}`,
    }));
    http.expectOne('/api/ops/attendance/register').flush({
      from: '2026-09-13',
      to: '2026-09-19',
      rows: manyRows,
    });
    flushOvertime(http, '2026-09-13', '2026-09-19');
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Worker 01');
    expect(fixture.nativeElement.textContent).toContain('Worker 10');
    expect(fixture.nativeElement.textContent).not.toContain('Worker 11');

    fixture.componentInstance.onPage({ pageIndex: 1, pageSize: 10, length: 15 });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('Worker 01');
    expect(fixture.nativeElement.textContent).toContain('Worker 11');
    expect(fixture.nativeElement.textContent).toContain('Worker 15');
  });

  it('admin loads shop-wide overtime and own overtime with mine=true', async () => {
    await TestBed.configureTestingModule({
      imports: [AttendancePage],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: authStub(adminMe) },
      ],
    }).compileComponents();

    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(AttendancePage);
    fixture.detectChanges();

    http.expectOne('/api/staff').flush([]);
    http.expectOne('/api/ops/attendance/register').flush(register);

    const shopOtReq = http.expectOne(
      (r) =>
        r.url === '/api/ops/attendance/overtime' &&
        r.params.get('from') === '2026-09-13' &&
        r.params.get('to') === '2026-09-19' &&
        !r.params.has('mine'),
    );
    expect(shopOtReq.request.method).toBe('GET');
    shopOtReq.flush(emptyOvertime('2026-09-13', '2026-09-19'));

    const mineOtReq = http.expectOne(
      (r) =>
        r.url === '/api/ops/attendance/overtime' &&
        r.params.get('from') === '2026-09-13' &&
        r.params.get('to') === '2026-09-19' &&
        r.params.get('mine') === 'true',
    );
    expect(mineOtReq.request.method).toBe('GET');
    mineOtReq.flush(emptyOvertime('2026-09-13', '2026-09-19'));
  });
});
