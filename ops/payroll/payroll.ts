import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { forkJoin, of } from 'rxjs';
import { catchError, map } from 'rxjs';
import {
  apiErrorMessage,
  manilaToday,
  roundMoney,
  OpsBreakpoints,
  fitDialogToHandset,
  ShopApiService,
  AuthService,
  BannerComponent,
  CardComponent,
  EmptyStateComponent,
  OpsConfirmService,
  OpsToastService,
  InfoTipComponent,
  PageHeaderComponent,
  PeriodNavComponent,
  PeriodRange,
  SkeletonComponent,
  StatusBadgeComponent,
} from '@hr-host';
import { manilaWeekRange } from '../attendance/attendance';
import { MatDialog } from '@angular/material/dialog';
import { PayrollApi } from './payroll.api';
import { AllowanceOverrideDialog } from './payroll-allowance-dialog';
import { rowHasAllowances } from './payroll-allowances.format';
import { LoanOverrideDialog } from './payroll-loan-dialog';
import { loanItemLabel, loanOverrideKey, rowHasLoans } from './payroll-loans.format';
import {
  HolidayDialog,
  HolidayDialogResult,
  PremiumOverrideDialog,
} from './payroll-premium-dialog';
import { DayPayOverrideDialog } from './payroll-day-override-dialog';
import {
  canEditPremiumSettings,
  premiumLabel,
  rowHasPremium,
  sumPremiums,
} from './payroll-premiums.format';
import {
  PayrollDeductionDraft,
  PayrollStaffRow,
  ShopHoliday,
  StatutorySuggestion,
  ThirteenthMonthRow,
  UpdatePayrollPremiumSettings,
  UnpaidBonus,
} from './payroll.models';
import { PayslipDialog, buildPayslipView, hasPayRate } from './payroll-payslip';
import { ThirteenthSummaryDialog } from './payroll-thirteenth-print';
import { PayrollRateDialog } from './payroll-rate-dialog';
import { PayrollFinalPayDialog } from './payroll-final-pay-dialog';
import {
  csvTextWithLockLine,
  manilaLockDate,
  openPunchWarning,
  payrollLockLine,
  rateSliceLabels,
} from './payroll-lock';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatButtonToggle, MatButtonToggleGroup } from '@angular/material/button-toggle';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatCheckbox } from '@angular/material/checkbox';
import { MatMenu, MatMenuItem, MatMenuTrigger } from '@angular/material/menu';
import { MatOption, MatSelect } from '@angular/material/select';
import { MatSlideToggle } from '@angular/material/slide-toggle';
import { MatTableModule } from '@angular/material/table';
import { MatTooltip } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-ops-payroll',
  imports: [
    InfoTipComponent,
    MatButton,
    MatIconButton,
    MatButtonToggle,
    MatButtonToggleGroup,
    MatExpansionModule,
    MatFormField,
    MatLabel,
    MatInput,
    MatCheckbox,
    MatMenu,
    MatMenuItem,
    MatMenuTrigger,
    MatSelect,
    MatOption,
    MatSlideToggle,
    MatTableModule,
    MatTooltip,
    FormsModule,
    RouterLink,
    PageHeaderComponent,
    CardComponent,
    BannerComponent,
    EmptyStateComponent,
    SkeletonComponent,
    PeriodNavComponent,
    StatusBadgeComponent,
  ],
  templateUrl: './payroll.html',
  styles: `
    .ops-payroll-toolbar {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-4);
    }
    .ops-payroll-toolbar-controls {
      display: flex;
      align-items: center;
      gap: var(--space-4);
      flex-wrap: wrap;
    }
    .ops-payroll-total {
      padding: var(--space-3) var(--space-4);
      margin: 0;
    }
    .thirteenth-heading {
      margin-top: 0;
    }
    .thirteenth-total-msg {
      padding: var(--space-3) var(--space-4);
    }
    .ops-statutory-toggle-wrap {
      display: inline-flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2);
      max-width: 100%;
    }
    .ops-statutory-label {
      font-size: 0.85rem;
      font-weight: 500;
      color: var(--ops-muted, #5b6178);
    }
    .ops-statutory-toggle {
      height: 36px;
    }
    .ops-payroll-slices {
      margin: var(--space-1) 0 0;
      padding-left: var(--space-4);
      font-size: 0.8rem;
      color: var(--ops-muted, #5b6178);
    }
    .ops-premium-panel {
      margin: var(--space-2) 0 var(--space-4);
    }
    .ops-premium-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr));
      gap: var(--space-2) var(--space-4);
      align-items: start;
    }
    .ops-premium-grid mat-form-field,
    .ops-premium-actions {
      width: 100%;
    }
    .ops-premium-actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      align-items: center;
      margin: var(--space-2) 0 var(--space-4);
    }
    .ops-premium-hint {
      margin: 0 0 var(--space-3);
      color: var(--ops-muted, #5b6178);
      font-size: 0.85rem;
    }
    .ops-premium-toggles {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-3) var(--space-6);
      margin-bottom: var(--space-2);
    }
    .ops-name-cell {
      display: inline-flex;
      align-items: center;
      gap: var(--space-1);
      min-width: 0;
      overflow-wrap: anywhere;
    }
    .ops-row-warn {
      display: inline-flex;
      color: #b45309;
      cursor: help;
    }
    .ops-premium-edited {
      margin-left: var(--space-1);
      font-size: 0.75rem;
      font-weight: 600;
      color: var(--ops-muted, #5b6178);
    }
    .ops-loan-item {
      display: block;
      margin-top: var(--space-1);
      font-size: 0.75rem;
      color: var(--ops-muted, #5b6178);
      overflow-wrap: anywhere;
    }
    .ops-payroll-actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-1);
    }
    .ops-payroll-detail-row > td {
      border-top: 0;
      padding-top: var(--space-1);
      padding-bottom: var(--space-3);
    }
    .ops-payroll-deductions {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      gap: var(--space-2) var(--space-3);
    }
    .ops-payroll-deductions .ops-table-input {
      width: 7.5rem;
      max-width: 100%;
    }
    .ops-issued-line {
      display: inline-block;
      font-size: 0.8rem;
      color: var(--ops-muted, #5b6178);
      align-self: center;
    }
    /* Sticky first column ("Staff name"): opaque background so scrolled columns slide under it
       instead of showing through and overlapping the name. */
    .ops-payroll-table .mat-mdc-table-sticky {
      position: sticky;
      left: 0;
      z-index: 1;
      background: var(--mat-sys-surface, var(--ops-card, #fff));
      box-shadow: 2px 0 4px rgba(40, 48, 64, 0.08);
    }
    .ops-payroll-table th.mat-mdc-table-sticky {
      z-index: 2;
    }
    /* The name column keeps a usable width and names wrap inside it. Tables stack into cards
       below 900px (see shell/ops-theme.scss), so this only applies when the table is a real table. */
    @media (min-width: 900.02px) {
      .ops-payroll-table .mat-column-name {
        min-width: 11rem;
      }
    }
  `,
})
export class PayrollPage {
  private readonly api = inject(PayrollApi);
  private readonly dialog = inject(MatDialog);
  private readonly breakpoints = inject(OpsBreakpoints);
  private readonly shopApi = inject(ShopApiService);
  private readonly auth = inject(AuthService);
  private readonly confirm = inject(OpsConfirmService);
  private readonly toast = inject(OpsToastService);

  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly includeInactive = signal(false);
  protected readonly statutorySchedule = signal<'semi_monthly' | 'full_month'>('semi_monthly');
  protected readonly rows = signal<PayrollStaffRow[]>([]);
  protected readonly pendingOtCount = signal(0);
  protected readonly unpaidBonuses = signal<UnpaidBonus[]>([]);
  protected readonly unpaidBonusWarning = computed(() => {
    const rows = this.unpaidBonuses();
    const names = rows.map((row) => `${row.staffName} ${this.formatMoney(row.amount)}`).join(', ');
    return `${rows.length} sales ${rows.length === 1 ? 'bonus' : 'bonuses'} not yet paid, period ended: ${names}`;
  });
  protected readonly pendingOtWarning = computed(() => {
    const count = this.pendingOtCount();
    return `${count} OT ${count === 1 ? 'claim' : 'claims'} pending approval`;
  });
  protected readonly drafts = signal<Record<string, PayrollDeductionDraft>>({});
  protected readonly savingId = signal<string | null>(null);
  protected readonly savingAll = signal(false);
  protected readonly loading = signal(true);
  protected readonly downloading = signal(false);
  protected readonly error = signal('');
  protected readonly source = signal<'live' | 'locked'>('live');
  protected readonly runId = signal<string | null>(null);
  protected readonly lockedAtUtc = signal<string | null>(null);
  protected readonly lockedByName = signal<string | null>(null);
  protected readonly locking = signal(false);
  protected readonly voiding = signal(false);
  protected readonly lockBranchChoice = signal<string | null>(null);
  protected readonly issuedAt = signal<Record<string, string>>({});
  protected readonly suggestingId = signal<string | null>(null);
  protected readonly suggestingAll = signal(false);
  protected readonly suggestNotes = signal<string[]>([]);
  protected readonly suggestError = signal('');
  protected readonly thirteenthYear = signal(Number(manilaToday().slice(0, 4)));
  protected readonly thirteenthRows = signal<ThirteenthMonthRow[]>([]);
  protected readonly thirteenthDrafts = signal<Record<string, { amount: number | null }>>({});
  protected readonly thirteenthLoading = signal(true);
  protected readonly thirteenthDownloading = signal(false);
  protected readonly thirteenthSavingId = signal<string | null>(null);
  protected readonly thirteenthError = signal('');
  protected readonly thirteenthColumns = [
    'name',
    'role',
    'branch',
    'hireDate',
    'basis',
    'rate',
    'basicAuto',
    'override',
    'basic',
    'thirteenth',
    'actions',
  ];

