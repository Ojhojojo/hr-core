import { Component, computed, inject, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatTableModule } from '@angular/material/table';
import {
  ShopApiService,
  ShopInfo,
  apiErrorMessage,
  printPdfBlob,
  BannerComponent,
  CardComponent,
  PageHeaderComponent,
  SkeletonComponent,
} from '@hr-host';
import { basisLabel, manilaLockDate, money, payrollLockLine } from '../payroll/payroll-lock';
import {
  buildPayslipPdf,
  payslipAllowanceLines,
  payslipLoanLines,
  payslipPremiumLines,
  payslipViewFromApi,
} from '../payroll/payroll-payslip';
import { PayrollApi } from '../payroll/payroll.api';
import { MyPayslipDetail, MyPayslipSummary, Payslip } from '../payroll/payroll.models';
import { myPayslipDetailToPayslip } from './my-payslip.format';

/**
 * Worker self-service: a history of the caller's own finalised payslips. Every row comes from a
 * locked payroll run, newest first, and the detail shows the frozen line exactly as stored at
 * lock time, never recalculated.
 */
@Component({
  selector: 'app-ops-my-payslip',
  imports: [
    MatButton,
    MatTableModule,
    CardComponent,
    BannerComponent,
    PageHeaderComponent,
    SkeletonComponent,
  ],
  styles: `
    .ops-payslip-meta {
      color: var(--ops-muted, #6b7280);
      font-size: 0.85rem;
      margin: var(--space-1) 0 var(--space-3);
    }
    dl.ops-payslip div {
      display: flex;
      flex-wrap: wrap;
      justify-content: space-between;
      gap: var(--space-4);
      padding: var(--space-1) 0;
      border-bottom: 1px solid var(--ops-line, #e5e7eb);
    }
    dl.ops-payslip dt {
      font-weight: 500;
    }
    dl.ops-payslip .ops-payslip-total {
      border-bottom: none;
    }
    .ops-payslip-row {
      cursor: pointer;
    }
    .ops-payslip-row:hover {
      background: var(--ops-hover, #f3f4f6);
    }
    .ops-payslip-row-selected {
      background: var(--ops-selected, #eef2ff);
    }
    .ops-payslip-actions {
      margin-bottom: 0;
      margin-top: var(--space-4);
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2) var(--space-3);
    }
  `,
  template: `
    <ops-page-header title="My payslip" />

    <ops-card>
      @if (error()) {
        <ops-banner tone="danger" [message]="error()" />
      }

      @if (loading()) {
        <ops-skeleton [rows]="4" />
      } @else if (payslips().length === 0) {
        <p class="ops-empty">No payslips yet. They appear here once payroll is locked.</p>
      } @else {
        <table mat-table [dataSource]="payslips()" class="ops-table">
          <ng-container matColumnDef="period">
            <th mat-header-cell *matHeaderCellDef>Period</th>
            <td mat-cell *matCellDef="let row">{{ row.periodFrom }} → {{ row.periodTo }}</td>
          </ng-container>

          <ng-container matColumnDef="gross">
            <th mat-header-cell *matHeaderCellDef>Gross</th>
            <td mat-cell *matCellDef="let row">{{ money(row.gross) }}</td>
          </ng-container>

          <ng-container matColumnDef="net">
            <th mat-header-cell *matHeaderCellDef>Net</th>
            <td mat-cell *matCellDef="let row">
              <strong>{{ money(row.net) }}</strong>
            </td>
          </ng-container>

          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr
            mat-row
            *matRowDef="let row; columns: columns"
            class="ops-payslip-row"
            [class.ops-payslip-row-selected]="selectedDetail()?.lineId === row.lineId"
            (click)="select(row)"
          ></tr>
        </table>
      }
    </ops-card>

    @if (detailLoading()) {
      <ops-card>
        <ops-skeleton [rows]="4" />
      </ops-card>
    } @else if (selectedPayslip(); as slip) {
      <ops-card>
        <p class="ops-payslip-meta" data-payslip-period>
          {{ slip.from }} → {{ slip.to }}
          @if (slip.basis) {
            · {{ basisLabel(slip.basis) }}
            @if (slip.ratePhp) {
              at {{ money(slip.ratePhp) }}
            }
          }
          · Locked {{ manilaLockDate(slip.lockedAtUtc) }}
          @if (slip.lockedByName) {
            by {{ slip.lockedByName }}
          }
        </p>
        <dl class="ops-payslip" data-payslip-rows>
          <div>
            <dt>Regular hours</dt>
            <dd>{{ slip.regularHours }}</dd>
          </div>
          <div>
            <dt>OT hours</dt>
            <dd>{{ slip.otHours }}</dd>
          </div>
          <div>
            <dt>OT pay</dt>
            <dd>{{ money(slip.otPay) }}</dd>
          </div>
          <div>
            <dt>Days paid</dt>
            <dd>{{ slip.daysPaid }}</dd>
          </div>
          @for (line of premiumLines(slip); track line.label) {
            <div>
              <dt>{{ line.label }}</dt>
              <dd>{{ line.amount }}</dd>
            </div>
          }
          @for (line of allowanceLines(slip); track $index) {
            <div>
              <dt>{{ line.label }}</dt>
              <dd>{{ line.amount }}</dd>
            </div>
          }
          <div>
            <dt>Gross</dt>
            <dd>{{ money(slip.gross) }}</dd>
          </div>
          <div>
            <dt>SSS</dt>
            <dd>{{ money(slip.sss) }}</dd>
          </div>
          <div>
            <dt>PhilHealth</dt>
            <dd>{{ money(slip.philHealth) }}</dd>
          </div>
          <div>
            <dt>Pag-IBIG</dt>
            <dd>{{ money(slip.pagIbig) }}</dd>
          </div>
          <div>
            <dt>Withholding</dt>
            <dd>{{ money(slip.wht) }}</dd>
          </div>
          <div>
            <dt>Other</dt>
            <dd>{{ money(slip.other) }}</dd>
          </div>
          @for (line of loanLines(slip); track $index) {
            <div>
              <dt>{{ line.label }}</dt>
              <dd>{{ line.amount }}</dd>
            </div>
          }
          <div>
            <dt>Total deductions</dt>
            <dd>{{ money(slip.deductions) }}</dd>
          </div>
          <div class="ops-payslip-total">
            <dt>Net</dt>
            <dd>
              <strong>{{ money(slip.net) }}</strong>
            </dd>
          </div>
        </dl>
        <p class="ops-payslip-actions">
          <button
            matButton="outlined"
            type="button"
            name="printPayslip"
            [disabled]="printing()"
            (click)="print()"
          >
            {{ printing() ? 'Preparing…' : 'Print' }}
          </button>
        </p>
      </ops-card>
    }
  `,
})
export class MyPayslipPage {
  private readonly api = inject(PayrollApi);
  private readonly shopApi = inject(ShopApiService);

