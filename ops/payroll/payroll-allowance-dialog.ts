import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import { MatSlideToggle } from '@angular/material/slide-toggle';
import { BannerComponent, DialogComponent } from '@hr-host';
import {
  allowanceNameSuggestions,
  allowancePerLabel,
  normalizeAllowancePer,
} from './payroll-allowances.format';
import { AllowancePer, StaffAllowance } from './payroll.models';

export interface AllowanceDialogData {
  allowance: StaffAllowance | null;
  suggestedNames: readonly string[];
}

export interface AllowanceDialogResult {
  name: string;
  amount: number;
  per: AllowancePer;
  taxable: boolean;
  effectiveFrom: string | null;
  effectiveTo: string | null;
  note: string | null;
  active: boolean;
}

export interface AllowanceOverrideDialogData {
  staffName: string;
  amount: number | null;
}

export interface AllowanceOverrideDialogResult {
  amount: number | null;
  reason: string;
}

@Component({
  selector: 'app-staff-allowance-dialog',
  imports: [
    FormsModule,
    MatAutocompleteModule,
    MatButton,
    MatFormField,
    MatLabel,
    MatHint,
    MatError,
    MatInput,
    MatSelect,
    MatOption,
    MatSlideToggle,
    DialogComponent,
    BannerComponent,
  ],
  template: `
    <ops-dialog [title]="data.allowance ? 'Edit allowance' : 'Add allowance'">
      <form id="allowance-form" (ngSubmit)="save()">
        @if (error()) {
          <ops-banner tone="danger" role="alert" [message]="error()" />
        }
        <div class="allowance-dialog-fields">
          <mat-form-field appearance="outline">
            <mat-label>Name</mat-label>
            <input
              matInput
              name="name"
              [(ngModel)]="name"
              [matAutocomplete]="nameAuto"
              required
              maxlength="80"
              autocomplete="off"
            />
            <mat-autocomplete #nameAuto="matAutocomplete">
              @for (option of filteredNames(); track option) {
                <mat-option [value]="option">{{ option }}</mat-option>
              }
            </mat-autocomplete>
            <mat-error>Name is required.</mat-error>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Amount</mat-label>
            <input
              matInput
              name="amount"
              type="number"
              min="0"
              step="0.01"
              [(ngModel)]="amount"
              required
            />
            <mat-error>Amount must be zero or more.</mat-error>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Per</mat-label>
            <mat-select name="per" [(ngModel)]="per">
              <mat-option value="period">Period</mat-option>
              <mat-option value="day">Day</mat-option>
            </mat-select>
            <mat-hint>{{ allowancePerLabel(per) }}</mat-hint>
          </mat-form-field>
          <mat-slide-toggle name="taxable" [(ngModel)]="taxable">Taxable</mat-slide-toggle>
          <mat-form-field appearance="outline">
            <mat-label>Effective from</mat-label>
            <input matInput name="effectiveFrom" type="date" [(ngModel)]="effectiveFrom" />
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Effective to</mat-label>
            <input matInput name="effectiveTo" type="date" [(ngModel)]="effectiveTo" />
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Note</mat-label>
            <input matInput name="note" [(ngModel)]="note" maxlength="500" />
          </mat-form-field>
          <mat-slide-toggle name="active" [(ngModel)]="active">Active</mat-slide-toggle>
        </div>
      </form>
      <ng-container dialogActions>
        <button matButton="text" type="button" (click)="close()">Cancel</button>
        <button matButton="filled" type="submit" form="allowance-form">Save</button>
      </ng-container>
    </ops-dialog>
  `,
  styles: `
    .allowance-dialog-fields {
      display: grid;
      gap: var(--space-1);
      padding-top: var(--space-1);
    }
    .allowance-dialog-fields mat-form-field {
      width: 100%;
    }
  `,
})
export class StaffAllowanceDialog {
  protected readonly data = inject<AllowanceDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject(
    MatDialogRef<StaffAllowanceDialog, AllowanceDialogResult | undefined>,
  );

  protected readonly allowancePerLabel = allowancePerLabel;
  protected readonly error = signal('');
  protected name = this.data.allowance?.name ?? '';
  protected amount: string | number | null = this.data.allowance?.amount ?? '';
  protected per: AllowancePer = normalizeAllowancePer(this.data.allowance?.per);
  protected taxable = this.data.allowance?.taxable ?? true;
  protected effectiveFrom = this.data.allowance?.effectiveFrom?.slice(0, 10) ?? '';
  protected effectiveTo = this.data.allowance?.effectiveTo?.slice(0, 10) ?? '';
  protected note = this.data.allowance?.note ?? '';
  protected active = this.data.allowance?.active ?? true;

  private readonly suggestions = allowanceNameSuggestions(this.data.suggestedNames);

  protected filteredNames(): string[] {
    const query = this.name.trim().toLowerCase();
    if (!query) {
      return this.suggestions;
    }
    return this.suggestions.filter((option) => option.toLowerCase().includes(query));
  }

  protected close(): void {
    this.ref.close(undefined);
  }

  protected save(): void {
    const name = this.name.trim();
    if (!name || name.length > 80) {
      this.error.set('Name is required and must be 80 characters or fewer.');
      return;
    }
    const raw = this.amount;
    if (raw == null || (typeof raw === 'string' && raw.trim() === '')) {
      this.error.set('Amount must be zero or more.');
      return;
    }
    const amount = typeof raw === 'number' ? raw : Number(raw);
    if (!Number.isFinite(amount) || amount < 0) {
      this.error.set('Amount must be zero or more.');
      return;
    }
    const from = this.effectiveFrom.trim();
    const to = this.effectiveTo.trim();
    if (from && to && to < from) {
      this.error.set('Effective to must be on or after effective from.');
      return;
    }
    const note = this.note.trim();
    if (note.length > 500) {
      this.error.set('Note must be 500 characters or fewer.');
      return;
    }
    this.ref.close({
      name,
      amount,
      per: this.per,
      taxable: this.taxable,
      effectiveFrom: from || null,
      effectiveTo: to || null,
      note: note || null,
      active: this.active,
    });
  }
}

@Component({
  selector: 'app-allowance-override-dialog',
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
    <ops-dialog title="Edit allowances" [subtitle]="data.staffName">
      <form id="allowance-override" (ngSubmit)="save()">
        @if (error()) {
          <ops-banner tone="danger" role="alert" [message]="error()" />
        }
        <div class="allowance-dialog-fields">
          <mat-form-field appearance="outline">
            <mat-label>COLA / allowances</mat-label>
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
        <button matButton="filled" type="submit" form="allowance-override">Save</button>
      </ng-container>
    </ops-dialog>
  `,
  styles: `
    .allowance-dialog-fields {
      display: grid;
      gap: var(--space-1);
      padding-top: var(--space-1);
    }
    .allowance-dialog-fields mat-form-field {
      width: 100%;
    }
  `,
})
export class AllowanceOverrideDialog {
  protected readonly data = inject<AllowanceOverrideDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject(
    MatDialogRef<AllowanceOverrideDialog, AllowanceOverrideDialogResult | undefined>,
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