  protected readonly isOwner = computed(() => this.auth.me()?.staffRole === 'owner');
  protected readonly canEditPremiums = computed(() =>
    canEditPremiumSettings(this.auth.me()?.staffRole),
  );
  protected readonly holidayYear = signal(Number(manilaToday().slice(0, 4)));
  protected readonly holidays = signal<ShopHoliday[]>([]);
  protected readonly settingsDraft = signal<UpdatePayrollPremiumSettings | null>(null);
  protected readonly premiumsLoading = signal(false);
  protected readonly premiumsSaving = signal(false);
  protected readonly holidayBusy = signal(false);
  protected readonly premiumsError = signal('');
  protected readonly premiumsNotice = signal('');
  protected readonly premiumLabel = premiumLabel;
  protected readonly showPremiumColumns = computed(() =>
    this.rows().some((row) => rowHasPremium(row)),
  );
  protected readonly premiumTotal = computed(() => sumPremiums(this.rows()));
  protected readonly showAllowanceColumn = computed(() =>
    this.rows().some((row) => rowHasAllowances(row)),
  );
  protected readonly allowanceTotal = computed(() =>
    roundMoney(
      this.rows().reduce(
        (sum, row) => sum + (row.allowances == null ? 0 : Number(row.allowances) || 0),
        0,
      ),
    ),
  );
  protected readonly showLoanColumn = computed(() => this.rows().some((row) => rowHasLoans(row)));
  protected readonly loanTotal = computed(() =>
    roundMoney(
      this.rows().reduce(
        (sum, row) => sum + (row.loanDeductions == null ? 0 : Number(row.loanDeductions) || 0),
        0,
      ),
    ),
  );
  protected readonly loanItemLabel = loanItemLabel;

  protected readonly isLocked = computed(() => this.source() === 'locked');

  /** "Locked 7 Oct 2026 by Owner Apple", empty while the period is live. */
  protected readonly lockLine = computed(() =>
    payrollLockLine(this.source(), this.lockedAtUtc(), this.lockedByName()),
  );

  protected readonly lockStatusLabel = computed(() => this.lockLine() || 'Not locked');

  /** Shops on this register, for the lock target when the viewer has no home branch (HQ owner). */
  protected readonly branchOptions = computed(() => {
    const options: { id: string; name: string }[] = [];
    for (const row of this.rows()) {
      if (!row.branchId || options.some((option) => option.id === row.branchId)) {
        continue;
      }
      options.push({ id: row.branchId, name: row.branchName || row.branchId });
    }
    return options;
  });

  /** True when the viewer must pick the shop to lock because the register spans several shops. */
  protected readonly needsShopChoice = computed(
    () => !this.auth.me()?.branchId && this.branchOptions().length > 1,
  );

  /** Shop a lock would freeze: the chosen one, the home branch, or the only shop on the register. */
  protected readonly lockBranchId = computed(() => {
    const chosen = this.lockBranchChoice();
    if (chosen) {
      return chosen;
    }
    const home = this.auth.me()?.branchId;
    if (home) {
      return home;
    }
    const options = this.branchOptions();
    return options.length === 1 ? options[0].id : null;
  });

  protected readonly lockBranchName = computed(() => {
    const id = this.lockBranchId();
    return this.branchOptions().find((option) => option.id === id)?.name ?? id ?? '';
  });

  protected readonly showShopColumn = computed(() => {
    const set = new Set(
      this.rows()
        .map((r) => r.branchName || r.branchId)
        .filter(Boolean),
    );
    return set.size > 1;
  });