  protected readonly columns = ['period', 'gross', 'net'] as const;
  protected readonly payslips = signal<MyPayslipSummary[]>([]);
  protected readonly selectedDetail = signal<MyPayslipDetail | null>(null);
  protected readonly selectedPayslip = computed<Payslip | null>(() => {
    const detail = this.selectedDetail();
    return detail ? myPayslipDetailToPayslip(detail) : null;
  });
  protected readonly loading = signal(true);
  protected readonly detailLoading = signal(false);
  protected readonly printing = signal(false);
  protected readonly error = signal('');
  private shop: ShopInfo | null = null;

  protected readonly money = money;
  protected readonly basisLabel = basisLabel;
  protected readonly manilaLockDate = manilaLockDate;
  protected readonly premiumLines = payslipPremiumLines;
  protected readonly allowanceLines = payslipAllowanceLines;
  protected readonly loanLines = payslipLoanLines;

  constructor() {
    this.shopApi.getShop().subscribe({
      next: (shop) => {
        this.shop = shop;
      },
    });
    this.load();
  }

  /** The caller's own lines from locked runs only, newest first. */
  load(): void {
    this.error.set('');
    this.loading.set(true);
    this.selectedDetail.set(null);
    this.api.listMyPayslips().subscribe({
      next: (payslips) => {
        this.payslips.set(payslips);
        this.loading.set(false);
        const newest = payslips[0];
        if (newest) {
          this.select(newest);
        }
      },
      error: (err: unknown) => {
        this.payslips.set([]);
        this.error.set(apiErrorMessage(err));
        this.loading.set(false);
      },
    });
  }

  /** Shows the saved payslip exactly as stored at lock time. Not recalculated. */
  select(row: MyPayslipSummary): void {
    if (this.detailLoading() || this.selectedDetail()?.lineId === row.lineId) {
      return;
    }
    this.error.set('');
    this.detailLoading.set(true);
    this.selectedDetail.set(null);
    this.api.getMyPayslipLine(row.lineId).subscribe({
      next: (detail) => {
        this.selectedDetail.set(detail);
        this.detailLoading.set(false);
      },
      error: (err: unknown) => {
        this.selectedDetail.set(null);
        this.detailLoading.set(false);
        this.error.set(apiErrorMessage(err));
      },
    });
  }

  print(): void {
    const slip = this.selectedPayslip();
    if (!slip) {
      return;
    }
    this.error.set('');
    this.printing.set(true);
    const run = (shop: ShopInfo | null) => {
      try {
        const view = payslipViewFromApi(slip);
        const lockLine = payrollLockLine(slip.source, slip.lockedAtUtc, slip.lockedByName);
        const blob = buildPayslipPdf(shop, slip.from, slip.to, [view], lockLine || null);
        printPdfBlob(blob, 'Payslip');
      } catch {
        this.error.set('Could not build the PDF.');
      } finally {
        this.printing.set(false);
      }
    };
    if (this.shop) {
      run(this.shop);
      return;
    }
    this.shopApi.getShop().subscribe({
      next: (shop) => {
        this.shop = shop;
        run(shop);
      },
      error: (err: unknown) => {
        this.printing.set(false);
        this.error.set(apiErrorMessage(err));
      },
    });
  }
}
