import { Component, inject, output, signal } from '@angular/core';
import { apiErrorMessage, BannerComponent, SkeletonComponent } from '@hr-host';
import { MatButton } from '@angular/material/button';
import { MatButtonToggle, MatButtonToggleGroup } from '@angular/material/button-toggle';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { formatDayRange } from '../hr-dates';
import { PayrollApi } from './payroll.api';
import {
  firstFieldError,
  formatCutoffMonth,
  problemFieldErrors,
  shiftMonth,
} from './payroll-cutoff.format';
import { PayrollCutoffPeriod, PayrollCutoffSchedule } from './payroll.models';

interface CutoffDraft {
  startDay: number | null;
  endDay: number | null;
}

@Component({
  selector: 'app-ops-payroll-cutoff-settings',
  imports: [
    BannerComponent,
    SkeletonComponent,
    MatButton,
    MatButtonToggle,
    MatButtonToggleGroup,
    MatFormField,
    MatLabel,
    MatInput,
  ],
  templateUrl: './payroll-cutoff-settings.html',
  styles: `
    .ops-cutoff-settings {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
    }
    .ops-cutoff-row {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-start;
      gap: var(--space-2);
    }
    .ops-cutoff-row mat-form-field {
      width: 8rem;
    }
    .ops-cutoff-actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      align-items: center;
    }
    .ops-field-error {
      margin: 0;
      color: #9f1239;
      font-size: 0.8rem;
    }
    .ops-cutoff-preview {
      margin: 0;
      padding-left: 1.2rem;
    }
  `,
})
export class PayrollCutoffSettings {
  readonly saved = output<void>();
  protected readonly formatDayRange = formatDayRange;
  protected readonly formatCutoffMonth = formatCutoffMonth;
  private readonly api = inject(PayrollApi);

  protected readonly loading = signal(false);
  protected readonly ready = signal(false);
  protected readonly loadError = signal('');
  protected readonly saveError = signal('');
  protected readonly notice = signal('');
  protected readonly saving = signal(false);
  protected readonly mode = signal<'free' | 'schedule'>('free');
  protected readonly month = signal('');
  protected readonly periods = signal<PayrollCutoffPeriod[]>([]);
  protected readonly cutoffRows = signal<CutoffDraft[]>([]);
  protected readonly fieldErrors = signal<Record<string, string[]>>({});

  protected fieldError(key: string): string {
    return firstFieldError(this.fieldErrors(), key);
  }

  /** Loads the schedule. The payroll page calls this when the panel opens. */
  public load(): void {
    this.fetch(this.month(), true);
  }

  protected retry(): void {
    this.load();
  }

  /** Previews another month's dates without discarding unsaved day edits. */
  protected shiftPreview(delta: number): void {
    const next = shiftMonth(this.month() || '2000-01', delta);
    this.fetch(next, false);
  }

  protected setMode(value: string | null): void {
    const mode = value === 'schedule' ? 'schedule' : 'free';
    this.mode.set(mode);
    this.fieldErrors.set({});
    this.saveError.set('');
    if (mode === 'schedule' && this.cutoffRows().length === 0) {
      this.cutoffRows.set([{ startDay: null, endDay: null }]);
    }
  }

  protected setDay(index: number, key: 'startDay' | 'endDay', event: Event): void {
    const raw = (event.target as HTMLInputElement).value.trim();
    const parsed = raw === '' ? null : Number(raw);
    const day = parsed != null && Number.isFinite(parsed) ? parsed : null;
    this.cutoffRows.update((rows) =>
      rows.map((row, i) => (i === index ? { ...row, [key]: day } : row)),
    );
  }

  protected addRow(): void {
    if (this.cutoffRows().length >= 8) {
      return;
    }
    this.cutoffRows.update((rows) => [...rows, { startDay: null, endDay: null }]);
  }

  protected removeRow(index: number): void {
    this.cutoffRows.update((rows) => rows.filter((_, i) => i !== index));
  }

  protected save(): void {
    this.saving.set(true);
    this.saveError.set('');
    this.notice.set('');
    this.fieldErrors.set({});
    const body =
      this.mode() === 'free'
        ? { mode: 'free' as const, cutoffs: [] }
        : {
            mode: 'schedule' as const,
            cutoffs: this.cutoffRows().map((row, index) => ({
              ordinal: index + 1,
              startDay: row.startDay ?? 0,
              endDay: row.endDay ?? 0,
            })),
          };
    this.api.saveCutoffSchedule(body, this.month() || undefined).subscribe({
      next: (saved) => {
        this.apply(saved);
        this.saving.set(false);
        this.notice.set(
          saved.mode === 'schedule' ? 'Cut-off schedule saved.' : 'Free dates saved.',
        );
        this.saved.emit();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        const fields = problemFieldErrors(err);
        this.fieldErrors.set(fields);
        this.saveError.set(
          Object.keys(fields).length > 0
            ? 'Some cut-offs need a change.'
            : apiErrorMessage(err) || 'Could not save payroll cut-offs.',
        );
      },
    });
  }

  private fetch(month: string, replaceDraft: boolean): void {
    if (replaceDraft) {
      this.loading.set(true);
      this.loadError.set('');
      this.notice.set('');
      this.fieldErrors.set({});
    }
    this.saveError.set('');
    this.api.getCutoffSchedule(month || undefined).subscribe({
      next: (body) => {
        if (replaceDraft) {
          this.apply(body);
        } else {
          this.month.set(body.month || month);
          this.periods.set(body.periods ?? []);
        }
        this.ready.set(true);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        const message = apiErrorMessage(err) || 'Could not load payroll cut-offs.';
        if (replaceDraft) {
          this.ready.set(false);
          this.loadError.set(message);
        } else {
          this.saveError.set(message);
        }
      },
    });
  }

  private apply(body: PayrollCutoffSchedule): void {
    this.mode.set(body.mode === 'schedule' ? 'schedule' : 'free');
    this.month.set(body.month || this.month());
    this.periods.set(body.periods ?? []);
    this.cutoffRows.set(
      (body.cutoffs ?? []).map((row) => ({ startDay: row.startDay, endDay: row.endDay })),
    );
  }
}
