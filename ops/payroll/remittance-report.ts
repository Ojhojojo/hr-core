import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  apiErrorMessage,
  BannerComponent,
  EmptyStateComponent,
  formatPhp,
  SkeletonComponent,
} from '@hr-host';
import { MatButton } from '@angular/material/button';
import { MatSlideToggle } from '@angular/material/slide-toggle';
import { MatTableModule } from '@angular/material/table';
import { PayrollApi } from './payroll.api';
import { formatCutoffMonth, shiftMonth } from './payroll-cutoff.format';
import { type RemittanceReport as RemittanceReportBody } from './payroll.models';
import { remittanceCsv } from './remittance-report.format';

@Component({
  selector: 'app-ops-remittance-report',
  imports: [
    FormsModule,
    BannerComponent,
    EmptyStateComponent,
    SkeletonComponent,
    MatButton,
    MatSlideToggle,
    MatTableModule,
  ],
  templateUrl: './remittance-report.html',
  styles: `
    .remittance-report {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
      min-width: 0;
    }
    .remittance-actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2);
    }
    .remittance-table {
      overflow-x: auto;
    }
  `,
})
export class RemittanceReport {
  private readonly api = inject(PayrollApi);

  protected readonly formatMonth = formatCutoffMonth;
  protected readonly formatPhp = formatPhp;
  protected readonly columns = ['agency', 'employee', 'employer', 'total'];
  protected readonly loading = signal(false);
  protected readonly ready = signal(false);
  protected readonly saving = signal(false);
  protected readonly loadError = signal('');
  protected readonly saveError = signal('');
  protected readonly report = signal<RemittanceReportBody | null>(null);
  protected readonly enabled = signal(false);
  private month = '';

  load(): void {
    this.loading.set(true);
    this.loadError.set('');
    this.saveError.set('');
    this.api.getRemittance(this.month || undefined).subscribe({
      next: (body) => this.show(body),
      error: (err: unknown) => {
        this.ready.set(false);
        this.loading.set(false);
        this.loadError.set(apiErrorMessage(err) || 'Could not load the remittance report.');
      },
    });
  }

  protected shift(delta: number): void {
    const current = this.report()?.month;
    if (!current) {
      return;
    }
    this.month = shiftMonth(current, delta);
    this.load();
  }

  protected setEnabled(value: boolean): void {
    if (value === this.enabled() || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.saveError.set('');
    this.api.saveRemittance({ enabled: value }).subscribe({
      next: () => {
        this.saving.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.saveError.set(apiErrorMessage(err) || 'Could not save the remittance setting.');
      },
    });
  }

  protected exportCsv(): void {
    const report = this.report();
    if (!report?.enabled || report.emptyMessage) {
      return;
    }
    const csv = remittanceCsv(report);
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' });
    const fileName = `payroll-remittance-${report.month}.csv`;
    if (typeof window !== 'undefined' && typeof window.URL?.createObjectURL === 'function') {
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = fileName;
      link.click();
      setTimeout(() => window.URL.revokeObjectURL(url), 60_000);
    }
  }

  private show(body: RemittanceReportBody): void {
    this.report.set(body);
    this.enabled.set(body.enabled);
    this.month = body.month;
    this.ready.set(true);
    this.loading.set(false);
  }
}
