import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import { BannerComponent, DialogComponent } from '@hr-host';
import { loanKindSuggestions } from './payroll-loans.format';
import { StaffLoan } from './payroll.models';

export interface LoanDialogData {
  loan: StaffLoan | null;
  suggestedKinds: readonly string[];
}

export interface LoanDialogResult {
  kind: string;
  principal: number;
  perPeriodAmount: number;
  startPeriod: string;
  note: string | null;
}

export interface LoanOverrideChoice {
  loanId: string;
  kind: string;
  amount: number;
}

export interface LoanOverrideDialogData {
  staffName: string;
  loans: readonly LoanOverrideChoice[];
}

export interface LoanOverrideDialogResult {
  loanId: string;
  amount: number | null;
  reason: string;
}

@Component({
  selector: 'app-staff-loan-dialog',
  imports: [
    FormsModule,
    MatAutocompleteModule,
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
    <ops-dialog [title]="data.loan ? 'Edit loan' : 'Add loan'">
      <form id="loan-form" (ngSubmit)="save()">
        @if (error()) {
          <ops-banner tone="danger" role="alert" [message]="error()" />
        }
        <div class="loan-dialog-fields">
          <mat-form-field appearance="outline">
            <mat-label>Kind</mat-label>
            <input
              matInput
              name="kind"
              [(ngModel)]="kind"
              [matAutocomplete]="kindAuto"
              required
              maxlength="80"
              autocomplete="off"
            />
            <mat-autocomplete #kindAuto="matAutocomplete">
              @for (option of filteredKinds(); track option) {
                <mat-option [value]="option">{{ option }}</mat-option>
              }
            </mat-autocomplete>
            <mat-error>Kind is required.</mat-error>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Principal</mat-label>
            <input
              matInput
              name="principal"
              type="number"
              min="0.01"
              step="0.01"
              [(ngModel)]="principal"
              required
            />
            <mat-error>Principal must be greater than zero.</mat-error>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Per period</mat-label>
            <input
              matInput
              name="perPeriodAmount"
              type="number"
              min="0.01"
              step="0.01"
              [(ngModel)]="perPeriodAmount"
              required
            />
            <mat-error>Per period must be greater than zero.</mat-error>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Start period</mat-label>
            <input matInput name="startPeriod" type="date" [(ngModel)]="startPeriod" required />
            <mat-hint
              >Instalments start in the first period that ends on or after this date.</mat-hint
            >
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Note</mat-label>
            <input matInput name="note" [(ngModel)]="note" maxlength="500" />
          </mat-form-field>
        </div>
      </form>
      <ng-container dialogActions>
        <button matButton="text" type="button" (click)="close()">Cancel</button>
        <button matButton="filled" type="submit" form="loan-form">Save</button>
      </ng-container>
    </ops-dialog>
  `,
  styles: `
    .loan-dialog-fields {
      display: grid;
      gap: var(--space-1);
      padding-top: var(--space-1);
    }
    .loan-dialog-fields mat-form-field {
      width: 100%;
    }
  `,
})
export class StaffLoanDialog {
  protected readonly data = inject<LoanDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<StaffLoanDialog, LoanDialogResult | undefined>);

  protected readonly error = signal('');
  protected kind = this.data.loan?.kind ?? '';
  protected principal: string | number | null = this.data.loan?.principal ?? '';
  protected perPeriodAmount: string | number | null = this.data.loan?.perPeriodAmount ?? '';
  protected startPeriod = this.data.loan?.startPeriod?.slice(0, 10) ?? '';
  protected note = this.data.loan?.note ?? '';

  private readonly suggestions = loanKindSuggestions(this.data.suggestedKinds);

  protected filteredKinds(): string[] {
    const query = this.kind.trim().toLowerCase();
    if (!query) {
      return this.suggestions;
    }
    return this.suggestions.filter((option) => option.toLowerCase().includes(query));
  }

  protected close(): void {
    this.ref.close(undefined);
  }

  protected save(): void {
    const kind = this.kind.trim();
    if (!kind || kind.length > 80) {
      this.error.set('Kind is required and must be 80 characters or fewer.');
      return;
    }
    const principal = readPositive(this.principal);
    if (principal == null) {
      this.error.set('Principal must be greater than zero.');
      return;
    }
    const perPeriodAmount = readPositive(this.perPeriodAmount);
    if (perPeriodAmount == null) {
      this.error.set('Per period must be greater than zero.');
      return;
    }
    const startPeriod = this.startPeriod.trim();
    if (!/^\d{4}-\d{2}-\d{2}$/.test(startPeriod)) {
      this.error.set('Start period is required.');
      return;
    }
    const note = this.note.trim();
    if (note.length > 500) {
      this.error.set('Note must be 500 characters or fewer.');
      return;
    }
    this.ref.close({
      kind,
      principal,
      perPeriodAmount,
      startPeriod,
      note: note || null,
    });
  }
}

@Component({
  selector: 'app-loan-override-dialog',
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
    <ops-dialog title="Edit loan instalment" [subtitle]="data.staffName">
      <form id="loan-override" (ngSubmit)="save()">
        @if (error()) {
          <ops-banner tone="danger" role="alert" [message]="error()" />
        }
        <div class="loan-dialog-fields">
          @if (data.loans.length === 0) {
            <ops-banner tone="info" message="No loan instalment on this period." />
          }
          <mat-form-field appearance="outline">
            <mat-label>Loan</mat-label>
            <mat-select name="loanId" [(ngModel)]="loanId" [disabled]="data.loans.length === 0">
              @for (loan of data.loans; track loan.loanId) {
                <mat-option [value]="loan.loanId">{{ loan.kind }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Amount</mat-label>
            <input matInput name="amount" type="number" min="0" step="0.01" [(ngModel)]="amount" />
            <mat-hint
              >Leave blank to clear the edited amount. Still capped at the balance and at net
              pay.</mat-hint
            >
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
        <button
          matButton="filled"
          type="submit"
          form="loan-override"
          [disabled]="data.loans.length === 0"
        >
          Save
        </button>
      </ng-container>
    </ops-dialog>
  `,
  styles: `
    .loan-dialog-fields {
      display: grid;
      gap: var(--space-1);
      padding-top: var(--space-1);
    }
    .loan-dialog-fields mat-form-field {
      width: 100%;
    }
  `,
})
export class LoanOverrideDialog {
  protected readonly data = inject<LoanOverrideDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject(
    MatDialogRef<LoanOverrideDialog, LoanOverrideDialogResult | undefined>,
  );

  protected readonly error = signal('');
  protected loanId = this.data.loans.length === 1 ? this.data.loans[0].loanId : '';
  protected amount: string | number | null =
    this.data.loans.length === 1 ? this.data.loans[0].amount : '';
  protected reason = '';

  protected close(): void {
    this.ref.close(undefined);
  }

  protected save(): void {
    if (!this.loanId) {
      this.error.set('Pick a loan on this period.');
      return;
    }
    const reason = this.reason.trim();
    if (!reason) {
      this.error.set('A reason is required.');
      return;
    }
    const raw = this.amount;
    if (raw == null || (typeof raw === 'string' && raw.trim() === '')) {
      this.ref.close({ loanId: this.loanId, amount: null, reason });
      return;
    }
    const amount = typeof raw === 'number' ? raw : Number(raw);
    if (!Number.isFinite(amount) || amount < 0) {
      this.error.set('Amount must be zero or more.');
      return;
    }
    this.ref.close({ loanId: this.loanId, amount, reason });
  }
}

function readPositive(raw: string | number | null): number | null {
  if (raw == null || (typeof raw === 'string' && raw.trim() === '')) {
    return null;
  }
  const amount = typeof raw === 'number' ? raw : Number(raw);
  if (!Number.isFinite(amount) || amount <= 0) {
    return null;
  }
  return amount;
}
