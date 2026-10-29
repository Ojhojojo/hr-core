import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import {
  AuthMe,
  AuthService,
  ShopApiService,
  manilaToday,
  ConfirmDialogData,
  OpsConfirmService,
  OpsToastService,
} from '@hr-host';
import { ClockPage, clockModeFrom } from './clock';
import { AttendanceMe } from './clock.models';
import { formatDay } from '../hr-dates';

const techMe: AuthMe = {
  authUserId: 'tech',
  email: 'tech@rakkiicars.ph',
  profileKind: 'staff',
  staffRole: 'technician',
  branchId: 'branch-1',
  branchCode: 'main-roosevelt',
  isStaff: true,
  displayName: "Danilo 'Danny' Santos",
};

const adminMe: AuthMe = {
  ...techMe,
  authUserId: 'admin',
  staffRole: 'admin',
  displayName: 'Clarissa Reyes',
};

const ownerMe: AuthMe = {
  ...techMe,
  authUserId: 'owner',
  staffRole: 'owner',
  displayName: 'Shop Owner',
};

function authStub(me: AuthMe) {
  return { me: signal(me) };
}

function clockRoute(mode: 'self' | 'kiosk') {
  return { provide: ActivatedRoute, useValue: { snapshot: { data: { mode } } } };
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

const clockedOut: AttendanceMe = {
  clockedIn: false,
  status: "You're clocked out",
  open: null,
  today: [],
};

describe('ClockPage', () => {
  it('does not punch when the shop requires a photo and none is attached', async () => {
    await TestBed.configureTestingModule({
      imports: [ClockPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        clockRoute('self'),
        { provide: AuthService, useValue: authStub(techMe) },
        { provide: OpsConfirmService, useValue: confirmStub().service },
        { provide: OpsToastService, useValue: { success: vi.fn(), error: vi.fn() } },
      ],
    }).compileComponents();

    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ClockPage);
    fixture.detectChanges();

    http.expectOne('/api/ops/attendance/me').flush({
      ...clockedOut,
      requireClockPhoto: true,
    });
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('This shop requires a photo on clock-in and clock-out.');
    expect(text).toContain('Take photo');

    const button = Array.from(
      fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>,
    ).find((btn) => btn.textContent?.includes('Clock in'));
    button?.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('A photo is required to clock in.');
    http.expectNone('/api/ops/attendance/clock-in');
    http.verify();
  });

  it('technician does not list other staff', async () => {
    await TestBed.configureTestingModule({
      imports: [ClockPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        clockRoute('self'),
        { provide: AuthService, useValue: authStub(techMe) },
        { provide: OpsConfirmService, useValue: confirmStub().service },
        { provide: OpsToastService, useValue: { success: vi.fn(), error: vi.fn() } },
      ],
    }).compileComponents();

    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ClockPage);
    fixture.detectChanges();
    http.expectOne('/api/ops/attendance/me').flush(clockedOut);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Clock in');
    expect(text).not.toContain('Desk kiosk');
    expect(text).not.toContain('Clarissa');
    expect(http.match('/api/ops/attendance/kiosk')).toEqual([]);
    http.verify();
  });

  it('kiosk route shows the desk kiosk and hides the personal clock, including for the owner', async () => {
    await TestBed.configureTestingModule({
      imports: [ClockPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        clockRoute('kiosk'),
        { provide: AuthService, useValue: authStub(ownerMe) },
        { provide: OpsConfirmService, useValue: confirmStub().service },
        { provide: OpsToastService, useValue: { success: vi.fn(), error: vi.fn() } },
      ],
    }).compileComponents();

    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ClockPage);
    fixture.detectChanges();
    http.expectOne('/api/ops/attendance/me').flush(clockedOut);
    http.expectOne('/api/ops/attendance/kiosk').flush([]);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Desk kiosk');
    expect(text).toContain('desk taps skip GPS.');
    expect(text).not.toContain('Clock in');
    expect(text).not.toContain('Punches show here after you clock in.');
    http.expectNone('/api/ops/attendance/clock-in');
    http.verify();
  });

  it('admin sees must-clock staff on the desk kiosk', async () => {
    const confirm = confirmStub();
    await TestBed.configureTestingModule({
      imports: [ClockPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        clockRoute('kiosk'),
        { provide: AuthService, useValue: authStub(adminMe) },
        { provide: OpsConfirmService, useValue: confirm.service },
        { provide: OpsToastService, useValue: { success: vi.fn(), error: vi.fn() } },
      ],
    }).compileComponents();

    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ClockPage);
    fixture.detectChanges();
    http.expectOne('/api/ops/attendance/me').flush(clockedOut);
    const kiosk = http.expectOne('/api/ops/attendance/kiosk');
    kiosk.flush([
      {
        id: 'tech-1',
        displayName: "Danilo 'Danny' Santos",
        mustClock: true,
        clockedIn: true,
      },
      {
        id: 'front-1',
        displayName: 'Front Desk Ana',
        mustClock: true,
        clockedIn: false,
      },
    ]);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Desk kiosk');
    expect(text).toContain("Danilo 'Danny' Santos");
    expect(text).toContain('Front Desk Ana');
    expect(text).toContain('Clocked in — tap to clock out');
    expect(text).toContain('Clocked out — tap to clock in');

    const button = Array.from(
      fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>,
    ).find((btn) => btn.textContent?.includes('Front Desk Ana'));
    button?.click();
    expect(confirm.calls).toEqual([
      {
        title: 'Clock in Front Desk Ana?',
        message: `Front Desk Ana will be clocked in for today (${formatDay(manilaToday())}).`,
        confirmLabel: 'Clock in',
      },
    ]);
    const tap = http.expectOne('/api/ops/attendance/kiosk/front-1/clock-in');
    expect(tap.request.body).toEqual({});
    tap.flush({ id: 'punch-kiosk' });
    http.expectOne('/api/ops/attendance/kiosk').flush([]);
    http.expectOne('/api/ops/attendance/me').flush(clockedOut);
    http.verify();
  });

  it('keeps a clock-out on today when the follow-up list omits it', async () => {
    const original = Object.getOwnPropertyDescriptor(navigator, 'geolocation');
    Object.defineProperty(navigator, 'geolocation', {
      configurable: true,
      value: {
        getCurrentPosition: (
          ok: (pos: { coords: { latitude: number; longitude: number } }) => void,
        ) => ok({ coords: { latitude: 14.6406, longitude: 121.0051 } }),
      },
    });

    try {
      await TestBed.configureTestingModule({
        imports: [ClockPage],
        providers: [
          provideZonelessChangeDetection(),
          provideNoopAnimations(),
          provideHttpClient(),
          provideHttpClientTesting(),
          clockRoute('self'),
          { provide: AuthService, useValue: authStub(techMe) },
          { provide: OpsConfirmService, useValue: confirmStub().service },
          { provide: OpsToastService, useValue: { success: vi.fn(), error: vi.fn() } },
          {
            provide: ShopApiService,
            useValue: {
              getShop: () => of({ latitude: 14.6406, longitude: 121.0051, clockRadiusMeters: 150 }),
            },
          },
        ],
      }).compileComponents();

      const http = TestBed.inject(HttpTestingController);
      const fixture = TestBed.createComponent(ClockPage);
      fixture.detectChanges();
      http.expectOne('/api/ops/attendance/me').flush({
        ...clockedOut,
        clockedIn: true,
        status: "You're clocked in since 8:02 AM",
        open: {
          id: 'punch-1',
          staffId: 'tech',
          branchId: 'branch-1',
          branchName: 'Roosevelt',
          inAtUtc: '2026-10-15T00:02:00Z',
          outAtUtc: null,
          inAtManila: '8:02 AM',
          outAtManila: null,
          source: 'ops',
        },
      });
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).toContain('Punches show here after you clock in.');
      expect(fixture.nativeElement.textContent).toContain('Clock out');

      const page = fixture.componentInstance as unknown as { clockOut(): void };
      page.clockOut();
      await Promise.resolve();
      await Promise.resolve();
      await fixture.whenStable();

      const clockOut = http.expectOne('/api/ops/attendance/clock-out');
      clockOut.flush({
        id: 'punch-1',
        staffId: 'tech',
        branchId: 'branch-1',
        branchName: 'Roosevelt',
        inAtUtc: '2026-10-15T00:02:00Z',
        outAtUtc: '2026-10-15T00:08:00Z',
        inAtManila: '8:02 AM',
        outAtManila: '8:08 AM',
        source: 'ops',
      });
      await fixture.whenStable();
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).not.toContain(
        'Punches show here after you clock in.',
      );
      expect(fixture.nativeElement.textContent).toContain('Roosevelt');

      http.expectOne('/api/ops/attendance/me').flush({
        ...clockedOut,
        status: "You're clocked out",
      });
      await fixture.whenStable();
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).not.toContain(
        'Punches show here after you clock in.',
      );
      expect(fixture.nativeElement.textContent).toContain('Roosevelt');
      http.verify();
    } finally {
      if (original) {
        Object.defineProperty(navigator, 'geolocation', original);
      } else {
        Reflect.deleteProperty(navigator, 'geolocation');
      }
    }
  });

  it('does not clock a kiosk name when the confirmation is cancelled', async () => {
    const confirm = confirmStub(false);
    await TestBed.configureTestingModule({
      imports: [ClockPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        clockRoute('kiosk'),
        { provide: AuthService, useValue: authStub(adminMe) },
        { provide: OpsConfirmService, useValue: confirm.service },
        { provide: OpsToastService, useValue: { success: vi.fn(), error: vi.fn() } },
      ],
    }).compileComponents();

    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ClockPage);
    fixture.detectChanges();
    http.expectOne('/api/ops/attendance/me').flush(clockedOut);
    http.expectOne('/api/ops/attendance/kiosk').flush([
      {
        id: 'tech-1',
        displayName: "Danilo 'Danny' Santos",
        mustClock: true,
        clockedIn: true,
      },
    ]);
    fixture.detectChanges();

    const button = Array.from(
      fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>,
    ).find((btn) => btn.textContent?.includes("Danilo 'Danny' Santos"));
    button?.click();
    fixture.detectChanges();

    expect(confirm.calls[0]?.title).toBe("Clock out Danilo 'Danny' Santos?");
    expect(confirm.calls[0]?.confirmLabel).toBe('Clock out');
    http.expectNone('/api/ops/attendance/kiosk/tech-1/clock-out');
    http.expectNone('/api/ops/attendance/kiosk/tech-1/clock-in');
    http.verify();
  });

  it('does not call clock-in when location is off', async () => {
    const original = Object.getOwnPropertyDescriptor(navigator, 'geolocation');
    Object.defineProperty(navigator, 'geolocation', {
      configurable: true,
      value: {
        getCurrentPosition: (_ok: unknown, err?: (reason: unknown) => void) => err?.({ code: 1 }),
      },
    });

    try {
      await TestBed.configureTestingModule({
        imports: [ClockPage],
        providers: [
          provideZonelessChangeDetection(),
          provideNoopAnimations(),
          provideHttpClient(),
          provideHttpClientTesting(),
          clockRoute('self'),
          { provide: AuthService, useValue: authStub(techMe) },
          { provide: OpsConfirmService, useValue: confirmStub().service },
          { provide: OpsToastService, useValue: { success: vi.fn(), error: vi.fn() } },
        ],
      }).compileComponents();

      const http = TestBed.inject(HttpTestingController);
      const fixture = TestBed.createComponent(ClockPage);
      fixture.detectChanges();
      http.expectOne('/api/ops/attendance/me').flush(clockedOut);
      fixture.detectChanges();

      const button = Array.from(
        fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>,
      ).find((btn) => btn.textContent?.includes('Clock in'));
      button?.click();
      await Promise.resolve();
      await Promise.resolve();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain("You're not at the shop");
      expect(fixture.nativeElement.textContent).toContain(
        'A reason is required for an off-site punch.',
      );
      http.expectNone('/api/ops/attendance/clock-in');
      http.verify();
    } finally {
      if (original) {
        Object.defineProperty(navigator, 'geolocation', original);
      } else {
        Reflect.deleteProperty(navigator, 'geolocation');
      }
    }
  });

  it('reads clock mode from route data', () => {
    expect(clockModeFrom({ mode: 'self' })).toBe('self');
    expect(clockModeFrom({ mode: 'kiosk' })).toBe('kiosk');
    expect(clockModeFrom(undefined)).toBe('self');
    expect(clockModeFrom({})).toBe('self');
  });

  it.each([
    ['owner', ownerMe],
    ['admin', adminMe],
    ['technician', techMe],
  ] as const)(
    '%s on the self route sees the personal clock and not the kiosk',
    async (_role, me) => {
      await TestBed.configureTestingModule({
        imports: [ClockPage],
        providers: [
          provideZonelessChangeDetection(),
          provideHttpClient(),
          provideHttpClientTesting(),
          clockRoute('self'),
          { provide: AuthService, useValue: authStub(me) },
          { provide: OpsConfirmService, useValue: confirmStub().service },
          { provide: OpsToastService, useValue: { success: vi.fn(), error: vi.fn() } },
        ],
      }).compileComponents();

      const http = TestBed.inject(HttpTestingController);
      const fixture = TestBed.createComponent(ClockPage);
      fixture.detectChanges();
      http.expectOne('/api/ops/attendance/me').flush(clockedOut);
      fixture.detectChanges();

      const text = fixture.nativeElement.textContent as string;
      expect(text).toContain('Phone clock-in uses the shop pin');
      expect(text).toContain('Clock in');
      expect(text).not.toContain('Desk kiosk');
      expect(http.match('/api/ops/attendance/kiosk')).toEqual([]);
      http.verify();
    },
  );

  it('shows "Getting your location…" during GPS wait and "Uploading…" when a photo is attached', async () => {
    const original = Object.getOwnPropertyDescriptor(navigator, 'geolocation');
    let resolveGps!: (pos: GeolocationPosition) => void;
    const fakeGeo = {
      getCurrentPosition: vi.fn().mockImplementation((success) => {
        resolveGps = success;
      }),
    };
    Object.defineProperty(navigator, 'geolocation', {
      configurable: true,
      value: fakeGeo,
    });

    let http: HttpTestingController | undefined;

    try {
      await TestBed.configureTestingModule({
        imports: [ClockPage],
        providers: [
          provideZonelessChangeDetection(),
          provideHttpClient(),
          provideHttpClientTesting(),
          clockRoute('self'),
          { provide: AuthService, useValue: authStub(techMe) },
          { provide: OpsConfirmService, useValue: confirmStub().service },
          { provide: OpsToastService, useValue: { success: vi.fn(), error: vi.fn() } },
        ],
      }).compileComponents();

      http = TestBed.inject(HttpTestingController);
      const toast = TestBed.inject(OpsToastService);
      const toastSpy = vi.spyOn(toast, 'success');
      const fixture = TestBed.createComponent(ClockPage);
      fixture.detectChanges();
      http.expectOne('/api/ops/attendance/me').flush(clockedOut);
      fixture.detectChanges();

      const page = fixture.componentInstance;
      page['shopPin'].set({ latitude: 14.5, longitude: 121.0, clockRadiusMeters: 500 });
      const file = new File(['image-bytes'], 'punch.jpg', { type: 'image/jpeg' });
      page['photoFile'].set(file);

      const punchPromise = page['selfPunch']('in');
      expect(page['busy']()).toBe(true);
      expect(page['busyMessage']()).toBe('Getting your location…');

      resolveGps({
        coords: { latitude: 14.5, longitude: 121.0, accuracy: 10 },
        timestamp: Date.now(),
      } as unknown as GeolocationPosition);
      await punchPromise;

      expect(page['busyMessage']()).toBe('Uploading…');

      const punchReq = http.expectOne('/api/ops/attendance/clock-in');
      expect(punchReq.request.method).toBe('POST');
      punchReq.flush({
        id: 'p-1',
        staffId: 'tech',
        inAtUtc: '2026-09-21T00:00:00Z',
        inAtManila: '8:00 AM',
        outAtUtc: null,
        outAtManila: null,
        hoursWorked: null,
        source: 'web',
        locationKind: 'onsite',
      });

      const meReq = http.expectOne('/api/ops/attendance/me');
      meReq.flush({ ...clockedOut, clockedIn: true, status: "You're clocked in" });

      expect(page['busy']()).toBe(false);
      expect(page['busyMessage']()).toBe('');
      expect(toastSpy).toHaveBeenCalledWith('Clocked in.');
    } finally {
      if (original) {
        Object.defineProperty(navigator, 'geolocation', original);
      } else {
        Reflect.deleteProperty(navigator, 'geolocation');
      }
      http?.verify();
    }
  });
});