  protected readonly mainColumns = computed(() => {
    const columns = ['name', 'role'];
    if (this.showShopColumn()) {
      columns.push('shop');
    }
    columns.push('basis', 'rate', 'regularHours', 'otHours', 'otPay', 'daysPaid');
    if (this.showPremiumColumns()) {
      columns.push('holidayHours', 'restDayHours', 'nightHours', 'premiumPay');
    }
    if (this.showAllowanceColumn()) {
      columns.push('allowances');
    }
    if (this.showLoanColumn()) {
      columns.push('loans');
    }
    columns.push('amount', 'notes', 'deductions', 'net', 'actions');
    return columns;
  });

  protected readonly detailColumns = ['deductionDetail'];

  protected readonly totalAmount = computed(() => {
    return roundMoney(this.rows().reduce((sum, row) => sum + (Number(row.amount) || 0), 0));
  });

  protected readonly ratedRows = computed(() =>
    this.rows().filter((row) => hasPayRate(row) && row.active !== false),
  );

  protected readonly rateAdvisory = computed(() =>
    this.rows().some(
      (row) =>
        row.active !== false && String(row.role).toLowerCase() !== 'owner' && !hasPayRate(row),
    ),
  );

  protected readonly thirteenthTotal = computed(() =>
    roundMoney(
      this.thirteenthRows().reduce((sum, row) => sum + (Number(row.thirteenthMonth) || 0), 0),
    ),
  );

  constructor() {
    const range = manilaWeekRange();
    this.from.set(range.from);
    this.to.set(range.to);
    this.load();
    this.loadThirteenth();
  }

  load(): void {
    this.loading.set(true);
    this.error.set('');
    this.api
      .getPayroll({
        from: this.from() || undefined,
        to: this.to() || undefined,
        includeInactive: this.includeInactive(),
      })
      .subscribe({
        next: (body) => {
          if (body.from) {
            this.from.set(body.from);
          }
          if (body.to) {
            this.to.set(body.to);
          }
          const nextRows = body.rows ?? [];
          this.rows.set(nextRows);
          this.source.set(body.source === 'locked' ? 'locked' : 'live');
          this.runId.set(body.runId ?? null);
          this.lockedAtUtc.set(body.lockedAtUtc ?? null);
          this.lockedByName.set(body.lockedByName ?? null);
          this.pendingOtCount.set(body.pendingOvertimeClaimsCount ?? 0);
          this.unpaidBonuses.set(body.unpaidBonuses ?? []);
          this.rememberDrafts(nextRows);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.error.set(apiErrorMessage(err));
          this.loading.set(false);
        },
      });
  }

  onPeriodChange(range: PeriodRange): void {
    this.from.set(range.from);
    this.to.set(range.to);
    this.statutorySchedule.set(this.isSingleMonth() ? 'full_month' : 'semi_monthly');
    this.load();
  }

  onIncludeInactiveToggle(checked: boolean): void {
    this.includeInactive.set(checked);
    this.load();
  }

  /** Freezes the period for one shop after the operator confirms the totals. */
  lockPeriod(): void {
    const branchId = this.lockBranchId();
    if (!branchId || !this.from() || !this.to()) {
      this.error.set('Pick a shop and a period before locking.');
      return;
    }

    // Warn about open punches first. If the lookup fails, the lock still goes ahead without the warning.
    this.api
      .openPunches(this.from(), this.to())
      .pipe(catchError(() => of([])))
      .subscribe((openRows) => {
        const warning = openPunchWarning(openRows.map((row) => row.staffName));
        this.openPunchNames.set(openRows.map((row) => row.staffName));
        this.confirmLock(branchId, warning);
      });
  }

  /** Names with an open punch in the period, shown with a link to Attendance. Empty when none. */
  protected readonly openPunchNames = signal<string[]>([]);

  private confirmLock(branchId: string, openWarning: string): void {
    const warningLine = openWarning ? ` ${openWarning}` : '';
    this.confirm
      .confirm({
        title: 'Lock period',
        message:
          `Lock ${this.lockBranchName()} for ${this.from()} to ${this.to()}? ` +
          `Total gross ${this.formatMoney(this.totalAmount())}, deductions ${this.formatMoney(this.totalDeductions())}, ` +
          `net ${this.formatMoney(this.totalNet())}. Locking freezes this period; later corrections go in the next period.${warningLine}`,
        confirmLabel: 'Lock period',
      })
      .subscribe((result) => {
        if (!result.confirmed) {
          return;
        }
        this.locking.set(true);
        this.error.set('');
        this.api.lockRun({ branchId, from: this.from(), to: this.to(), note: null }).subscribe({
          next: () => {
            this.locking.set(false);
            this.openPunchNames.set([]);
            this.load();
          },
          error: (err: unknown) => {
            this.error.set(apiErrorMessage(err));
            this.locking.set(false);
          },
        });
      });
  }

  /** Owner-only: voids the locked run and puts the period back to live. */
  voidRun(): void {
    const id = this.runId();
    if (!id || !this.isOwner()) {
      return;
    }

    this.confirm
      .confirm({
        title: 'Void payroll run',
        message:
          `Void the locked run for ${this.from()} to ${this.to()}? ` +
          'The frozen rows stay for the audit trail and the period becomes live again.',
        confirmLabel: 'Void run',
        tone: 'danger',
      })
      .subscribe((result) => {
        if (!result.confirmed) {
          return;
        }
        this.voiding.set(true);
        this.error.set('');
        this.api.voidRun(id).subscribe({
          next: () => {
            this.voiding.set(false);
            this.load();
          },
          error: (err: unknown) => {
            this.error.set(apiErrorMessage(err));
            this.voiding.set(false);
          },
        });
      });
  }

  /** Rate history and a dated rate change for one worker. Reloads the register after a change. */
  openRate(row: PayrollStaffRow): void {
    if (this.isLocked() || !this.isOwner()) {
      return;
    }
    this.error.set('');
    const ref = this.dialog.open(
      PayrollRateDialog,
      fitDialogToHandset(
        {
          width: 'min(48rem, 78vw)',
          maxWidth: 'calc(100vw - 32px)',
          data: {
            staffId: row.staffId,
            staffName: row.staffName,
            rateSlices: row.rateSlices ?? null,
          },
        },
        this.breakpoints.isHandset(),
      ),
    );
    ref.afterClosed().subscribe((changed) => {
      if (changed) {
        this.toast.success('Rate saved.');
        this.load();
      }
    });
  }

  issuedLine(row: PayrollStaffRow): string {
    const at = this.issuedAt()[row.staffId];
    return at ? `Issued ${manilaLockDate(at)}` : '';
  }

