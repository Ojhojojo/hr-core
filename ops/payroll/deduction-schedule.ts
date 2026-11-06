import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { apiErrorMessage, BannerComponent, SkeletonComponent } from '@hr-host';
import { MatButton } from '@angular/material/button';
import { ErrorStateMatcher } from '@angular/material/core';
import { MatError, MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import { PayrollApi } from './payroll.api';
import { firstFieldError, problemFieldErrors } from './payroll-cutoff.format';
import { DeductionScheduleRule } from './payroll.models';

const STATUTORY = ['sss', 'philhealth', 'pagibig', 'wht'] as const;

/** SSS, PhilHealth and Pag-IBIG match the register labels. wht uses its full name. */
const DEDUCTION_LABELS: Record<string, string> = {
  sss: 'SSS',
  philhealth: 'PhilHealth',
  pagibig: 'Pag-IBIG',
  wht: 'Withholding tax',
};

interface RuleDraft {
  deductionKey: string;
  mode: 'prorate' | 'cutoff' | 'split';
  cutoffOrdinal: number | null;
}

@Component({
  selector: 'app-ops-deduction-schedule',
  imports: [
    FormsModule,
    BannerComponent,
    SkeletonComponent,
    MatButton,
    MatError,
    MatFormField,
    MatLabel,
    MatInput,
    MatSelect,
    MatOption,
  ],
  templateUrl: './deduction-schedule.html',
  styles: `
    .deduction-rows {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      container-type: inline-size;
      min-width: 0;
      max-width: 100%;
    }
    .deduction-row {
      display: grid;
      grid-template-columns: minmax(7rem, 11rem) minmax(0, 1.4fr) minmax(0, 1fr);
      column-gap: var(--space-3);
      row-gap: var(--space-2);
      align-items: start;
      min-width: 0;
      max-width: 100%;
    }
    .deduction-label {
      display: flex;
      align-items: center;
      min-height: 56px;
      font-weight: 500;
    }
    .deduction-row mat-form-field {
      width: 100%;
      min-width: 0;
      max-width: 100%;
    }
    @container (max-width: 36rem) {
      .deduction-row {
        grid-template-columns: minmax(0, 1fr);
      }
      .deduction-label {
        min-height: 0;
      }
    }
    @media (max-width: 36rem) {
      .deduction-row {
        grid-template-columns: minmax(0, 1fr);
      }
      .deduction-label {
        min-height: 0;
      }
    }
  `,
})
export class DeductionSchedule {
  private readonly api = inject(PayrollApi);

  protected readonly loading = signal(false);
  protected readonly ready = signal(false);
  protected readonly loadError = signal('');
  protected readonly saveError = signal('');
  protected readonly notice = signal('');
  protected readonly saving = signal(false);
  protected readonly rules = signal<RuleDraft[]>(STATUTORY.map((key) => blank(key)));
  protected readonly fieldErrors = signal<Record<string, string[]>>({});

  protected fieldError(key: string): string {
    return firstFieldError(this.fieldErrors(), key);
  }

  protected label(key: string): string {
    const known = DEDUCTION_LABELS[key];
    if (known) {
      return known;
    }
    if (key.startsWith('loan:')) {
      const name = key.slice('loan:'.length).trim();
      return name.length > 0 ? name : 'Loan';
    }
    return key;
  }

  protected isLoanKey(key: string): boolean {
    return key.startsWith('loan:');
  }

  protected loanKindName(key: string): string {
    return key.startsWith('loan:') ? key.slice('loan:'.length) : key;
  }

  /**
   * MatInput only calls this from ngDoCheck when the field has an NgControl.
   * The template binds ngModel so a 400 can put the field into the error state.
   */
  protected matcher(index: number, field: 'cutoffOrdinal' | 'mode'): ErrorStateMatcher {
    return { isErrorState: () => this.fieldError(`rules[${index}].${field}`).length > 0 };
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set('');
    this.saveError.set('');
    this.notice.set('');
    this.fieldErrors.set({});
    this.api.getDeductionSchedule().subscribe({
      next: (body) => {
        const byKey = new Map((body.rules ?? []).map((rule) => [rule.deductionKey, rule]));
        const drafts = STATUTORY.map((key) => draftFrom(key, byKey.get(key)));
        for (const rule of body.rules ?? []) {
          if (!STATUTORY.includes(rule.deductionKey as (typeof STATUTORY)[number])) {
            drafts.push(draftFrom(rule.deductionKey, rule));
          }
        }
        this.rules.set(drafts);
        this.ready.set(true);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.ready.set(false);
        this.loading.set(false);
        this.loadError.set(apiErrorMessage(err) || 'Could not load the deduction schedule.');
      },
    });
  }

  protected setMode(index: number, mode: string): void {
    const next = mode === 'cutoff' || mode === 'split' ? mode : 'prorate';
    this.rules.update((rows) => rows.map((row, i) => (i === index ? { ...row, mode: next } : row)));
  }

  protected setOrdinal(index: number, value: number | string | null): void {
    const raw = value == null ? '' : String(value).trim();
    const parsed = raw === '' ? null : Number(raw);
    const ordinal = parsed != null && Number.isFinite(parsed) ? parsed : null;
    this.rules.update((rows) =>
      rows.map((row, i) => (i === index ? { ...row, cutoffOrdinal: ordinal } : row)),
    );
  }

  protected setLoanKind(index: number, event: Event): void {
    const name = (event.target as HTMLInputElement).value.trim();
    const deductionKey = name.length === 0 ? 'loan:' : `loan:${name}`;
    this.rules.update((rows) =>
      rows.map((row, i) => (i === index ? { ...row, deductionKey } : row)),
    );
  }

  protected addLoan(): void {
    this.rules.update((rows) => [...rows, blank('loan:')]);
  }

  protected save(): void {
    this.saving.set(true);
    this.saveError.set('');
    this.notice.set('');
    this.fieldErrors.set({});
    this.api
      .saveDeductionSchedule({
        rules: this.rules().map((row) => ({
          deductionKey: row.deductionKey.trim(),
          mode: row.mode,
          cutoffOrdinal: row.mode === 'cutoff' ? row.cutoffOrdinal : null,
        })),
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.notice.set('Deduction schedule saved.');
        },
        error: (err: unknown) => {
          this.saving.set(false);
          const fields = problemFieldErrors(err);
          this.fieldErrors.set(fields);
          this.saveError.set(
            Object.keys(fields).length > 0
              ? 'Some rules need a change.'
              : apiErrorMessage(err) || 'Could not save the deduction schedule.',
          );
        },
      });
  }
}

function blank(deductionKey: string): RuleDraft {
  return { deductionKey, mode: 'prorate', cutoffOrdinal: null };
}

function draftFrom(key: string, rule: DeductionScheduleRule | undefined): RuleDraft {
  if (!rule) {
    return blank(key);
  }
  const mode = rule.mode === 'cutoff' || rule.mode === 'split' ? rule.mode : 'prorate';
  return { deductionKey: key, mode, cutoffOrdinal: rule.cutoffOrdinal ?? null };
}
