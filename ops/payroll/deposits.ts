import { Component, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { apiErrorMessage, BannerComponent, SkeletonComponent } from '@hr-host';
import { MatButton } from '@angular/material/button';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { PayrollApi } from './payroll.api';
import { PayrollDepositDiscrepancy, PayrollRunLine } from './payroll.models';

/** Text from a problem-details body, then the host's error helper. */
export function depositProblem(err: unknown): string {
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
  return apiErrorMessage(err) || 'Could not record the deposit.';
}

/** Peso text. A negative discrepancy keeps the sign in front of the symbol. */
export function formatDepositAmount(amount: number): string {
  const value = Number(amount);
  const sign = value < 0 ? '−' : '';
  const text = Math.abs(value).toLocaleString('en-US', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
  return `${sign}₱${text}`;
}

@Component({
  selector: 'app-ops-payroll-deposits',
  imports: [FormsModule, BannerComponent, SkeletonComponent, MatButton, MatFormField, MatLabel, MatInput],
  templateUrl: './deposits.html',
  styles: `
    .deposit-rows {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
      margin-top: var(--space-3);
    }
    .deposit-row {
      display: grid;
      grid-template-columns: minmax(0, 1.2fr) auto minmax(8rem, 12rem) auto auto;
      column-gap: var(--space-3);
      row-gap: var(--space-2);
      align-items: center;
      min-width: 0;
    }
    .deposit-row mat-form-field {
      width: 100%;
      min-width: 0;
    }
    .deposit-list {
      margin: 0 0 var(--space-3);
      padding-left: 1.2rem;
    }
    @media (max-width: 40rem) {
      .deposit-row {
        grid-template-columns: minmax(0, 1fr);
      }
    }
  `,
})
export class PayrollDepositSettings {
  private readonly api = inject(PayrollApi);

  readonly runId = input.required<string>();
  readonly changed = output<void>();

  protected readonly loading = signal(false);
  protected readonly ready = signal(false);
  protected readonly loadError = signal('');
  protected readonly saveError = signal('');
  protected readonly notice = signal('');
  protected readonly saving = signal(false);
  protected readonly lines = signal<PayrollRunLine[]>([]);
  protected readonly discrepancies = signal<PayrollDepositDiscrepancy[]>([]);
  protected readonly amounts = signal<Record<string, number | null>>({});
  protected readonly formatDepositAmount = formatDepositAmount;

  load(): void {
    this.loading.set(true);
    this.loadError.set('');
    this.api.getRun(this.runId()).subscribe({
      next: (body) => {
        const lines = body.lines ?? [];
        const amounts: Record<string, number | null> = {};
        for (const line of lines) {
          amounts[line.staffId] = line.depositedAmount == null ? null : Number(line.depositedAmount);
        }
        this.lines.set(lines);
        this.discrepancies.set(body.discrepancies ?? []);
        this.amounts.set(amounts);
        this.ready.set(true);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.ready.set(false);
        this.loading.set(false);
        this.loadError.set(depositProblem(err) || 'Could not load the run.');
      },
    });
  }

  protected amount(staffId: string): number | null {
    return this.amounts()[staffId] ?? null;
  }

  protected setAmount(staffId: string, value: number | null): void {
    this.amounts.update((current) => ({ ...current, [staffId]: value }));
  }

  protected save(line: PayrollRunLine): void {
    this.saving.set(true);
    this.saveError.set('');
    this.notice.set('');
    this.api
      .recordDeposit(this.runId(), {
        staffId: line.staffId,
        depositedAmount: this.amount(line.staffId),
      })
      .subscribe({
        next: (saved) => {
          this.saving.set(false);
          this.notice.set(`${saved.staffName} deposited ${formatDepositAmount(saved.depositedAmount)}.`);
          this.load();
          this.changed.emit();
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.saveError.set(depositProblem(err));
        },
      });
  }
}
