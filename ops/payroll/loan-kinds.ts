import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { apiErrorMessage, BannerComponent, OpsConfirmService, SkeletonComponent } from '@hr-host';
import { MatButton } from '@angular/material/button';
import { MatCheckbox } from '@angular/material/checkbox';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import { concat, Observable, of } from 'rxjs';
import { PayrollApi } from './payroll.api';
import { LoanKind, SaveLoanKind } from './payroll.models';

interface KindDraft {
  id: string | null;
  name: string;
  active: boolean;
  cutoffOnly: boolean;
  cutoffOrdinal: number | null;
}

/** Text from a problem-details body, then the host's error helper. */
export function loanKindProblem(err: unknown): string {
  const body = (err as { error?: unknown } | null)?.error;
  if (body && typeof body === 'object') {
    const detail = (body as { detail?: unknown }).detail;
    if (typeof detail === 'string' && detail.trim()) {
      return detail;
    }
    const message = (body as { error?: unknown }).error;
    if (typeof message === 'string' && message.trim()) {
      return message;
    }
    const errors = (body as { errors?: unknown }).errors;
    if (errors && typeof errors === 'object') {
      for (const value of Object.values(errors)) {
        if (Array.isArray(value) && value.length > 0) {
          return String(value[0]);
        }
      }
    }
  }
  return apiErrorMessage(err) || 'Could not save the loan kind.';
}

@Component({
  selector: 'app-ops-loan-kinds',
  imports: [
    FormsModule,
    BannerComponent,
    SkeletonComponent,
    MatButton,
    MatCheckbox,
    MatFormField,
    MatLabel,
    MatInput,
    MatSelect,
    MatOption,
  ],
  templateUrl: './loan-kinds.html',
  styles: `
    .loan-kind-rows {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      min-width: 0;
    }
    .loan-kind-row {
      display: grid;
      grid-template-columns: minmax(0, 1.4fr) minmax(0, 1fr) minmax(0, 6rem) auto auto;
      column-gap: var(--space-3);
      row-gap: var(--space-2);
      align-items: start;
      min-width: 0;
    }
    .loan-kind-row mat-form-field,
    .loan-kind-row mat-checkbox {
      width: 100%;
      min-width: 0;
    }
    @media (max-width: 40rem) {
      .loan-kind-row {
        grid-template-columns: minmax(0, 1fr);
      }
    }
  `,
})
export class LoanKindSettings {
  private readonly api = inject(PayrollApi);
  private readonly confirm = inject(OpsConfirmService);

  protected readonly loading = signal(false);
  protected readonly ready = signal(false);
  protected readonly loadError = signal('');
  protected readonly saveError = signal('');
  protected readonly notice = signal('');
  protected readonly saving = signal(false);
  protected readonly rows = signal<KindDraft[]>([]);

  load(): void {
    this.loading.set(true);
    this.loadError.set('');
    this.saveError.set('');
    this.notice.set('');
    this.api.listLoanKinds().subscribe({
      next: (body) => {
        this.rows.set((body.kinds ?? []).map(draftFrom));
        this.ready.set(true);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.ready.set(false);
        this.loading.set(false);
        this.loadError.set(loanKindProblem(err) || 'Could not load loan kinds.');
      },
    });
  }

  protected addKind(): void {
    this.rows.update((list) => [
      ...list,
      { id: null, name: '', active: true, cutoffOnly: false, cutoffOrdinal: null },
    ]);
  }

  protected setName(index: number, value: string): void {
    this.patch(index, { name: value });
  }

  protected setRule(index: number, value: string): void {
    const cutoffOnly = value === 'cutoff';
    this.patch(index, {
      cutoffOnly,
      cutoffOrdinal: cutoffOnly ? (this.rows()[index]?.cutoffOrdinal ?? null) : null,
    });
  }

  protected setOrdinal(index: number, value: number | string | null): void {
    const raw = value == null ? '' : String(value).trim();
    const parsed = raw === '' ? null : Number(raw);
    this.patch(index, { cutoffOrdinal: parsed != null && Number.isFinite(parsed) ? parsed : null });
  }

  protected setActive(index: number, value: boolean): void {
    this.patch(index, { active: value });
  }

  protected remove(index: number): void {
    const row = this.rows()[index];
    if (!row) {
      return;
    }
    if (!row.id) {
      this.rows.update((list) => list.filter((_, i) => i !== index));
      return;
    }
    const id = row.id;
    this.confirm
      .confirm({
        title: 'Delete loan kind',
        message: `Delete ${row.name}? Loans that use it cannot be deleted this way.`,
        confirmLabel: 'Delete',
      })
      .subscribe((result) => {
        if (!result.confirmed) {
          return;
        }
        this.api.deleteLoanKind(id).subscribe({
          next: () => {
            this.rows.update((list) => list.filter((item) => item.id !== id));
            this.notice.set('Loan kind deleted.');
            this.saveError.set('');
          },
          error: (err: unknown) => {
            this.saveError.set(loanKindProblem(err));
          },
        });
      });
  }

  protected save(): void {
    this.saving.set(true);
    this.saveError.set('');
    this.notice.set('');
    const drafts = this.rows();
    const calls = drafts.map((row) => {
      const body = toBody(row);
      return row.id ? this.api.updateLoanKind(row.id, body) : this.api.createLoanKind(body);
    });
    const work: Observable<unknown> = calls.length === 0 ? of(null) : concat(...calls);
    work.subscribe({
      error: (err: unknown) => {
        this.saving.set(false);
        this.saveError.set(loanKindProblem(err));
      },
      complete: () => {
        this.saving.set(false);
        this.notice.set('Loan kinds saved.');
        this.load();
      },
    });
  }

  private patch(index: number, patch: Partial<KindDraft>): void {
    this.rows.update((list) => list.map((row, i) => (i === index ? { ...row, ...patch } : row)));
  }
}

function draftFrom(row: LoanKind): KindDraft {
  return {
    id: row.id,
    name: row.name,
    active: row.active,
    cutoffOnly: row.cutoffOrdinal != null,
    cutoffOrdinal: row.cutoffOrdinal ?? null,
  };
}

function toBody(row: KindDraft): SaveLoanKind {
  return {
    name: row.name.trim(),
    active: row.active,
    cutoffOrdinal: row.cutoffOnly ? row.cutoffOrdinal : null,
  };
}
