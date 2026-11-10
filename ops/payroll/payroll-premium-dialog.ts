import { Component, inject, signal } from '@angular/core';
import { FormsModule, NgModel } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import { BannerComponent, DialogComponent, OpsDateModule } from '@hr-host';
import { premiumLabel } from './payroll-premiums.format';
import { HolidayKind, ShopHoliday } from './payroll.models';

export interface HolidayDialogData {
  year: number;
  holiday: ShopHoliday | null;
}

export interface HolidayDialogResult {
  date: string;
  name: string;
  kind: HolidayKind;
}

export interface PremiumDialogData {
  staffName: string;
  amount: number | null;
}

export interface PremiumDialogResult {
  amount: number | null;
  reason: string;
}

@Component({
  selector: 'app-holiday-dialog',
  imports: [
    FormsModule,
    MatButton,
    MatFormField,
    MatLabel,
    MatError,
    MatInput,
    MatSelect,
    MatOption,
    OpsDateModule,
    DialogComponent,
    BannerComponent,
  ],
  template: `
    <ops-dialog [title]="data.holiday ? 'Edit holiday' : 'Add holiday'">
      <form id="holiday-form" (ngSubmit)="save(dateModel)">
        @if (error()) {
          <ops-banner tone="danger" role="alert" [message]="error()" />
        }
        <div class="premium-dialog-fields">
          <mat-form-field appearance="outline">
            <mat-label>Date</mat-label>
            <input
              matInput
              [matDatepicker]="holidayDatePicker"
              name="date"
              placeholder="MM/DD/YYYY"
              [(ngModel)]="date"
              #dateModel="ngModel"
              required
            />
            <mat-datepicker-toggle matIconSuffix [for]="holidayDatePicker" />
            <mat-datepicker #holidayDatePicker />
            @if (dateModel.hasError('matDatepickerParse')) {
              <mat-error>Enter a date as MM/DD/YYYY.</mat-error>
            } @else {
              <mat-error>Date is required.</mat-error>
            }
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Name</mat-label>
            <input matInput name="name" [(ngModel)]="name" required maxlength="120" />
            <mat-error>Name is required.</mat-error>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Kind</mat-label>
            <mat-select name="kind" [(ngModel)]="kind">
              <mat-option value="regular">{{ premiumLabel('regular') }}</mat-option>
              <mat-option value="special">{{ premiumLabel('special') }}</mat-option>
            </mat-select>
          </mat-form-field>
        </div>
      </form>
      <ng-container dialogActions>
        <button matButton="text" type="button" (click)="close()">Cancel</button>
        <button matButton="filled" type="submit" form="holiday-form">Save</button>
      </ng-container>
    </ops-dialog>
  `,
  styles: `
    .premium-dialog-fields {
      display: grid;
      gap: var(--space-1);
      padding-top: var(--space-1);
    }
    .premium-dialog-fields mat-form-field {
      width: 100%;
    }
  `,
})
export class HolidayDialog {
  protected readonly data = inject<HolidayDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<HolidayDialog, HolidayDialogResult | undefined>);

  protected readonly premiumLabel = premiumLabel;
  protected readonly error = signal('');
  protected date = this.data.holiday?.date ?? '';
  protected name = this.data.holiday?.name ?? '';
  protected kind: HolidayKind = this.data.holiday?.kind ?? 'regular';

  protected close(): void {
    this.ref.close(undefined);
  }

  protected save(dateModel: NgModel): void {
    if (dateModel.hasError('matDatepickerParse')) {
      dateModel.control.markAsTouched();
      return;
    }
    const name = this.name.trim();
    if (!this.date || !name) {
      this.error.set('Date and name are required.');
      return;
    }
    if (!this.date.startsWith(`${this.data.year}-`)) {
      this.error.set(`Date must be in ${this.data.year}.`);
      return;
    }
    this.ref.close({ date: this.date, name, kind: this.kind });
  }
}

@Component({
  selector: 'app-premium-override-dialog',
  imports: [
    FormsModule,
    MatButton,
    MatFormField,
    MatLabel,
    MatHint,
    MatError,
    MatInput,
    DialogComponent,
    BannerComponent,
  ],
  template: `
    <ops-dialog title="Edit premium" [subtitle]="data.staffName">
      <form id="premium-override" (ngSubmit)="save()">
        @if (error()) {
          <ops-banner tone="danger" role="alert" [message]="error()" />
        }
        <div class="premium-dialog-fields">
          <mat-form-field appearance="outline">
            <mat-label>Premium pay</mat-label>
            <input matInput name="amount" type="number" min="0" step="0.01" [(ngModel)]="amount" />
            <mat-hint>Leave blank to clear the edited amount.</mat-hint>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Reason</mat-label>
            <input matInput name="reason" [(ngModel)]="reason" required maxlength="500" />
            <mat-error>A reason is required.</mat-error>
          </mat-form-field>
        </div>
      </form>
      <ng-container dialogActions>
        <button matButton="text" type="button" (click)="close()">Cancel</button>
        <button matButton="filled" type="submit" form="premium-override">Save</button>
      </ng-container>
    </ops-dialog>
  `,
  styles: `
    .premium-dialog-fields {
      display: grid;
      gap: var(--space-1);
      padding-top: var(--space-1);
    }
    .premium-dialog-fields mat-form-field {
      width: 100%;
    }
  `,
})
export class PremiumOverrideDialog {
  protected readonly data = inject<PremiumDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject(
    MatDialogRef<PremiumOverrideDialog, PremiumDialogResult | undefined>,
  );

  protected readonly error = signal('');
  /** Number inputs write a number; a cleared field writes null or ''. */
  protected amount: string | number | null = this.data.amount == null ? '' : this.data.amount;
  protected reason = '';

  protected close(): void {
    this.ref.close(undefined);
  }

  protected save(): void {
    const reason = this.reason.trim();
    if (!reason) {
      this.error.set('A reason is required.');
      return;
    }
    const raw = this.amount;
    if (raw == null || (typeof raw === 'string' && raw.trim() === '')) {
      this.ref.close({ amount: null, reason });
      return;
    }
    const amount = typeof raw === 'number' ? raw : Number(raw);
    if (!Number.isFinite(amount) || amount < 0) {
      this.error.set('Amount must be zero or more.');
      return;
    }
    this.ref.close({ amount, reason });
  }
}