  /**
   * Prefills SSS / PhilHealth / Pag-IBIG / WHT from the statutory pack for the open period.
   * The API prorates when from/to are sent. The semi-monthly toggle scales only a month-only
   * response (one that has no fraction).
   */
  suggestStatutory(row: PayrollStaffRow): void {
    const month = (this.from() || '').slice(0, 7);
    if (!/^\d{4}-\d{2}$/.test(month)) {
      this.suggestError.set('Set a period start date first.');
      return;
    }

    this.suggestError.set('');
    this.suggestNotes.set([]);
    this.suggestingId.set(row.staffId);
    this.api.getStatutorySuggested(row.staffId, month, this.statutoryPeriod()).subscribe({
      next: (suggestion) => {
        this.suggestingId.set(null);
        const factor = this.statutoryFactor(suggestion);

        this.setDraft(row.staffId, 'sss', roundMoney(suggestion.sss * factor));
        this.setDraft(row.staffId, 'philHealth', roundMoney(suggestion.philHealth * factor));
        this.setDraft(row.staffId, 'pagIbig', roundMoney(suggestion.pagIbig * factor));
        this.setDraft(row.staffId, 'wht', roundMoney(suggestion.wht * factor));

        const notes = [...suggestion.notes];
        if (typeof suggestion.fraction !== 'number') {
          const isSemi = this.statutorySchedule() === 'semi_monthly';
          if (isSemi) {
            notes.unshift(
              `Suggested at semi-monthly rate (50% of monthly: SSS ${this.formatMoney(suggestion.sss)}, PhilHealth ${this.formatMoney(suggestion.philHealth)}, Pag-IBIG ${this.formatMoney(suggestion.pagIbig)}, WHT ${this.formatMoney(suggestion.wht)}).`,
            );
          } else {
            notes.unshift(
              `Suggested at full monthly rate (SSS ${this.formatMoney(suggestion.sss)}, PhilHealth ${this.formatMoney(suggestion.philHealth)}, Pag-IBIG ${this.formatMoney(suggestion.pagIbig)}, WHT ${this.formatMoney(suggestion.wht)}).`,
            );
          }
        }
        this.suggestNotes.set(notes);
      },
      error: (err: unknown) => {
        this.suggestingId.set(null);
        this.suggestError.set(apiErrorMessage(err));
      },
    });
  }

  suggestAllStatutory(): void {
    const month = (this.from() || '').slice(0, 7);
    if (!/^\d{4}-\d{2}$/.test(month)) {
      this.suggestError.set('Set a period start date first.');
      return;
    }

    const targetRows = this.ratedRows();
    if (targetRows.length === 0) {
      return;
    }

    this.suggestError.set('');
    this.suggestNotes.set([]);
    this.suggestingAll.set(true);

    const period = this.statutoryPeriod();
    const calls = targetRows.map((row) =>
      this.api.getStatutorySuggested(row.staffId, month, period).pipe(
        map((suggestion) => ({ row, suggestion })),
        catchError(() => of(null)),
      ),
    );

    forkJoin(calls).subscribe({
      next: (results) => {
        this.suggestingAll.set(false);
        let count = 0;
        let prorated = false;
        for (const res of results) {
          if (!res) continue;
          const { row, suggestion } = res;
          const factor = this.statutoryFactor(suggestion);
          if (typeof suggestion.fraction === 'number') {
            prorated = true;
          }
          this.setDraft(row.staffId, 'sss', roundMoney(suggestion.sss * factor));
          this.setDraft(row.staffId, 'philHealth', roundMoney(suggestion.philHealth * factor));
          this.setDraft(row.staffId, 'pagIbig', roundMoney(suggestion.pagIbig * factor));
          this.setDraft(row.staffId, 'wht', roundMoney(suggestion.wht * factor));
          count++;
        }
        const isSemi = this.statutorySchedule() === 'semi_monthly';
        this.suggestNotes.set([
          prorated
            ? `Prefilled statutory suggestions for ${count} staff member(s). Review amounts and save.`
            : isSemi
              ? `Prefilled semi-monthly statutory suggestions (50%) for ${count} staff member(s). Review amounts and save.`
              : `Prefilled full monthly statutory suggestions for ${count} staff member(s). Review amounts and save.`,
        ]);
      },
      error: (err: unknown) => {
        this.suggestingAll.set(false);
        this.suggestError.set(apiErrorMessage(err));
      },
    });
  }

  saveAllDeductions(): void {
    if (this.isLocked() || !this.from() || !this.to()) {
      return;
    }

    const targetRows = this.ratedRows();
    if (targetRows.length === 0) {
      return;
    }

    this.confirm
      .confirm({
        title: `Save deductions for all ${targetRows.length} staff?`,
        message:
          'Every row on this period is saved with the values shown, replacing what was saved before.',
        confirmLabel: 'Save all',
      })
      .subscribe((result) => {
        if (result.confirmed) {
          this.saveAllDeductionsNow(targetRows);
        }
      });
  }

  private saveAllDeductionsNow(targetRows: PayrollStaffRow[]): void {
    this.savingAll.set(true);
    this.error.set('');

    const calls = targetRows.map((row) => {
      const draft = this.drafts()[row.staffId];
      if (!draft) return of(null);
      return this.api
        .saveDeductions(
          {
            staffId: row.staffId,
            from: this.from(),
            to: this.to(),
            sss: draft.sss,
            philHealth: draft.philHealth,
            pagIbig: draft.pagIbig,
            wht: draft.wht,
            other: draft.other,
          },
          { includeInactive: this.includeInactive() },
        )
        .pipe(catchError(() => of(null)));
    });

    forkJoin(calls).subscribe({
      next: (results) => {
        this.savingAll.set(false);
        const validResults = results.filter((r): r is NonNullable<typeof r> => r !== null);
        const byStaffId = new Map(validResults.map((s) => [s.staffId, s]));
        this.rows.update((list) =>
          list.map((item) => {
            const saved = byStaffId.get(item.staffId);
            if (!saved) return item;
            return {
              ...item,
              sss: saved.sss,
              philHealth: saved.philHealth,
              pagIbig: saved.pagIbig,
              wht: saved.wht,
              other: saved.other,
              deductions: roundMoney(saved.deductions + loanAmountOf(item)),
              net: roundMoney((Number(item.amount) || 0) - saved.deductions - loanAmountOf(item)),
            };
          }),
        );
      },
      error: (err: unknown) => {
        this.savingAll.set(false);
        this.error.set(apiErrorMessage(err));
      },
    });
  }

  /** Final pay estimate for a separated worker. */
  openFinalPay(row: PayrollStaffRow): void {
    this.error.set('');
    this.dialog.open(
      PayrollFinalPayDialog,
      fitDialogToHandset(
        {
          width: 'min(48rem, 78vw)',
          maxWidth: 'calc(100vw - 32px)',
          data: { staffId: row.staffId, staffName: row.staffName },
        },
        this.breakpoints.isHandset(),
      ),
    );
  }

  /** Register period sent to statutory-suggested. Both dates, or nothing. */
  private statutoryPeriod(): { from: string; to: string } | undefined {
    const from = this.from();
    const to = this.to();
    if (!/^\d{4}-\d{2}-\d{2}$/.test(from) || !/^\d{4}-\d{2}-\d{2}$/.test(to)) {
      return undefined;
    }
    return { from, to };
  }

