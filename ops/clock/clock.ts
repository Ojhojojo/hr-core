import { Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { MatButton } from '@angular/material/button';
import { MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatProgressBar } from '@angular/material/progress-bar';
import {
  AuthService,
  ShopApiService,
  apiErrorMessage,
  formatManilaClock,
  formatManilaPunchTime,
  manilaToday,
  PhotoProofInput,
  PageHeaderComponent,
  CardComponent,
  EmptyStateComponent,
  BannerComponent,
  OpsConfirmService,
  OpsToastService,
  SkeletonComponent,
  StatusBadgeComponent,
} from '@hr-host';
import { ClockApi } from './clock.api';
import { AttendanceMe, AttendancePunch, KioskStaff } from './clock.models';
import {
  DEFAULT_CLOCK_RADIUS_METERS,
  OFFSITE_REASON_REQUIRED,
  OFFSITE_WARNING,
  GpsReading,
  OffsitePunchInput,
  ShopPin,
  canSubmitPunch,
  needsOffsiteReason,
} from './offsite-punch';
import { punchStatusLabel, punchStatusTone } from '../attendance/punch-review';
import { formatDay } from '../hr-dates';

const GPS_IN = 'Turn on location to clock in from your phone.';
const GPS_OUT = 'Turn on location to clock out from your phone.';

export type ClockMode = 'self' | 'kiosk';

export function clockModeFrom(data: Record<string, unknown> | undefined): ClockMode {
  return data?.['mode'] === 'kiosk' ? 'kiosk' : 'self';
}

/** Only people set to must clock can clock in themselves. Unknown (not yet loaded or older API) allows it. */
export function canSelfClockIn(me: Pick<AttendanceMe, 'mustClock'> | null): boolean {
  return me?.mustClock !== false;
}

@Component({
  selector: 'app-ops-clock',
  imports: [
    MatButton,
    MatFormField,
    MatHint,
    MatLabel,
    MatInput,
    MatProgressBar,
    PhotoProofInput,
    PageHeaderComponent,
    CardComponent,
    EmptyStateComponent,
    BannerComponent,
    SkeletonComponent,
    StatusBadgeComponent,
  ],
  templateUrl: './clock.html',
})
export class ClockPage {
  private readonly api = inject(ClockApi);
  private readonly auth = inject(AuthService);
  private readonly confirm = inject(OpsConfirmService);
  private readonly toast = inject(OpsToastService);
  private readonly shopApi = inject(ShopApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  /** Route data, not the signed-in role. /me/clock is self; /hr/clock-kiosk is kiosk. */
  protected readonly clockMode: ClockMode = clockModeFrom(this.route.snapshot.data);
  protected readonly isSelfClock = this.clockMode === 'self';
  protected readonly isKiosk = this.clockMode === 'kiosk';

  protected readonly now = signal(new Date());
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly busyMessage = signal('');
  protected readonly kioskBusy = signal(false);
  protected readonly error = signal('');
  protected readonly me = signal<AttendanceMe | null>(null);
  protected readonly kiosk = signal<KioskStaff[]>([]);

  protected readonly clockedIn = computed(() => this.me()?.clockedIn === true);
  protected readonly canClockIn = computed(() => canSelfClockIn(this.me()));
  protected readonly status = computed(() => this.me()?.status ?? 'Checking…');
  protected readonly today = computed(() => this.me()?.today ?? []);
  protected readonly clockLabel = computed(() => formatManilaClock(this.now()));
  protected readonly requirePhoto = computed(() => this.me()?.requireClockPhoto === true);
  protected readonly photoFile = signal<File | null>(null);
  protected readonly offsiteReason = signal('');
  protected readonly gps = signal<GpsReading>({ status: 'unknown' });
  protected readonly shopPin = signal<ShopPin | null>(null);
  protected readonly knownOffsite = signal(false);
  protected readonly allowOffsite = computed(
    () => this.me()?.allowOffsitePunchWithApproval !== false,
  );
  protected readonly offsiteInput = computed<OffsitePunchInput>(() => ({
    allowOffsitePunchWithApproval: this.allowOffsite(),
    gps: this.gps(),
    shop: this.shopPin(),
    offsiteReason: this.offsiteReason(),
    knownOffsite: this.knownOffsite(),
  }));
  protected readonly needsReason = computed(() => needsOffsiteReason(this.offsiteInput()));
  protected readonly canSubmitNow = computed(() => canSubmitPunch(this.offsiteInput()));
  protected readonly offsiteWarning = OFFSITE_WARNING;
  protected readonly offsiteReasonRequired = OFFSITE_REASON_REQUIRED;
  protected readonly punchStatusLabel = punchStatusLabel;
  protected readonly punchStatusTone = punchStatusTone;
  protected readonly pageSubtitle = computed(() =>
    this.isSelfClock ? `${this.clockLabel()} · Manila time` : undefined,
  );
  protected readonly pageTooltip = computed(() =>
    this.isSelfClock
      ? 'Phone clock-in uses the shop pin and radius from Settings.'
      : 'Tap a name to clock in or out; desk taps skip GPS.',
  );

  constructor() {
    this.refresh();
    effect(() => {
      if (this.isKiosk) {
        this.loadKiosk();
      } else {
        this.kiosk.set([]);
      }
    });
    const id = setInterval(() => this.now.set(new Date()), 1000);
    this.destroyRef.onDestroy(() => clearInterval(id));
  }

  protected punchTime(iso: string | null | undefined): string {
    return formatManilaPunchTime(iso);
  }

  refresh(): void {
    this.loadMe(true);
  }

  clockIn(): void {
    void this.selfPunch('in');
  }

  clockOut(): void {
    void this.selfPunch('out');
  }

  protected setOffsiteReason(event: Event): void {
    this.offsiteReason.set((event.target as HTMLInputElement).value);
  }

  kioskTap(person: KioskStaff): void {
    if (this.kioskBusy() || !this.isKiosk) {
      return;
    }

    if (
      !this.ensurePhoto(
        person.clockedIn ? 'A photo is required to clock out.' : 'A photo is required to clock in.',
      )
    ) {
      return;
    }

    const clockingOut = person.clockedIn;
    const side = clockingOut ? 'out' : 'in';
    this.confirm
      .confirm({
        title: `Clock ${side} ${person.displayName}?`,
        message: `${person.displayName} will be clocked ${side} for today (${formatDay(manilaToday())}).`,
        confirmLabel: clockingOut ? 'Clock out' : 'Clock in',
      })
      .subscribe((result) => {
        if (!result.confirmed || this.kioskBusy() || !this.isKiosk) {
          return;
        }
        this.sendKioskPunch(person);
      });
  }

  private sendKioskPunch(person: KioskStaff): void {
    this.kioskBusy.set(true);
    this.error.set('');
    const photo = this.photoFile();
    const call = person.clockedIn
      ? this.api.kioskClockOut(person.id, photo)
      : this.api.kioskClockIn(person.id, photo);
    call.subscribe({
      next: () => {
        this.toast.success(person.clockedIn ? 'Clocked out.' : 'Clocked in.');
        this.photoFile.set(null);
        this.kioskBusy.set(false);
        this.loadKiosk();
        this.loadMe(false);
      },
      error: (err: unknown) => {
        this.error.set(apiErrorMessage(err));
        this.kioskBusy.set(false);
        this.loadKiosk();
      },
    });
  }

  private loadMe(showLoading: boolean): void {
    if (showLoading) {
      this.loading.set(true);
      this.error.set('');
    }

    this.api.me().subscribe({
      next: (me) => {
        this.me.set(me);
        if (showLoading) {
          this.loading.set(false);
        }
      },
      error: (err: unknown) => {
        this.error.set(apiErrorMessage(err));
        if (showLoading) {
          this.loading.set(false);
        }
      },
    });
  }

  private showPunch(punch: AttendancePunch): void {
    this.me.update((current) => {
      const base = current ?? {
        clockedIn: false,
        status: "You're clocked out",
        open: null,
        today: [],
      };
      const today = [punch, ...base.today.filter((row) => row.id !== punch.id)];
      if (!punch.outAtUtc) {
        return {
          ...base,
          clockedIn: true,
          status: `You're clocked in since ${punch.inAtManila}`,
          open: punch,
          today,
        };
      }

      const open = base.open?.id === punch.id ? null : base.open;
      return {
        ...base,
        clockedIn: open != null,
        status: open ? base.status : "You're clocked out",
        open,
        today,
      };
    });
  }

  private loadKiosk(): void {
    if (!this.isKiosk) {
      this.kiosk.set([]);
      return;
    }

    this.api.kiosk().subscribe({
      next: (rows) => this.kiosk.set(rows),
      error: (err: unknown) => {
        this.kiosk.set([]);
        this.error.set(apiErrorMessage(err));
      },
    });
  }

  private ensurePhoto(message: string): boolean {
    if (this.requirePhoto() && !this.photoFile()) {
      this.error.set(message);
      return false;
    }

    return true;
  }

  private readGps(): Promise<{ latitude: number; longitude: number } | null> {
    const geo = typeof navigator === 'undefined' ? undefined : navigator.geolocation;
    if (!geo) {
      return Promise.resolve(null);
    }

    return new Promise((resolve) => {
      geo.getCurrentPosition(
        (pos) =>
          resolve({
            latitude: pos.coords.latitude,
            longitude: pos.coords.longitude,
          }),
        () => resolve(null),
        { enableHighAccuracy: true, timeout: 10000, maximumAge: 15000 },
      );
    });
  }

  private async selfPunch(side: 'in' | 'out'): Promise<void> {
    if (this.busy() || (side === 'in' && !this.canClockIn())) {
      return;
    }

    if (
      !this.ensurePhoto(
        side === 'in' ? 'A photo is required to clock in.' : 'A photo is required to clock out.',
      )
    ) {
      return;
    }

    this.busy.set(true);
    this.error.set('');
    this.busyMessage.set('Getting your location…');
    const photo = this.photoFile();
    const coords = await this.readGps();
    this.gps.set(
      coords
        ? { status: 'ok', latitude: coords.latitude, longitude: coords.longitude }
        : { status: 'unavailable' },
    );
    if (coords) {
      await this.ensureShopPin();
    }

    const args = this.offsiteInput();
    if (!canSubmitPunch(args)) {
      if (!this.allowOffsite() && !coords) {
        this.error.set(side === 'in' ? GPS_IN : GPS_OUT);
      } else if (!this.allowOffsite()) {
        this.error.set(OFFSITE_WARNING);
      }
      this.busy.set(false);
      this.busyMessage.set('');
      return;
    }

    if (photo) {
      this.busyMessage.set('Uploading…');
    } else {
      this.busyMessage.set(side === 'in' ? 'Clocking in…' : 'Clocking out…');
    }

    const reason = needsOffsiteReason(args) ? this.offsiteReason().trim() : '';
    this.punch(
      () =>
        side === 'in'
          ? this.api.clockIn(coords, photo, reason || null)
          : this.api.clockOut(coords, photo, reason || null),
      true,
    );
  }

  private async ensureShopPin(): Promise<void> {
    if (this.shopPin()?.latitude != null && this.shopPin()?.longitude != null) {
      return;
    }

    try {
      const shop = await firstValueFrom(this.shopApi.getShop(this.auth.me()?.branchCode));
      this.shopPin.set({
        latitude: shop.latitude ?? null,
        longitude: shop.longitude ?? null,
        clockRadiusMeters: DEFAULT_CLOCK_RADIUS_METERS,
      });
    } catch {
      this.shopPin.set(this.shopPin());
    }
  }

  private punch(send: () => ReturnType<ClockApi['clockIn']>, alreadyBusy = false): void {
    if (this.busy() && !alreadyBusy) {
      return;
    }

    this.busy.set(true);
    this.error.set('');
    send().subscribe({
      next: (punch) => {
        this.toast.success(punch.outAtUtc ? 'Clocked out.' : 'Clocked in.');
        this.photoFile.set(null);
        this.offsiteReason.set('');
        this.gps.set({ status: 'unknown' });
        this.knownOffsite.set(false);
        this.showPunch(punch);
        this.api.me().subscribe({
          next: (me) => {
            this.me.set(keepPunch(me, punch));
            this.busy.set(false);
            this.busyMessage.set('');
            if (this.isKiosk) {
              this.loadKiosk();
            }
          },
          error: (err: unknown) => {
            this.error.set(apiErrorMessage(err));
            this.busy.set(false);
            this.busyMessage.set('');
          },
        });
      },
      error: (err: unknown) => {
        const message = apiErrorMessage(err);
        if (message === OFFSITE_REASON_REQUIRED) {
          this.knownOffsite.set(true);
          this.error.set('');
        } else {
          this.error.set(message);
        }
        this.busy.set(false);
        this.busyMessage.set('');
        this.refresh();
      },
    });
  }
}

/** Keep a punch the clock just saved when a later /me list leaves it out. */
export function keepPunch(me: AttendanceMe, punch: AttendancePunch): AttendanceMe {
  if (me.today.some((row) => row.id === punch.id)) {
    return me;
  }

  return { ...me, today: [punch, ...me.today] };
}
