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
import { formatDayRange } from '../hr-dates';
import {
  type RemittanceReport as RemittanceReportBody,
  type RemittanceRunRef,
} from './payroll.models';
import { HttpErrorResponse } from '@angular/common/http';

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
  protected readonly formatRange = formatDayRange;
  protected readonly blockingRuns = signal<RemittanceRunRef[]>([]);
  protected readonly exportError = signal('');
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
    this.exportError.set('');
    this.blockingRuns.set([]);
    this.api.exportRemittance(report.month).subscribe({
      next: (response) => {
        const blob = response.body;
        if (!blob) {
          return;
        }
        const fileName = `payroll-remittance-${report.month}.csv`;
        if (typeof window !== 'undefined' && typeof window.URL?.createObjectURL === 'function') {
          const url = window.URL.createObjectURL(blob);
          const link = document.createElement('a');
          link.href = url;
          link.download = fileName;
          link.click();
          setTimeout(() => window.URL.revokeObjectURL(url), 60_000);
        }
      },
      error: (err: unknown) => {
        void this.showExportError(err);
      },
    });
  }

  private async showExportError(err: unknown): Promise<void> {
    const body = await exportErrorBody(err);
    this.exportError.set(body.error || 'Could not export the remittance report.');
    this.blockingRuns.set(body.blockingRuns ?? []);
  }

  private show(body: RemittanceReportBody): void {
    this.report.set(body);
    this.enabled.set(body.enabled);
    this.month = body.month;
    this.ready.set(true);
    this.loading.set(false);
  }
}

async function exportErrorBody(
  err: unknown,
): Promise<{ error?: string; blockingRuns?: RemittanceRunRef[] }> {
  if (!(err instanceof HttpErrorResponse)) {
    return {};
  }
  const payload = err.error;
  if (payload instanceof Blob) {
    const text = await blobText(payload);
    return text ? (JSON.parse(text) as { error?: string; blockingRuns?: RemittanceRunRef[] }) : {};
  }
  if (typeof payload === 'string' && payload) {
    return JSON.parse(payload) as { error?: string; blockingRuns?: RemittanceRunRef[] };
  }
  return (payload ?? {}) as { error?: string; blockingRuns?: RemittanceRunRef[] };
}

function blobText(blob: Blob): Promise<string> {
  if (typeof blob.text === 'function') {
    return blob.text();
  }
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () => reject(reader.error);
    reader.readAsText(blob);
  });
}
