import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import { apiErrorMessage, formatPhp, manilaToday, downloadPdfBlob, printPdfBlob } from '@hr-host';
import { EmployeeCard, EmployeeDirectoryApi } from '../employee-directory/employee-directory.api';
import { Bir2316Sheet, PayrollApi } from '../payroll/payroll.api';
import {
  BIR_2316_ITEM_LABELS,
  BIR_2316_THIRTEENTH_NON_TAXABLE_CAP,
  Bir2316Computation,
  Bir2316HrInputs,
  buildBir2316Pdf,
  computeBir2316,
  emptyBir2316Inputs,
  missingLabels,
  monthNames,
} from './bir-2316';
import { formatDayRange } from '../hr-dates';

@Component({
  selector: 'app-bir-2316-dialog',
  imports: [
    FormsModule,
    MatDialogModule,
    MatButton,
    MatFormField,
    MatLabel,
    MatHint,
    MatInput,
    MatSelect,
    MatOption,
  ],
  encapsulation: ViewEncapsulation.None,
  templateUrl: './bir-2316-dialog.html',
  styles: `
    .bir-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: var(--space-1) var(--space-3);
    }

    .bir-span {
      grid-column: 1 / -1;
    }

    .bir-sheet-table {
      width: 100%;
      border-collapse: collapse;
      margin-top: var(--space-2);
    }

    .bir-sheet-table td {
      padding: var(--space-1) var(--space-2);
      border-bottom: 1px solid var(--ops-border, #e2e4ec);
      vertical-align: top;
    }

    .bir-sheet-table td.bir-amount {
      text-align: right;
      white-space: nowrap;
      font-variant-numeric: tabular-nums;
    }

    .bir-sheet-table tr.bir-total td {
      font-weight: 600;
    }

    .bir-banner {
      margin: var(--space-2) 0;
      padding: var(--space-2) var(--space-3);
      border-radius: 6px;
      background: var(--ops-warn-bg, #fff7e0);
      border: 1px solid var(--ops-warn-border, #e8c46a);
      font-size: 0.9rem;
    }

    .bir-banner p {
      margin: var(--space-1) 0 0;
    }

    .bir-section-title {
      margin: var(--space-4) 0 var(--space-1);
      font-size: 1rem;
    }

    .bir-muted {
      color: var(--ops-muted, #5b6178);
      font-size: 0.85rem;
    }

    .bir-actions {
      flex-wrap: wrap;
      gap: var(--space-2);
    }

    @media (max-width: 700px) {
      .bir-grid {
        grid-template-columns: 1fr;
      }
    }
  `,
})
export class Bir2316Dialog {
  protected readonly formatDayRange = formatDayRange;
  private readonly data = inject<{ staffId?: string | null } | null>(MAT_DIALOG_DATA, {
    optional: true,
  });
  private readonly ref = inject(MatDialogRef<Bir2316Dialog, boolean>);
  private readonly directory = inject(EmployeeDirectoryApi);
  private readonly payroll = inject(PayrollApi);

  protected readonly labels = BIR_2316_ITEM_LABELS;
  protected readonly capLabel = formatPhp(BIR_2316_THIRTEENTH_NON_TAXABLE_CAP);
  protected readonly years = this.yearOptions();
  protected readonly people = signal<EmployeeCard[]>([]);
  protected readonly loadingPeople = signal(true);
  protected readonly loadingSheet = signal(false);
  protected readonly error = signal('');
  protected readonly sheet = signal<Bir2316Sheet | null>(null);

  protected staffId = this.data?.staffId ?? '';
  protected year = this.years[0];
  protected inputs: Bir2316HrInputs = emptyBir2316Inputs();

  constructor() {
    this.directory.list(true).subscribe({
      next: (body) => {
        this.people.set(body.items ?? []);
        this.loadingPeople.set(false);
      },
      error: (err: unknown) => {
        this.error.set(apiErrorMessage(err));
        this.loadingPeople.set(false);
      },
    });
    if (this.staffId) {
      this.load();
    }
  }

  protected load(): void {
    if (!this.staffId) {
      return;
    }
    this.loadingSheet.set(true);
    this.error.set('');
    this.payroll.getBir2316(this.staffId, this.year).subscribe({
      next: (sheet) => {
        this.sheet.set(sheet);
        this.loadingSheet.set(false);
      },
      error: (err: unknown) => {
        this.sheet.set(null);
        this.error.set(apiErrorMessage(err));
        this.loadingSheet.set(false);
      },
    });
  }

  /** Recomputed on every change so the sheet always matches the inputs above it. */
  protected computation(): Bir2316Computation | null {
    const sheet = this.sheet();
    return sheet ? computeBir2316(sheet.amounts, this.inputs) : null;
  }

  protected missingText(): string[] {
    const sheet = this.sheet();
    return sheet ? missingLabels(sheet.missing) : [];
  }

  protected monthText(): string {
    const sheet = this.sheet();
    return sheet ? monthNames(sheet.missingMonths).join(', ') : '';
  }

  protected php(amount: number): string {
    return formatPhp(amount);
  }

  protected print(): void {
    const sheet = this.sheet();
    const computation = this.computation();
    if (!sheet || !computation) {
      return;
    }
    this.error.set('');
    try {
      printPdfBlob(buildBir2316Pdf(sheet, computation), `BIR 2316 ${sheet.year}`);
    } catch {
      this.error.set('Could not build the PDF.');
    }
  }

  protected download(): void {
    const sheet = this.sheet();
    const computation = this.computation();
    if (!sheet || !computation) {
      return;
    }
    this.error.set('');
    try {
      const slug =
        sheet.employee.name
          .trim()
          .toLowerCase()
          .replace(/[^a-z0-9]+/g, '-')
          .replace(/^-|-$/g, '') || 'staff';
      downloadPdfBlob(buildBir2316Pdf(sheet, computation), `bir-2316-${sheet.year}-${slug}.pdf`);
    } catch {
      this.error.set('Could not build the PDF.');
    }
  }

  protected close(): void {
    this.ref.close(false);
  }

  private yearOptions(): number[] {
    const current = Number(manilaToday().slice(0, 4));
    return [current, current - 1, current - 2, current - 3, current - 4];
  }
}
