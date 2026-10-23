import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import {
  apiErrorMessage,
  formatManilaDateTime,
  manilaToday,
  BannerComponent,
  DialogComponent,
} from '@hr-host';
import { PayrollApi } from './payroll.api';
import { basisLabel, money, rateSliceLabels } from './payroll-lock';
import {
  PayBasis,
  PayrollRateHistoryRow,
  PayrollRateSlice,
  PayrollRatesResponse,
} from './payroll.models';

export interface PayrollRateDialogData {
  staffId: string;
  staffName: string;
  /** Rate spans of the open period, shown so a mid-period change is visible next to the history. */
  rateSlices?: PayrollRateSlice[] | null;
}

/** Rate history and a dated rate change for one worker. */
@Component({
  selector: 'app-payroll-rate-dialog',
  imports: [
    FormsModule,
    MatButton,
    MatFormField,
    MatLabel,
    MatInput,
    MatSelect,
    MatOption,
    MatTableModule,
    DialogComponent,
    BannerComponent,
  ],
  template: `
    <ops-dialog [title]="'Pay rate — ' + data.staffName">
      @if (loading()) {
        <p class="ops-empty">Loading rate history…</p>
      } @else if (loadError()) {
        <ops-banner tone="danger" role="alert" [message]="loadError()" />
      } @else {
        <div class="rate-form">
          <section class="rate-current" data-rate-current>
            <h3 class="rate-title">Current</h3>
            <dl class="rate-grid">
              <dt>Basis</dt>
              <dd data-rate-basis>{{ basisLabel(rates()?.basis) }}</dd>
              <dt>Rate</dt>
              <dd data-rate-amount>{{ formatRate(rates()?.ratePhp) }}</dd>
              <dt>OT after</dt>
              <dd>{{ hoursLabel(rates()?.otAfterHoursPerDay) }}</dd>
              <dt>OT multiplier</dt>
              <dd>{{ multiplierLabel(rates()?.otMultiplier) }}</dd>
            </dl>
          </section>

          @if (sliceLabels().length > 0) {
            <section class="rate-slices" data-rate-slices>
              <h3 class="rate-title">Rates used in this period</h3>
              <ul>
                @for (label of sliceLabels(); track label) {
                  <li>{{ label }}</li>
                }
              </ul>
            </section>
          }

          <section class="rate-history" data-rate-history>
            <h3 class="rate-title">History</h3>
            @if (history().length === 0) {
              <p class="ops-empty">No rate changes recorded yet.</p>
            } @else {
              <div class="ops-table-wrap">
                <table mat-table [dataSource]="history()" class="ops-table">
                  <ng-container matColumnDef="effectiveFrom">
                    <th mat-header-cell *matHeaderCellDef>Effective from</th>
                    <td mat-cell *matCellDef="let row" data-label="Effective from">
                      {{ row.effectiveFrom }}
                    </td>
                  </ng-container>
                  <ng-container matColumnDef="basis">
                    <th mat-header-cell *matHeaderCellDef>Basis</th>
                    <td mat-cell *matCellDef="let row" data-label="Basis">
                      {{ basisLabel(row.basis) }}
                    </td>
                  </ng-container>
                  <ng-container matColumnDef="rate">
                    <th mat-header-cell *matHeaderCellDef>Rate</th>
                    <td mat-cell *matCellDef="let row" data-label="Rate">
                      {{ money(row.ratePhp) }}
                    </td>
                  </ng-container>
                  <ng-container matColumnDef="ot">
                    <th mat-header-cell *matHeaderCellDef>OT</th>
                    <td mat-cell *matCellDef="let row" data-label="OT">{{ otLabel(row) }}</td>
                  </ng-container>
                  <ng-container matColumnDef="reason">
                    <th mat-header-cell *matHeaderCellDef>Reason</th>
                    <td mat-cell *matCellDef="let row" data-label="Reason">
                      {{ row.reason || '—' }}
                    </td>
                  </ng-container>
                  <ng-container matColumnDef="createdBy">
                    <th mat-header-cell *matHeaderCellDef>Created by</th>
                    <td mat-cell *matCellDef="let row" data-label="Created by">
                      {{ row.createdByName || '—' }}
                    </td>
                  </ng-container>
                  <ng-container matColumnDef="createdAt">
                    <th mat-header-cell *matHeaderCellDef>Created at</th>
                    <td mat-cell *matCellDef="let row" data-label="Created at">
                      {{ createdLabel(row.createdAtUtc) }}
                    </td>
                  </ng-container>
                  <tr mat-header-row *matHeaderRowDef="historyColumns"></tr>
                  <tr mat-row *matRowDef="let row; columns: historyColumns"></tr>
                </table>
              </div>
            }
          </section>

          <section class="rate-change" data-rate-change>
            <h3 class="rate-title">Change rate</h3>
            @if (error()) {
              <ops-banner tone="danger" role="alert" [message]="error()" />
            }
            <form id="payroll-rate" (ngSubmit)="save()">
              <div class="rate-form-grid">
                <mat-form-field appearance="outline" subscriptSizing="dynamic" class="ops-field">
                  <mat-label>Basis</mat-label>
                  <mat-select name="rateBasis" [(ngModel)]="basis">
                    <mat-option value="hourly">Hourly</mat-option>
                    <mat-option value="daily">Daily</mat-option>
                    <mat-option value="monthly">Monthly</mat-option>
                  </mat-select>
                </mat-form-field>
                <mat-form-field appearance="outline" subscriptSizing="dynamic" class="ops-field">
                  <mat-label>Rate (PHP)</mat-label>
                  <input
                    matInput
                    name="ratePhp"
                    type="number"
                    min="0"
                    step="0.01"
                    [(ngModel)]="ratePhp"
                  />
                </mat-form-field>
                <mat-form-field appearance="outline" subscriptSizing="dynamic" class="ops-field">
                  <mat-label>OT after hours per day</mat-label>
                  <input
                    matInput
                    name="rateOtAfter"
                    type="number"
                    min="0"
                    step="0.5"
                    [(ngModel)]="otAfter"
                  />
                </mat-form-field>
                <mat-form-field appearance="outline" subscriptSizing="dynamic" class="ops-field">
                  <mat-label>OT multiplier</mat-label>
                  <input
                    matInput
                    name="rateOtMultiplier"
                    type="number"
                    min="0"
                    step="0.05"
                    [(ngModel)]="otMultiplier"
                  />
                </mat-form-field>
                <mat-form-field appearance="outline" subscriptSizing="dynamic" class="ops-field">
                  <mat-label>Effective from</mat-label>
                  <input
                    matInput
                    name="rateEffectiveFrom"
                    type="date"
                    [(ngModel)]="effectiveFrom"
                  />
                </mat-form-field>
                <mat-form-field appearance="outline" subscriptSizing="dynamic" class="ops-field">
                  <mat-label>Reason</mat-label>
                  <input matInput name="rateReason" [(ngModel)]="reason" maxlength="500" />
                </mat-form-field>
              </div>
              <div class="rate-form-actions">
                <button matButton="tonal" type="button" [disabled]="saving()" (click)="resetForm()">
                  Use current
                </button>
              </div>
            </form>
          </section>
        </div>
      }
      <div dialogActions>
        <button matButton="text" type="button" (click)="close()">Close</button>
        <button
          matButton="filled"
          type="submit"
          form="payroll-rate"
          name="saveRate"
          [disabled]="saving()"
        >
          {{ saving() ? 'Saving…' : 'Save rate' }}
        </button>
      </div>
    </ops-dialog>
  `,
  styles: `
    .rate-form {
      display: grid;
      gap: var(--space-4);
    }

    .rate-title {
      margin: 0 0 var(--space-1);
      font-size: 0.8rem;
      font-weight: 700;
      letter-spacing: 0.06em;
      text-transform: uppercase;
      color: var(--ops-muted, #5b6178);
    }

    .rate-grid {
      display: grid;
      grid-template-columns: max-content 1fr;
      column-gap: var(--space-4);
      row-gap: var(--space-1);
      margin: 0;
    }

    .rate-grid dt {
      font-weight: 600;
    }

    .rate-grid dd {
      margin: 0;
      min-width: 0;
      overflow-wrap: anywhere;
    }

    .rate-form-grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      column-gap: var(--space-3);
      row-gap: var(--space-1);
    }

    .rate-form-grid mat-form-field {
      width: 100%;
    }

    @media (max-width: 600px) {
      .rate-form-grid {
        grid-template-columns: 1fr;
      }
    }
  `,
})
export class PayrollRateDialog {
  protected readonly data = inject<PayrollRateDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<PayrollRateDialog, boolean>);
  private readonly api = inject(PayrollApi);

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly loadError = signal('');
  protected readonly error = signal('');
  protected readonly rates = signal<PayrollRatesResponse | null>(null);
  protected readonly historyColumns = [
    'effectiveFrom',
    'basis',
    'rate',
    'ot',
    'reason',
    'createdBy',
    'createdAt',
  ];

  protected basis: PayBasis = 'hourly';
  protected ratePhp: number | null = null;
  protected otAfter: number | null = null;
  protected otMultiplier: number | null = null;
  protected effectiveFrom = manilaToday();
  protected reason = '';

  private changed = false;

  protected readonly basisLabel = basisLabel;
  protected readonly money = money;

  constructor() {
    this.load();
  }

  protected history(): PayrollRateHistoryRow[] {
    return this.rates()?.history ?? [];
  }

  protected sliceLabels(): string[] {
    return rateSliceLabels(this.data.rateSlices);
  }

  protected formatRate(rate: number | null | undefined): string {
    return rate == null ? 'No rate' : money(rate);
  }

  protected hoursLabel(value: number | null | undefined): string {
    return value == null ? '—' : `${value} h`;
  }

  protected multiplierLabel(value: number | null | undefined): string {
    return value == null ? '—' : `×${value}`;
  }

  protected otLabel(row: {
    otAfterHoursPerDay?: number | null;
    otMultiplier?: number | null;
  }): string {
    const parts: string[] = [];
    if (row.otAfterHoursPerDay != null) {
      parts.push(`${row.otAfterHoursPerDay}h`);
    }
    if (row.otMultiplier != null) {
      parts.push(`×${row.otMultiplier}`);
    }
    return parts.length ? parts.join(' ') : '—';
  }

  protected createdLabel(iso: string | null | undefined): string {
    return formatManilaDateTime(iso);
  }

  protected resetForm(): void {
    this.fillForm(this.rates());
  }

  protected close(): void {
    this.ref.close(this.changed);
  }

  protected save(): void {
    const rate = Number(this.ratePhp);
    if (this.ratePhp == null || !Number.isFinite(rate) || rate < 0) {
      this.error.set('Rate must be zero or more.');
      return;
    }
    if (!this.effectiveFrom) {
      this.error.set('Effective from is required.');
      return;
    }

    this.saving.set(true);
    this.error.set('');
    this.api
      .setRate(this.data.staffId, {
        effectiveFrom: this.effectiveFrom,
        basis: this.basis,
        ratePhp: rate,
        otAfterHoursPerDay: this.numberOrNull(this.otAfter),
        otMultiplier: this.numberOrNull(this.otMultiplier),
        reason: this.reason.trim() || null,
      })
      .subscribe({
        next: (body) => {
          this.rates.set(body);
          this.changed = true;
          this.saving.set(false);
          this.fillForm(body);
        },
        error: (err: unknown) => {
          this.error.set(apiErrorMessage(err));
          this.saving.set(false);
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.loadError.set('');
    this.api.getRates(this.data.staffId).subscribe({
      next: (body) => {
        this.rates.set(body);
        this.fillForm(body);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loadError.set(apiErrorMessage(err));
        this.loading.set(false);
      },
    });
  }

  private fillForm(body: PayrollRatesResponse | null): void {
    this.basis = body?.basis ?? 'hourly';
    this.ratePhp = body?.ratePhp ?? null;
    this.otAfter = body?.otAfterHoursPerDay ?? null;
    this.otMultiplier = body?.otMultiplier ?? null;
    this.reason = '';
  }

  private numberOrNull(value: number | null): number | null {
    if (value == null || !Number.isFinite(Number(value))) {
      return null;
    }
    return Number(value);
  }
}