  /** A period-aware pack response is already the period's share. Scale month-only responses only. */
  private statutoryFactor(suggestion: StatutorySuggestion): number {
    if (typeof suggestion.fraction === 'number') {
      return 1;
    }
    return this.statutorySchedule() === 'semi_monthly' ? 0.5 : 1;
  }

  private isSingleMonth(): boolean {
    const from = this.from();
    const to = this.to();
    if (!/^\d{4}-\d{2}-01$/.test(from) || !/^\d{4}-\d{2}-\d{2}$/.test(to)) {
      return false;
    }

    if (from.slice(0, 7) !== to.slice(0, 7)) {
      return false;
    }

    const [year, month] = from.split('-').map(Number);
    return Number(to.slice(8, 10)) === new Date(Date.UTC(year, month, 0)).getUTCDate();
  }

  setFrom(event: Event): void {
    this.from.set((event.target as HTMLInputElement).value);
  }

  setTo(event: Event): void {
    this.to.set((event.target as HTMLInputElement).value);
  }

  draftAmount(staffId: string, field: keyof PayrollDeductionDraft): number {
    return this.drafts()[staffId]?.[field] ?? 0;
  }

  setDraft(staffId: string, field: keyof PayrollDeductionDraft, value: number | null): void {
    const num = typeof value === 'number' && Number.isFinite(value) ? value : 0;
    this.drafts.update((map) => {
      const current = map[staffId] ?? { sss: 0, philHealth: 0, pagIbig: 0, wht: 0, other: 0 };
      return {
        ...map,
        [staffId]: {
          ...current,
          [field]: num,
        },
      };
    });
  }

  rowDeductions(staffId: string): number {
    const draft = this.drafts()[staffId];
    const statutory = draft
      ? draft.sss + draft.philHealth + draft.pagIbig + draft.wht + draft.other
      : 0;
    const loan = this.rows().find((row) => row.staffId === staffId)?.loanDeductions;
    const loanAmount = loan == null ? 0 : Number(loan) || 0;
    return roundMoney(statutory + loanAmount);
  }

  rowNet(row: PayrollStaffRow): number {
    return roundMoney((Number(row.amount) || 0) - this.rowDeductions(row.staffId));
  }

  totalDeductions(): number {
    return roundMoney(this.rows().reduce((sum, row) => sum + this.rowDeductions(row.staffId), 0));
  }

  totalNet(): number {
    return roundMoney(this.rows().reduce((sum, row) => sum + this.rowNet(row), 0));
  }

  saveRow(row: PayrollStaffRow): void {
    const draft = this.drafts()[row.staffId];
    if (!draft || !this.from() || !this.to() || this.isLocked()) {
      return;
    }
    this.savingId.set(row.staffId);
    this.error.set('');
    this.api
      .saveDeductions(
        {
          staffId: row.staffId,
          from: this.from(),
          to: this.to(),
          sss: draft.sss,
          philHealth: draft.philHealth,
          pagIbig: draft.pagIbig,
          wht: draft.wht,
          other: draft.other,
        },
        {
          includeInactive: this.includeInactive(),
        },
      )
      .subscribe({
        next: (saved) => {
          draft.sss = saved.sss;
          draft.philHealth = saved.philHealth;
          draft.pagIbig = saved.pagIbig;
          draft.wht = saved.wht;
          draft.other = saved.other;
          this.rows.update((list) =>
            list.map((item) =>
              item.staffId === saved.staffId
                ? {
                    ...item,
                    sss: saved.sss,
                    philHealth: saved.philHealth,
                    pagIbig: saved.pagIbig,
                    wht: saved.wht,
                    other: saved.other,
                    deductions: roundMoney(saved.deductions + loanAmountOf(item)),
                    net: roundMoney(
                      (Number(item.amount) || 0) - saved.deductions - loanAmountOf(item),
                    ),
                  }
                : item,
            ),
          );
          this.savingId.set(null);
        },
        error: (err: unknown) => {
          this.error.set(apiErrorMessage(err));
          this.savingId.set(null);
        },
      });
  }

