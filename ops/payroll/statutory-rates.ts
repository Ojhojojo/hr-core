import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { apiErrorMessage, BannerComponent, OpsConfirmService, SkeletonComponent } from '@hr-host';
import { MatButton } from '@angular/material/button';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import { concat, Observable, of } from 'rxjs';
import { PayrollApi } from './payroll.api';
import {
  SaveStatutoryRate,
  StatutoryRate,
  StatutoryRateKind,
  StatutoryTaxBracket,
} from './payroll.models';

interface RateDraft {
  id: string | null;
  kind: StatutoryRateKind;
  effectiveFrom: string;
  mscFloor: number | null;
  mscCeiling: number | null;
  mscStep: number | null;
  employeeRate: number | null;
  employerRate: number | null;
  ecLow: number | null;
  ecHigh: number | null;
  ecMscCeilingForLow: number | null;
  floor: number | null;
  ceiling: number | null;
  smallSalary: number | null;
  rateSmall: number | null;
  rate: number | null;
  cap: number | null;
  exemptMonthly: number | null;
  brackets: BracketDraft[];
}

interface BracketDraft {
  upTo: number | null;
  baseTax: number | null;
  rate: number | null;
  excessOver: number | null;
}

/** Text from a problem-details body, then the host's error helper. */
export function statutoryRateProblem(err: unknown): string {
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
  return apiErrorMessage(err) || 'Could not save the statutory rate.';
}

@Component({
  selector: 'app-ops-statutory-rates',
  imports: [
    FormsModule,
    BannerComponent,
    SkeletonComponent,
    MatButton,
    MatFormField,
    MatLabel,
    MatInput,
    MatSelect,
    MatOption,
  ],
  templateUrl: './statutory-rates.html',
  styles: `
    .statutory-rate-rows {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
      min-width: 0;
    }
    .statutory-rate-row {
      display: grid;
      grid-template-columns: minmax(0, 8rem) minmax(0, 9rem) repeat(auto-fit, minmax(7rem, 1fr)) auto;
      column-gap: var(--space-3);
      row-gap: var(--space-2);
      align-items: start;
      min-width: 0;
    }
    .statutory-rate-row mat-form-field {
      width: 100%;
      min-width: 0;
    }
    .statutory-brackets {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      grid-column: 1 / -1;
    }
    .statutory-bracket-row {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr)) auto;
      column-gap: var(--space-2);
      align-items: start;
    }
    @media (max-width: 40rem) {
      .statutory-rate-row,
      .statutory-bracket-row {
        grid-template-columns: minmax(0, 1fr);
      }
    }
  `,
})
export class StatutoryRateSettings {
  private readonly api = inject(PayrollApi);
  private readonly confirm = inject(OpsConfirmService);

  protected readonly loading = signal(false);
  protected readonly ready = signal(false);
  protected readonly loadError = signal('');
  protected readonly saveError = signal('');
  protected readonly notice = signal('');
  protected readonly saving = signal(false);
  protected readonly rows = signal<RateDraft[]>([]);

  load(): void {
    this.loading.set(true);
    this.loadError.set('');
    this.saveError.set('');
    this.notice.set('');
    this.api.listStatutoryRates().subscribe({
      next: (body) => {
        this.rows.set((body.rates ?? []).map(draftFrom));
        this.ready.set(true);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.ready.set(false);
        this.loading.set(false);
        this.loadError.set(statutoryRateProblem(err) || 'Could not load statutory rates.');
      },
    });
  }

  protected addRate(): void {
    this.rows.update((list) => [...list, emptyDraft()]);
  }

  protected setKind(index: number, value: StatutoryRateKind): void {
    this.patch(index, { kind: value, brackets: value === 'wht' ? this.rows()[index]?.brackets ?? [] : [] });
  }

  protected setDate(index: number, value: string): void {
    this.patch(index, { effectiveFrom: value });
  }

  protected setNumber(index: number, field: NumericField, value: number | string | null): void {
    this.patch(index, { [field]: parseNumber(value) } as Partial<RateDraft>);
  }

  protected addBracket(index: number): void {
    const row = this.rows()[index];
    if (!row) {
      return;
    }
    this.patch(index, {
      brackets: [...row.brackets, { upTo: null, baseTax: 0, rate: 0, excessOver: 0 }],
    });
  }

  protected setBracket(
    index: number,
    bracketIndex: number,
    field: keyof BracketDraft,
    value: number | string | null,
  ): void {
    const row = this.rows()[index];
    if (!row) {
      return;
    }
    this.patch(index, {
      brackets: row.brackets.map((bracket, i) =>
        i === bracketIndex ? { ...bracket, [field]: parseNumber(value) } : bracket,
      ),
    });
  }

