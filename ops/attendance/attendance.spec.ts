import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
  type TestRequest,
} from '@angular/common/http/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { afterEach, describe, expect, it } from 'vitest';
import { AuthService } from '@hr-host';
import { MEMBER_ROLE, specUser, type SpecUser } from '../host-spec-fixtures';
import { AttendancePage, emptyPunchesMessage, manilaWeekRange } from './attendance';
import { AttendanceRegisterPage, OvertimeListResponse } from './attendance.models';

const ownerMe: SpecUser = specUser('owner', {
  authUserId: 'owner',
  email: 'owner@rakkiicars.ph',
  branchId: null,
  branchCode: null,
  displayName: 'Apple',
});

const memberMe: SpecUser = specUser(MEMBER_ROLE, {
  authUserId: 'tech',
  email: 'owner@rakkiicars.ph',
  branchId: 'branch-1',
  branchCode: 'main-roosevelt',
  displayName: "Danilo 'Danny' Santos",
});

const adminMe: SpecUser = specUser('admin', {
  authUserId: 'admin',
  email: 'owner@rakkiicars.ph',
  branchId: 'branch-1',
  branchCode: 'main-roosevelt',
  displayName: 'Admin Staff',
});

function authStub(me: SpecUser) {
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

/** One page of the register: the default page is 1 of 25, and the request sends `page` and `pageSize`. */
const register: AttendanceRegisterPage = {
  from: '2026-09-13',
  to: '2026-09-19',
  items: [openRow, closedRow],
  total: 2,
  pageNumber: 1,
  pageSize: 25,
  pendingTotal: 0,
};

const emptyOvertime = (from: string, to: string): OvertimeListResponse => ({
  from,
  to,
  rows: [],
});

/** The paged register request. Matches on `page`, so the CSV download is not picked up. */
function expectRegisterPage(http: HttpTestingController): TestRequest {
  return http.expectOne((r) => r.url === '/api/ops/attendance/register' && r.params.has('page'));
}

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
        { provide: AuthService, useValue: authStub(memberMe) },
      ],
    }).compileComponents();

    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(AttendancePage);
    fixture.detectChanges();

    expectRegisterPage(http).flush({
      from: '2026-09-13',
      to: '2026-09-19',
      items: [{ ...openRow, punchId: 'punch-1', hasInPhoto: true }],
      total: 1,
      pageNumber: 1,
      pageSize: 25,
      pendingTotal: 0,
    } satisfies AttendanceRegisterPage);
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
    expectRegisterPage(http).flush(register);
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
    expectRegisterPage(http).flush(register);
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
      items: [],
      total: 0,
      pageNumber: 1,
      pageSize: 25,
      pendingTotal: 0,
    });
    flushOvertime(http, '2026-10-01', '2026-10-07');
  });

  it('manilaWeekRange is Monday through Sunday, like the API', () => {
    expect(manilaWeekRange('2026-09-17')).toEqual({ from: '2026-09-14', to: '2026-09-20' });
    expect(manilaWeekRange('2026-09-20')).toEqual({ from: '2026-09-14', to: '2026-09-20' });
    expect(manilaWeekRange('2026-09-14')).toEqual({ from: '2026-09-14', to: '2026-09-20' });
  });

  it('emptyPunchesMessage says "Try another date range." only when the range is entirely in the past', () => {
    const yet = 'No punches yet. They show here once someone clocks in.';
    expect(emptyPunchesMessage('2026-09-19', '2026-09-20')).toBe('Try another date range.');
    expect(emptyPunchesMessage('2026-09-20', '2026-09-20')).toBe(yet);
    expect(emptyPunchesMessage('2026-09-27', '2026-09-20')).toBe(yet);
    expect(emptyPunchesMessage('', '2026-09-20')).toBe(yet);
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
    const workers = (from: number, to: number) =>
      Array.from({ length: to - from + 1 }, (_, i) => ({
        ...closedRow,
        staffId: `staff-${from + i}`,
        staffName: `Worker ${String(from + i).padStart(2, '0')}`,
      }));
    expectRegisterPage(http).flush({
      from: '2026-09-13',
      to: '2026-09-19',
      items: workers(1, 10),
      total: 15,
      pageNumber: 1,
      pageSize: 25,
      pendingTotal: 0,
    } satisfies AttendanceRegisterPage);
    flushOvertime(http, '2026-09-13', '2026-09-19');
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Worker 01');
    expect(fixture.nativeElement.textContent).toContain('Worker 10');

    // The next page is a new request for page 2, not a client-side slice.
    fixture.componentInstance.onPage({ pageIndex: 1, pageSize: 10, length: 15 });
    const next = http.expectOne(
      (r) =>
        r.url === '/api/ops/attendance/register' &&
        r.params.get('page') === '2' &&
        r.params.get('pageSize') === '10',
    );
    next.flush({
      from: '2026-09-13',
      to: '2026-09-19',
      items: workers(11, 15),
      total: 15,
      pageNumber: 2,
      pageSize: 10,
      pendingTotal: 0,
    } satisfies AttendanceRegisterPage);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('Worker 01');
    expect(fixture.nativeElement.textContent).toContain('Worker 11');
    expect(fixture.nativeElement.textContent).toContain('Worker 15');
  });

  it('asks for page 1 again when a filter changes', async () => {
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
    expectRegisterPage(http).flush(register);
    flushOvertime(http, '2026-09-13', '2026-09-19');
    fixture.detectChanges();

    fixture.componentInstance.onPage({ pageIndex: 2, pageSize: 25, length: 80 });
    const page3 = expectRegisterPage(http);
    expect(page3.request.params.get('page')).toBe('3');
    page3.flush({
      ...register,
      items: [],
      total: 80,
      pageNumber: 3,
      pageSize: 25,
      pendingTotal: 0,
    });
    fixture.detectChanges();

    fixture.componentInstance.setStaff('staff-2');
    const reset = expectRegisterPage(http);
    expect(reset.request.params.get('page')).toBe('1');
    expect(reset.request.params.get('staffId')).toBe('staff-2');
    reset.flush({ ...register, total: 2, pendingTotal: 0 });
    flushOvertime(http, '2026-09-13', '2026-09-19');
    fixture.detectChanges();
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
    expectRegisterPage(http).flush(register);

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
