import { Component, inject, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { apiErrorMessage, DialogComponent } from '@hr-host';
import { PayrollApi } from './payroll.api';
import { money } from './payroll-lock';
import { PayrollFinalPay } from './payroll.models';
import { formatDay } from '../hr-dates';

export interface PayrollFinalPayDialogData {
  staffId: string;
  staffName: string;
}

/**
 * Final pay estimate for a separated worker: wages since the last locked period, a pro-rated
 * 13th month, and cash-convertible SIL. Estimate only — the owner verifies before paying.
 */
@Component({
  selector: 'app-payroll-final-pay-dialog',
  imports: [DialogComponent, MatButton],
  template: `
    <ops-dialog [title]="'Final pay — ' + data.staffName">
      @if (loading()) {
        <p class="ops-empty">Computing final pay…</p>
      } @else if (error()) {
        <p class="ops-empty" data-final-pay-error>{{ error() }}</p>
        <p class="ops-hint">Set the separation date on the person in Directory first.</p>
      } @else if (result(); as final) {
        <p data-final-pay-date>
          <strong>Separated:</strong> {{ formatDay(final.separationDate) }}
          @if (final.separationReason) {
            — {{ final.separationReason }}
          }
        </p>
        <dl class="ops-final-pay" data-final-pay-rows>
          <div>
            <dt>Wages ({{ final.wages.from }} → {{ final.wages.to }})</dt>
            <dd>{{ money(final.wages.gross) }}</dd>
          </div>
          <div>
            <dt>
              13th month {{ final.thirteenthMonth.year }} (basic earned
              {{ money(final.thirteenthMonth.basicEarned) }} / 12)
            </dt>
            <dd>{{ money(final.thirteenthMonth.amount) }}</dd>
          </div>
          <div>
            @if (final.sil.eligible) {
              <dt>
                SIL — {{ final.sil.remaining }} unused day(s) at {{ money(final.sil.dailyRate) }}
              </dt>
              <dd>{{ money(final.sil.amount) }}</dd>
            } @else {
              <dt>Service incentive leave</dt>
              <dd>Not eligible — SIL needs one year of service</dd>
            }
          </div>
          <div class="ops-final-total">
            <dt>Gross total</dt>
            <dd>
              <strong>{{ money(final.grossTotal) }}</strong>
            </dd>
          </div>
        </dl>
        <ul class="ops-notes" data-final-pay-notes>
          @for (note of final.notes; track note) {
            <li>{{ note }}</li>
          }
        </ul>
      }
      <div dialogActions>
        <button matButton="text" type="button" (click)="dialogRef.close()">Close</button>
      </div>
    </ops-dialog>
  `,
})
export class PayrollFinalPayDialog {
  protected readonly formatDay = formatDay;
  protected readonly dialogRef = inject(MatDialogRef<PayrollFinalPayDialog>);
  private readonly api = inject(PayrollApi);
  protected readonly data = inject<PayrollFinalPayDialogData>(MAT_DIALOG_DATA);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly result = signal<PayrollFinalPay | null>(null);
  protected readonly money = money;

  constructor() {
    this.api.getFinalPay(this.data.staffId).subscribe({
      next: (final) => {
        this.result.set(final);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.error.set(apiErrorMessage(err));
        this.loading.set(false);
      },
    });
  }
}