  protected removeBracket(index: number, bracketIndex: number): void {
    const row = this.rows()[index];
    if (!row) {
      return;
    }
    this.patch(index, { brackets: row.brackets.filter((_, i) => i !== bracketIndex) });
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
        title: 'Delete statutory rate',
        message: `Delete the ${row.kind} row from ${row.effectiveFrom}? Months that used it will fall back to an earlier row, or need amounts entered by hand.`,
        confirmLabel: 'Delete',
      })
      .subscribe((result) => {
        if (!result.confirmed) {
          return;
        }
        this.api.deleteStatutoryRate(id).subscribe({
          next: () => {
            this.rows.update((list) => list.filter((item) => item.id !== id));
            this.notice.set('Statutory rate deleted.');
            this.saveError.set('');
          },
          error: (err: unknown) => {
            this.saveError.set(statutoryRateProblem(err));
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
      return row.id ? this.api.updateStatutoryRate(row.id, body) : this.api.createStatutoryRate(body);
    });
    const work: Observable<unknown> = calls.length === 0 ? of(null) : concat(...calls);
    work.subscribe({
      error: (err: unknown) => {
        this.saving.set(false);
        this.saveError.set(statutoryRateProblem(err));
      },
      complete: () => {
        this.saving.set(false);
        this.notice.set('Statutory rates saved.');
        this.load();
      },
    });
  }

  private patch(index: number, patch: Partial<RateDraft>): void {
    this.rows.update((list) => list.map((row, i) => (i === index ? { ...row, ...patch } : row)));
  }
}

type NumericField =
  | 'mscFloor'
  | 'mscCeiling'
  | 'mscStep'
  | 'employeeRate'
  | 'employerRate'
  | 'ecLow'
  | 'ecHigh'
  | 'ecMscCeilingForLow'
  | 'floor'
  | 'ceiling'
  | 'smallSalary'
  | 'rateSmall'
  | 'rate'
  | 'cap'
  | 'exemptMonthly';

function emptyDraft(): RateDraft {
  return {
    id: null,
    kind: 'sss',
    effectiveFrom: '',
    mscFloor: 5_000,
    mscCeiling: 35_000,
    mscStep: 500,
    employeeRate: 0.05,
    employerRate: 0.1,
    ecLow: 10,
    ecHigh: 30,
    ecMscCeilingForLow: 14_500,
    floor: null,
    ceiling: null,
    smallSalary: null,
    rateSmall: null,
    rate: null,
    cap: null,
    exemptMonthly: null,
    brackets: [],
  };
}

function draftFrom(row: StatutoryRate): RateDraft {
  return {
    id: row.id,
    kind: row.kind,
    effectiveFrom: row.effectiveFrom,
    mscFloor: row.mscFloor ?? null,
    mscCeiling: row.mscCeiling ?? null,
    mscStep: row.mscStep ?? null,
    employeeRate: row.employeeRate ?? null,
    employerRate: row.employerRate ?? null,
    ecLow: row.ecLow ?? null,
    ecHigh: row.ecHigh ?? null,
    ecMscCeilingForLow: row.ecMscCeilingForLow ?? null,
    floor: row.floor ?? null,
    ceiling: row.ceiling ?? null,
    smallSalary: row.smallSalary ?? null,
    rateSmall: row.rateSmall ?? null,
    rate: row.rate ?? null,
    cap: row.cap ?? null,
    exemptMonthly: row.exemptMonthly ?? null,
    brackets: (row.brackets ?? []).map(bracketFrom),
  };
}

function bracketFrom(row: StatutoryTaxBracket): BracketDraft {
  return {
    upTo: row.upTo,
    baseTax: row.baseTax,
    rate: row.rate,
    excessOver: row.excessOver,
  };
}

function toBody(row: RateDraft): SaveStatutoryRate {
  const body: SaveStatutoryRate = {
    kind: row.kind,
    effectiveFrom: row.effectiveFrom.trim(),
  };
  if (row.kind === 'sss') {
    body.mscFloor = row.mscFloor;
    body.mscCeiling = row.mscCeiling;
    body.mscStep = row.mscStep;
    body.employeeRate = row.employeeRate;
    body.employerRate = row.employerRate;
    body.ecLow = row.ecLow;
    body.ecHigh = row.ecHigh;
    body.ecMscCeilingForLow = row.ecMscCeilingForLow;
  } else if (row.kind === 'philhealth') {
    body.floor = row.floor;
    body.ceiling = row.ceiling;
    body.employeeRate = row.employeeRate;
    body.employerRate = row.employerRate;
  } else if (row.kind === 'pagibig') {
    body.smallSalary = row.smallSalary;
    body.rateSmall = row.rateSmall;
    body.rate = row.rate;
    body.cap = row.cap;
    body.employerRate = row.employerRate;
  } else {
    body.exemptMonthly = row.exemptMonthly;
    body.brackets = row.brackets.map((bracket, index) => ({
      upTo: bracket.upTo,
      baseTax: bracket.baseTax ?? 0,
      rate: bracket.rate ?? 0,
      excessOver: bracket.excessOver ?? 0,
      sortOrder: index + 1,
    }));
  }
  return body;
}

function parseNumber(value: number | string | null): number | null {
  const raw = value == null ? '' : String(value).trim();
  if (raw === '') {
    return null;
  }
  const parsed = Number(raw);
  return Number.isFinite(parsed) ? parsed : null;
}
