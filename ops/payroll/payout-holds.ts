import { Component, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { apiErrorMessage, BannerComponent, OpsConfirmService, SkeletonComponent } from '@hr-host';
import { MatButton } from '@angular/material/button';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import { PayrollApi } from './payroll.api';
import { PayoutHold } from './payroll.models';

/** A person the hold form can pick. The payroll page passes the register rows. */
export interface PayoutHoldStaffOption {
  staffId: string;
  staffName: string;
}

/** Text from a problem-details body, then the host's error helper. */
export function payoutHoldProblem(err: unknown): string {
  const body = (err as { error?: unknown } | null)?.error;
  if (body && typeof body === 'object') {
    const errors = (body as { errors?: unknown }).errors;
    if (errors && typeof errors === 'object') {
      for (const value of Object.values(errors)) {
        if (Array.isArray(value) && value.length > 0) {
          return String(value[0]);
        }
      }
    }
    const detail = (body as { detail?: unknown }).detail;
    if (typeof detail === 'string' && detail.trim()) {
      return detail;
    }
    const message = (body as { error?: unknown }).error;
    if (typeof message === 'string' && message.trim()) {
      return message;
    }
  }
  return apiErrorMessage(err) || 'Could not update the payout hold.';
}

@Component({
  selector: 'app-ops-payout-holds',
  imports: [
    FormsModule,
    BannerComponent,
    SkeletonComponent,
    MatButton,
    MatFormField,
    MatLabel,
    MatInput,
    MatSelect,
    MatOption,
  ],
  templateUrl: './payout-holds.html',
  styles: `
    .payout-hold-form,
    .payout-hold-row {
      display: grid;
      grid-template-columns: minmax(0, 1.2fr) minmax(0, 1.4fr) auto;
      column-gap: var(--space-3);
      row-gap: var(--space-2);
      align-items: start;
      min-width: 0;
    }
    .payout-hold-rows {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      margin-top: var(--space-3);
    }
    .payout-hold-form mat-form-field {
      width: 100%;
      min-width: 0;
    }
    @media (max-width: 40rem) {
      .payout-hold-form,
      .payout-hold-row {
        grid-template-columns: minmax(0, 1fr);
      }
    }
  `,
})
export class PayoutHoldSettings {
  private readonly api = inject(PayrollApi);
  private readonly confirm = inject(OpsConfirmService);

  readonly staff = input<readonly PayoutHoldStaffOption[]>([]);
  readonly changed = output<void>();

  protected readonly loading = signal(false);
  protected readonly ready = signal(false);
  protected readonly loadError = signal('');
  protected readonly saveError = signal('');
  protected readonly notice = signal('');
  protected readonly saving = signal(false);
  protected readonly holds = signal<PayoutHold[]>([]);
  protected readonly staffId = signal('');
  protected readonly reason = signal('');

  load(): void {
    this.loading.set(true);
    this.loadError.set('');
    this.api.listPayoutHolds().subscribe({
      next: (body) => {
        this.holds.set(body.holds ?? []);
        this.ready.set(true);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.ready.set(false);
        this.loading.set(false);
        this.loadError.set(payoutHoldProblem(err) || 'Could not load payout holds.');
      },
    });
  }

  protected hold(): void {
    const staffId = this.staffId().trim();
    if (!staffId) {
      this.saveError.set('Pick a person.');
      return;
    }
    this.saving.set(true);
    this.saveError.set('');
    this.notice.set('');
    this.api.setPayoutHold({ staffId, reason: this.reason() }).subscribe({
      next: (saved) => {
        this.saving.set(false);
        this.reason.set('');
        this.notice.set(`${saved.staffName} is on hold.`);
        this.load();
        this.changed.emit();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.saveError.set(payoutHoldProblem(err));
      },
    });
  }

  protected release(hold: PayoutHold): void {
    this.confirm
      .confirm({
        title: 'Release payout',
        message: `Release ${hold.staffName}'s held payout? Clearance is approved and the pay can be paid.`,
        confirmLabel: 'Release',
      })
      .subscribe((result) => {
        if (!result.confirmed) {
          return;
        }
        this.saving.set(true);
        this.saveError.set('');
        this.notice.set('');
        this.api.releasePayoutHold(hold.staffId).subscribe({
          next: (saved) => {
            this.saving.set(false);
            const when = saved.releasedOn ?? '';
            const who = saved.releasedByName ? ` by ${saved.releasedByName}` : '';
            this.notice.set(`Released ${when}${who}.`);
            this.load();
            this.changed.emit();
          },
          error: (err: unknown) => {
            this.saving.set(false);
            this.saveError.set(payoutHoldProblem(err));
          },
        });
      });
  }
}
