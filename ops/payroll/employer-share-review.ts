import { Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { apiErrorMessage, BannerComponent, formatPhp } from '@hr-host';
import { MatButton } from '@angular/material/button';
import { MatTableModule } from '@angular/material/table';
import { PayrollApi } from './payroll.api';
import { ConfirmEmployerShareLine, EmployerShareReview } from './payroll.models';

@Component({
  selector: 'app-ops-employer-share-review',
  imports: [FormsModule, BannerComponent, MatButton, MatTableModule],
  templateUrl: './employer-share-review.html',
  styles: `
    .share-review {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
      min-width: 0;
    }
    .share-review-table {
      overflow-x: auto;
    }
    input[name='employerShare'] {
      width: 7rem;
    }
  `,
})
export class EmployerShareReviewPanel {
  private readonly api = inject(PayrollApi);

  readonly runId = input.required<string>();

  protected readonly formatPhp = formatPhp;
  protected readonly columns = ['name', 'sss', 'philhealth', 'pagibig'];
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly review = signal<EmployerShareReview | null>(null);
  protected readonly drafts = signal<Record<string, ConfirmEmployerShareLine>>({});

  constructor() {
    effect(() => {
      const runId = this.runId();
      if (runId) {
        this.load(runId);
      }
    });
  }

  protected draft(staffId: string, field: keyof ConfirmEmployerShareLine): number {
    return Number(this.drafts()[staffId]?.[field] ?? 0);
  }

  protected setDraft(
    staffId: string,
    field: 'employerSss' | 'employerPhilHealth' | 'employerPagIbig',
    value: number | null,
  ): void {
    const current = this.drafts()[staffId];
    if (!current || this.review()?.readOnly) {
      return;
    }
    this.drafts.update((rows) => ({
      ...rows,
      [staffId]: { ...current, [field]: Number(value ?? 0) },
    }));
  }

  protected confirm(): void {
    const review = this.review();
    if (!review || review.readOnly || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.error.set('');
    this.api.confirmEmployerShares(review.runId, Object.values(this.drafts())).subscribe({
      next: (body) => {
        this.saving.set(false);
        this.show(body);
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.error.set(apiErrorMessage(err) || 'Could not confirm the employer shares.');
      },
    });
  }

  private load(runId: string): void {
    this.loading.set(true);
    this.error.set('');
    this.api.getEmployerShareReview(runId).subscribe({
      next: (body) => {
        this.loading.set(false);
        this.show(body);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.review.set(null);
        this.error.set(apiErrorMessage(err) || 'Could not load employer shares.');
      },
    });
  }

  private show(body: EmployerShareReview): void {
    this.review.set(body);
    const drafts: Record<string, ConfirmEmployerShareLine> = {};
    for (const line of body.lines) {
      drafts[line.staffId] = {
        staffId: line.staffId,
        employerSss: line.employerSss ?? line.suggestedSss,
        employerPhilHealth: line.employerPhilHealth ?? line.suggestedPhilHealth,
        employerPagIbig: line.employerPagIbig ?? line.suggestedPagIbig,
      };
    }
    this.drafts.set(drafts);
  }
}