  formatMoney(amount: number | null | undefined): string {
    const val = Number(amount ?? 0);
    return `₱${val.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
  }

  formatBasis(row: PayrollStaffRow): string {
    const b = row.basis ?? row.payBasis;
    if (!b) {
      return '—';
    }
    if (b === 'monthly') {
      return 'Monthly';
    }
    return b.charAt(0).toUpperCase() + b.slice(1);
  }

  formatRate(row: PayrollStaffRow): string {
    const r = row.ratePhp ?? row.rate;
    if (r == null) {
      return 'No rate';
    }
    return this.formatMoney(r);
  }

  /** Readable rate spans when the period was priced at more than one rate. */
  rateSlices(row: PayrollStaffRow): string[] {
    return rateSliceLabels(row.rateSlices);
  }

  formatHours(h: number | null | undefined): string {
    if (h == null) {
      return '0.00';
    }
    return Number(h).toFixed(2);
  }

  private rememberDrafts(rows: PayrollStaffRow[]): void {
    const next: Record<string, PayrollDeductionDraft> = {};
    for (const row of rows) {
      next[row.staffId] = {
        sss: rememberAmount(row.sss),
        philHealth: rememberAmount(row.philHealth),
        pagIbig: rememberAmount(row.pagIbig),
        wht: rememberAmount(row.wht),
        other: rememberAmount(row.other),
      };
    }
    this.drafts.set(next);
  }

  openPayslip(row: PayrollStaffRow): void {
    if (!this.from() || !this.to()) {
      this.error.set('Choose a period before opening a payslip.');
      return;
    }
    this.shopApi.getShop().subscribe({
      next: (shop) => {
        const ref = this.dialog.open(
          PayslipDialog,
          fitDialogToHandset(
            {
              width: 'min(48rem, 78vw)',
              data: {
                shop,
                periodFrom: this.from(),
                periodTo: this.to(),
                lockLine: this.lockLine(),
                slips: [buildPayslipView(row, this.drafts()[row.staffId])],
                issue: {
                  staffId: row.staffId,
                  issuedLine: this.issuedLine(row),
                },
              },
            },
            this.breakpoints.isHandset(),
          ),
        );
        ref.afterClosed().subscribe((result: { issuedAtUtc?: string } | undefined) => {
          if (!result?.issuedAtUtc) {
            return;
          }
          this.toast.success('Payslip issued.');
          this.issuedAt.update((map) => ({
            ...map,
            [row.staffId]: result.issuedAtUtc ?? '',
          }));
        });
      },
      error: (err: unknown) => this.error.set(apiErrorMessage(err)),
    });
  }

  openAllPayslips(): void {
    const slips = this.ratedRows().map((row) => buildPayslipView(row, this.drafts()[row.staffId]));
    if (slips.length === 0) {
      return;
    }
    this.shopApi.getShop().subscribe({
      next: (shop) => {
        this.dialog.open(
          PayslipDialog,
          fitDialogToHandset(
            {
              width: 'min(48rem, 78vw)',
              data: {
                shop,
                periodFrom: this.from(),
                periodTo: this.to(),
                lockLine: this.lockLine(),
                slips,
              },
            },
            this.breakpoints.isHandset(),
          ),
        );
      },
      error: (err: unknown) => this.error.set(apiErrorMessage(err)),
    });
  }

  exportCsv(): void {
    this.downloading.set(true);
    this.error.set('');
    const lockLine = this.lockLine();
    this.api
      .exportCsv({
        from: this.from() || undefined,
        to: this.to() || undefined,
        includeInactive: this.includeInactive(),
      })
      .subscribe({
        next: (response) => {
          const blob = response.body;
          if (!blob) {
            this.error.set('Empty CSV.');
            this.downloading.set(false);
            return;
          }

          const fileName =
            parseFileName(response.headers.get('content-disposition')) ??
            `rakkii-payroll-${this.from() || manilaToday()}-${this.to() || manilaToday()}.csv`;
          void this.saveCsv(blob, fileName, lockLine);
        },
        error: (err: unknown) => {
          this.error.set(apiErrorMessage(err));
          this.downloading.set(false);
        },
      });
  }

  /** Adds the lock line to a locked register before the download so the file says the period is frozen. */
  private async saveCsv(blob: Blob, fileName: string, lockLine: string): Promise<void> {
    let file = blob;
    try {
      file = await csvTextWithLockLine(blob, lockLine);
    } catch {
      file = blob;
    }

    if (typeof window !== 'undefined' && typeof window.URL?.createObjectURL === 'function') {
      const url = window.URL.createObjectURL(file);
      const link = document.createElement('a');
      link.href = url;
      link.download = fileName;
      link.click();
      setTimeout(() => window.URL.revokeObjectURL(url), 60_000);
    }
    this.downloading.set(false);
  }

  loadThirteenth(): void {
    const year = this.thirteenthYear();
    if (!Number.isInteger(year) || year < 2000 || year > 2100) {
      this.thirteenthError.set('Year must be from 2000 through 2100.');
      this.thirteenthLoading.set(false);
      return;
    }

    this.thirteenthLoading.set(true);
    this.thirteenthError.set('');
    this.api.getThirteenth(year).subscribe({
      next: (body) => {
        const rows = body.rows ?? [];
        this.thirteenthRows.set(rows);
        this.rememberThirteenthDrafts(rows);
        if (body.year) {
          this.thirteenthYear.set(body.year);
        }
        this.thirteenthLoading.set(false);
      },
      error: (err: unknown) => {
        this.thirteenthError.set(apiErrorMessage(err));
        this.thirteenthLoading.set(false);
      },
    });
  }

  setThirteenthYear(event: Event): void {
    const raw = (event.target as HTMLInputElement).value;
    this.thirteenthYear.set(raw.trim() === '' ? Number.NaN : Number(raw));
  }

  thirteenthDraftAmount(staffId: string): number | null {
    return this.thirteenthDrafts()[staffId]?.amount ?? null;
  }

  setThirteenthDraft(staffId: string, value: number | null): void {
    const draft = this.thirteenthDrafts()[staffId];
    if (!draft) {
      return;
    }
    draft.amount = typeof value === 'number' && Number.isFinite(value) ? value : null;
  }

  canSaveThirteenth(staffId: string): boolean {
    const amount = this.thirteenthDraftAmount(staffId);
    return amount != null && amount >= 0 && this.thirteenthSavingId() !== staffId;
  }

  saveThirteenth(row: ThirteenthMonthRow): void {
    const amount = this.thirteenthDraftAmount(row.staffId);
    const year = this.thirteenthYear();
    if (amount == null || amount < 0 || !Number.isInteger(year)) {
      this.thirteenthError.set('Override must be zero or more.');
      return;
    }

    this.thirteenthSavingId.set(row.staffId);
    this.thirteenthError.set('');
    this.api
      .saveThirteenthOverride({
        staffId: row.staffId,
        year,
        basicEarnedOverride: amount,
      })
      .subscribe({
        next: (saved) => {
          this.replaceThirteenthRow(saved);
          this.thirteenthSavingId.set(null);
        },
        error: (err: unknown) => {
          this.thirteenthError.set(apiErrorMessage(err));
          this.thirteenthSavingId.set(null);
        },
      });
  }

  clearThirteenth(row: ThirteenthMonthRow): void {
    const year = this.thirteenthYear();
    if (!Number.isInteger(year)) {
      return;
    }

    this.confirm
      .confirm({
        title: `Clear the 13th-month override for ${row.staffName}?`,
        message: `The ${year} amount goes back to the computed value.`,
        confirmLabel: 'Clear override',
        tone: 'danger',
      })
      .subscribe((result) => {
        if (result.confirmed) {
          this.clearThirteenthNow(row, year);
        }
      });
  }

  private clearThirteenthNow(row: ThirteenthMonthRow, year: number): void {
    this.thirteenthSavingId.set(row.staffId);
    this.thirteenthError.set('');
    this.api.clearThirteenthOverride(row.staffId, year).subscribe({
      next: (saved) => {
        this.replaceThirteenthRow(saved);
        this.thirteenthSavingId.set(null);
      },
      error: (err: unknown) => {
        this.thirteenthError.set(apiErrorMessage(err));
        this.thirteenthSavingId.set(null);
      },
    });
  }

  printThirteenth(): void {
    const rows = this.thirteenthRows();
    if (rows.length === 0) {
      return;
    }

    this.shopApi.getShop().subscribe({
      next: (shop) => {
        this.dialog.open(
          ThirteenthSummaryDialog,
          fitDialogToHandset(
            {
              width: 'min(64rem, 78vw)',
              data: { shop, year: this.thirteenthYear(), rows, lockLine: this.lockLine() },
            },
            this.breakpoints.isHandset(),
          ),
        );
      },
      error: (err: unknown) => this.thirteenthError.set(apiErrorMessage(err)),
    });
  }

  exportThirteenth(): void {
    const year = this.thirteenthYear();
    if (!Number.isInteger(year)) {
      this.thirteenthError.set('Year must be from 2000 through 2100.');
      return;
    }

    this.thirteenthDownloading.set(true);
    this.thirteenthError.set('');
    this.api.exportThirteenth(year).subscribe({
      next: (response) => {
        const blob = response.body;
        if (!blob) {
          this.thirteenthError.set('Empty CSV.');
          this.thirteenthDownloading.set(false);
          return;
        }

        const fileName =
          parseFileName(response.headers.get('content-disposition')) ??
          `rakkii-13th-month-${year}.csv`;
        if (typeof window !== 'undefined' && typeof window.URL?.createObjectURL === 'function') {
          const url = window.URL.createObjectURL(blob);
          const link = document.createElement('a');
          link.href = url;
          link.download = fileName;
          link.click();
          setTimeout(() => window.URL.revokeObjectURL(url), 60_000);
        }
        this.thirteenthDownloading.set(false);
      },
      error: (err: unknown) => {
        this.thirteenthError.set(apiErrorMessage(err));
        this.thirteenthDownloading.set(false);
      },
    });
  }

  formatPayBasis(basis: string | null | undefined): string {
    if (!basis) {
      return '—';
    }
    if (basis === 'monthly') {
      return 'Monthly';
    }
    return basis.charAt(0).toUpperCase() + basis.slice(1);
  }

  formatRole(role: string | null | undefined): string {
    if (!role) {
      return '—';
    }
    return role.charAt(0).toUpperCase() + role.slice(1);
  }

  private rememberThirteenthDrafts(rows: ThirteenthMonthRow[]): void {
    const next: Record<string, { amount: number | null }> = {};
    for (const row of rows) {
      next[row.staffId] = { amount: row.basicEarnedOverride ?? null };
    }
    this.thirteenthDrafts.set(next);
  }

  loadPremiums(): void {
    if (this.premiumsLoading()) {
      return;
    }
    this.premiumsLoading.set(true);
    this.premiumsError.set('');
    forkJoin({
      settings: this.api.getSettings(),
      holidays: this.api.listHolidays(this.holidayYear()),
    }).subscribe({
      next: ({ settings, holidays }) => {
        this.settingsDraft.set(toSettingsDraft(settings));
        this.holidays.set(holidays.holidays ?? []);
        if (holidays.year) {
          this.holidayYear.set(holidays.year);
        }
        this.premiumsLoading.set(false);
      },
      error: (err: unknown) => {
        this.premiumsError.set(apiErrorMessage(err));
        this.premiumsLoading.set(false);
      },
    });
  }

  setHolidayYear(event: Event): void {
    const year = Number((event.target as HTMLInputElement).value);
    if (!Number.isInteger(year) || year < 2000 || year > 2100) {
      return;
    }
    this.holidayYear.set(year);
    this.reloadHolidays();
  }

  savePremiumSettings(): void {
    const draft = this.settingsDraft();
    if (!draft || !this.canEditPremiums()) {
      return;
    }
    const percents = [
      draft.regularHolidayPercent,
      draft.specialDayPercent,
      draft.restDayPercent,
      draft.nightDiffPercent,
      draft.otPremiumPercent,
    ];
    if (
      percents.some(
        (value) => !Number.isFinite(Number(value)) || Number(value) < 0 || Number(value) > 1000,
      )
    ) {
      this.premiumsError.set('Percents must be from 0 through 1000.');
      return;
    }
    this.premiumsSaving.set(true);
    this.premiumsError.set('');
    this.premiumsNotice.set('');
    this.api
      .saveSettings({
        ...draft,
        regularHolidayPercent: Number(draft.regularHolidayPercent),
        specialDayPercent: Number(draft.specialDayPercent),
        restDayPercent: Number(draft.restDayPercent),
        nightDiffPercent: Number(draft.nightDiffPercent),
        nightStart: toApiTime(draft.nightStart),
        nightEnd: toApiTime(draft.nightEnd),
        shiftEnd: toApiTime(draft.shiftEnd),
        otGraceMinutes: Number(draft.otGraceMinutes),
        adminsCanApproveOt: draft.adminsCanApproveOt,
        otPremiumPercent: Number(draft.otPremiumPercent),
        otMonthlyDayDivisor: Number(draft.otMonthlyDayDivisor),
      })
      .subscribe({
        next: (saved) => {
          this.settingsDraft.set(toSettingsDraft(saved));
          this.premiumsSaving.set(false);
          this.premiumsNotice.set('Premium settings saved.');
        },
        error: (err: unknown) => {
          this.premiumsError.set(apiErrorMessage(err));
          this.premiumsSaving.set(false);
        },
      });
  }

  openHoliday(holiday: ShopHoliday | null = null): void {
    if (!this.canEditPremiums()) {
      return;
    }
    this.dialog
      .open(
        HolidayDialog,
        fitDialogToHandset(
          {
            width: 'min(28rem, 78vw)',
            data: { year: this.holidayYear(), holiday },
          },
          this.breakpoints.isHandset(),
        ),
      )
      .afterClosed()
      .subscribe((result: HolidayDialogResult | undefined) => {
        if (!result) {
          return;
        }
        this.holidayBusy.set(true);
        this.premiumsError.set('');
        const call = holiday
          ? this.api.updateHoliday(holiday.id, result)
          : this.api.createHoliday(result);
        call.subscribe({
          next: () => {
            this.holidayBusy.set(false);
            this.reloadHolidays();
          },
          error: (err: unknown) => {
            this.holidayBusy.set(false);
            this.premiumsError.set(apiErrorMessage(err));
          },
        });
      });
  }

  deleteHoliday(holiday: ShopHoliday): void {
    if (!this.canEditPremiums()) {
      return;
    }
    this.confirm
      .confirm({
        title: 'Delete holiday',
        message: `Delete ${holiday.name} on ${holiday.date}?`,
        confirmLabel: 'Delete',
      })
      .subscribe((result) => {
        if (!result.confirmed) {
          return;
        }
        this.holidayBusy.set(true);
        this.premiumsError.set('');
        this.api.deleteHoliday(holiday.id).subscribe({
          next: () => {
            this.holidayBusy.set(false);
            this.reloadHolidays();
          },
          error: (err: unknown) => {
            this.holidayBusy.set(false);
            this.premiumsError.set(apiErrorMessage(err));
          },
        });
      });
  }

  copyLastYear(): void {
    if (!this.canEditPremiums()) {
      return;
    }
    const toYear = this.holidayYear();
    this.holidayBusy.set(true);
    this.premiumsError.set('');
    this.premiumsNotice.set('');
    this.api.copyHolidayYear({ fromYear: toYear - 1, toYear }).subscribe({
      next: (copied) => {
        this.holidayBusy.set(false);
        this.premiumsNotice.set(`Created ${copied.created}, skipped ${copied.skipped}.`);
        this.reloadHolidays();
      },
      error: (err: unknown) => {
        this.holidayBusy.set(false);
        this.premiumsError.set(apiErrorMessage(err));
      },
    });
  }

  openPremium(row: PayrollStaffRow): void {
    if (this.isLocked() || !this.from() || !this.to()) {
      return;
    }
    this.dialog
      .open(
        PremiumOverrideDialog,
        fitDialogToHandset(
          {
            width: 'min(28rem, 78vw)',
            data: { staffName: row.staffName, amount: row.premiumPay ?? null },
          },
          this.breakpoints.isHandset(),
        ),
      )
      .afterClosed()
      .subscribe((result) => {
        if (!result) {
          return;
        }
        this.saveAmountOverride(row, 'premium', result.amount, result.reason);
      });
  }

  openDayPayOverride(row: PayrollStaffRow): void {
    if (this.isLocked() || !this.from() || !this.to()) {
      return;
    }
    const existing = row.dayPayOverrides?.[0] ?? null;
    this.dialog
      .open(
        DayPayOverrideDialog,
        fitDialogToHandset(
          {
            width: 'min(28rem, 78vw)',
            data: {
              staffName: row.staffName,
              workDate: existing?.workDate ?? this.from(),
              mode: existing?.mode ?? 'half',
              customAmount: existing?.customAmount ?? null,
              reason: existing?.reason ?? '',
            },
          },
          this.breakpoints.isHandset(),
        ),
      )
      .afterClosed()
      .subscribe((result) => {
        if (!result) {
          return;
        }
        this.error.set('');
        this.api
          .saveDayPayOverride({
            staffId: row.staffId,
            workDate: result.workDate,
            mode: result.mode,
            customAmount: result.customAmount,
            reason: result.reason,
          })
          .subscribe({
            next: () => this.load(),
            error: (err: unknown) => this.error.set(apiErrorMessage(err)),
          });
      });
  }

  openAllowances(row: PayrollStaffRow): void {
    if (this.isLocked() || !this.from() || !this.to()) {
      return;
    }
    this.dialog
      .open(
        AllowanceOverrideDialog,
        fitDialogToHandset(
          {
            width: 'min(28rem, 78vw)',
            data: { staffName: row.staffName, amount: row.allowances ?? null },
          },
          this.breakpoints.isHandset(),
        ),
      )
      .afterClosed()
      .subscribe((result) => {
        if (!result) {
          return;
        }
        this.saveAmountOverride(row, 'allowances', result.amount, result.reason);
      });
  }

  openLoanInstalment(row: PayrollStaffRow): void {
    if (this.isLocked() || !this.from() || !this.to()) {
      return;
    }
    this.dialog
      .open(
        LoanOverrideDialog,
        fitDialogToHandset(
          {
            width: 'min(28rem, 78vw)',
            data: {
              staffName: row.staffName,
              loans: (row.loanItems ?? []).map((item) => ({
                loanId: item.loanId,
                kind: item.kind,
                amount: item.amount,
              })),
            },
          },
          this.breakpoints.isHandset(),
        ),
      )
      .afterClosed()
      .subscribe((result) => {
        if (!result) {
          return;
        }
        this.saveAmountOverride(row, loanOverrideKey(result.loanId), result.amount, result.reason);
      });
  }

  private saveAmountOverride(
    row: PayrollStaffRow,
    key: string,
    amount: number | null,
    reason: string,
  ): void {
    this.error.set('');
    this.api
      .saveOverride({
        staffId: row.staffId,
        from: this.from(),
        to: this.to(),
        key,
        amount,
        reason,
      })
      .subscribe({
        next: () => this.load(),
        error: (err: unknown) => this.error.set(apiErrorMessage(err)),
      });
  }

  rowWarnings(row: PayrollStaffRow): string {
    return (row.warnings ?? []).filter((warning) => warning.trim().length > 0).join(' · ');
  }

  formatOptionalHours(value: number | null | undefined): string {
    if (value == null) {
      return '—';
    }
    return Number(value).toFixed(2);
  }

  formatOptionalMoney(amount: number | null | undefined): string {
    if (amount == null) {
      return '—';
    }
    return this.formatMoney(amount);
  }

  private reloadHolidays(): void {
    this.api.listHolidays(this.holidayYear()).subscribe({
      next: (body) => {
        this.holidays.set(body.holidays ?? []);
        if (body.year) {
          this.holidayYear.set(body.year);
        }
      },
      error: (err: unknown) => this.premiumsError.set(apiErrorMessage(err)),
    });
  }

  private replaceThirteenthRow(saved: ThirteenthMonthRow): void {
    this.thirteenthRows.update((list) =>
      list.map((item) => (item.staffId === saved.staffId ? saved : item)),
    );
    const draft = this.thirteenthDrafts()[saved.staffId];
    if (draft) {
      draft.amount = saved.basicEarnedOverride ?? null;
    }
  }
}

function toTimeInput(value: string): string {
  const match = /^(\d{2}:\d{2})/.exec(value ?? '');
  return match?.[1] ?? '';
}

function toApiTime(value: string): string {
  const match = /^(\d{2}):(\d{2})/.exec((value ?? '').trim());
  return match ? `${match[1]}:${match[2]}:00` : value;
}

function toSettingsDraft(settings: {
  regularHolidayPercent: number;
  specialDayPercent: number;
  restDayPercent: number;
  holidayPremiumEnabled: boolean;
  restDayPremiumEnabled: boolean;
  nightDiffEnabled: boolean;
  nightDiffPercent: number;
  nightStart: string;
  nightEnd: string;
  shiftEnd?: string;
  otGraceMinutes?: number;
  adminsCanApproveOt?: boolean;
  otPremiumPercent?: number;
  otMonthlyDayDivisor?: number;
}): UpdatePayrollPremiumSettings {
  return {
    regularHolidayPercent: settings.regularHolidayPercent,
    specialDayPercent: settings.specialDayPercent,
    restDayPercent: settings.restDayPercent,
    holidayPremiumEnabled: settings.holidayPremiumEnabled,
    restDayPremiumEnabled: settings.restDayPremiumEnabled,
    nightDiffEnabled: settings.nightDiffEnabled,
    nightDiffPercent: settings.nightDiffPercent,
    nightStart: toTimeInput(settings.nightStart),
    nightEnd: toTimeInput(settings.nightEnd),
    shiftEnd: toTimeInput(settings.shiftEnd ?? '17:00:00'),
    otGraceMinutes: settings.otGraceMinutes ?? 30,
    adminsCanApproveOt: settings.adminsCanApproveOt ?? false,
    otPremiumPercent: settings.otPremiumPercent ?? 125,
    otMonthlyDayDivisor: settings.otMonthlyDayDivisor ?? 313,
  };
}

function rememberAmount(value: number | null | undefined): number {
  const amount = Number(value ?? 0);
  return Number.isFinite(amount) ? amount : 0;
}

function parseFileName(header: string | null): string | null {
  if (!header) {
    return null;
  }

  const utf = /filename\*=UTF-8''([^;]+)/i.exec(header);
  if (utf?.[1]) {
    try {
      return decodeURIComponent(utf[1].trim().replace(/^"|"$/g, ''));
    } catch {
      return utf[1].trim().replace(/^"|"$/g, '');
    }
  }

  const plain = /filename="?([^";]+)"?/i.exec(header);
  return plain?.[1]?.trim() || null;
}

/** Loan instalments already on the register row. Saved statutory deductions do not include them. */
function loanAmountOf(row: PayrollStaffRow): number {
  return row.loanDeductions == null ? 0 : Number(row.loanDeductions) || 0;
}
