import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import { BannerComponent, DialogComponent } from '@hr-host';
import { PayrollDayPayMode } from './payroll.models';

export interface DayPayOverrideDialogData {
  staffName: string;
  workDate: string;
  mode: PayrollDayPayMode | null;
  customAmount: number | null;
  reason: string;
}

export interface DayPayOverrideDialogResult {
  workDate: string;
  mode: PayrollDayPayMode | null;
  customAmount: number | null;
  reason: string;
}

@Component({
  selector: 'app-day-pay-override-dialog',
  imports: [
    FormsModule,
    MatButton,
    MatFormField,
    MatLabel,
    MatHint,
    MatError,
    MatInput,
    MatSelect,
    MatOption,
    DialogComponent,
    BannerComponent,
  ],
  template: `
    <ops-dialog title="Day pay override" [subtitle]="data.staffName">
      <form id="day-pay-override" (ngSubmit)="save()">
        @if (error()) {
          <ops-banner tone="danger" role="alert" [message]="error()" />
        }
        <div class="day-override-fields">
          <mat-form-field appearance="outline">
            <mat-label>Work date</mat-label>
            <input matInput name="workDate" type="date" [(ngModel)]="workDate" required />
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Mode</mat-label>
            <mat-select name="mode" [(ngModel)]="mode">
              <mat-option [value]="null">Clear override</mat-option>
              <mat-option value="full">Full day</mat-option>
              <mat-option value="half">Half day</mat-option>
              <mat-option value="none">None</mat-option>
              <mat-option value="custom">Custom amount</mat-option>
            </mat-select>
            <mat-hint>Clears the override and restores calculated pay.</mat-hint>
          </mat-form-field>
          @if (mode === 'custom') {
            <mat-form-field appearance="outline">
              <mat-label>Custom amount (PHP)</mat-label>
              <input
                matInput
                name="customAmount"
                type="number"
                min="0"
                step="0.01"
                [(ngModel)]="customAmount"
                required
              />
            </mat-form-field>
          }
          <mat-form-field appearance="outline">
            <mat-label>Reason</mat-label>
            <input matInput name="reason" [(ngModel)]="reason" required maxlength="500" />
            <mat-error>A reason is required.</mat-error>
          </mat-form-field>
        </div>
      </form>
      <ng-container dialogActions>
        <button matButton="text" type="button" (click)="close()">Cancel</button>
        <button matButton="filled" type="submit" form="day-pay-override">Save</button>
      </ng-container>
    </ops-dialog>
  `,
  styles: `
    .day-override-fields {
      display: grid;
      gap: var(--space-1);
      padding-top: var(--space-1);
    }
    .day-override-fields mat-form-field {
      width: 100%;
    }
  `,
})
export class DayPayOverrideDialog {
  protected readonly data = inject<DayPayOverrideDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject(
    MatDialogRef<DayPayOverrideDialog, DayPayOverrideDialogResult | undefined>,
  );

  protected readonly error = signal('');
  protected workDate = this.data.workDate;
  protected mode: PayrollDayPayMode | null = this.data.mode;
  protected customAmount: string | number | null =
    this.data.customAmount == null ? '' : this.data.customAmount;
  protected reason = this.data.reason;

  protected close(): void {
    this.ref.close(undefined);
  }

  protected save(): void {
    const reason = this.reason.trim();
    if (!reason) {
      this.error.set('A reason is required.');
      return;
    }
    if (!this.workDate) {
      this.error.set('Work date is required.');
      return;
    }
    if (this.mode === null) {
      this.ref.close({ workDate: this.workDate, mode: null, customAmount: null, reason });
      return;
    }
    if (this.mode !== 'custom') {
      this.ref.close({
        workDate: this.workDate,
        mode: this.mode,
        customAmount: null,
        reason,
      });
      return;
    }
    const raw = this.customAmount;
    if (raw == null || (typeof raw === 'string' && raw.trim() === '')) {
      this.error.set('Custom amount is required.');
      return;
    }
    const amount = typeof raw === 'number' ? raw : Number(raw);
    if (!Number.isFinite(amount) || amount < 0) {
      this.error.set('Custom amount must be zero or more.');
      return;
    }
    this.ref.close({
      workDate: this.workDate,
      mode: 'custom',
      customAmount: amount,
      reason,
    });
  }
}
