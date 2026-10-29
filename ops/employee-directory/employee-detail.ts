import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import {
  OpsBreakpoints,
  fitDialogToHandset,
  environment,
  AuthService,
  ShopInfo,
  ShopApiService,
  apiErrorMessage,
  formatManilaDateTime,
  BannerComponent,
  CardComponent,
  EmptyStateComponent,
  KeyValueComponent,
  KeyValueGridComponent,
  OpsConfirmService,
  OpsToastService,
  PageHeaderComponent,
  SkeletonComponent,
  StatusBadgeComponent,
  StatusBadgeTone,
  MoveOpenJobsDialog,
  MoveOpenJobsDialogData,
  HandoverMode,
  StaffAvailabilityApi,
  KpiScorecardsPanel,
  ScorecardPeriod,
} from '@hr-host';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTableModule } from '@angular/material/table';
import { MatTabsModule } from '@angular/material/tabs';
import { ActivatedRoute, Router } from '@angular/router';
import { HrDocumentDialog } from '../documents/hr-document-dialog';
import { HrDocumentKind } from '../documents/hr-templates';
import {
  LEAVE_TYPE_LABELS,
  LeaveApi,
  LeaveBalance,
  leaveHasDayLimit,
  LeaveRequestRow,
  LeaveType,
} from '../leave/leave.api';
import { AllowanceDialogResult, StaffAllowanceDialog } from '../payroll/payroll-allowance-dialog';
import { allowancePerLabel } from '../payroll/payroll-allowances.format';
import { LoanDialogResult, StaffLoanDialog } from '../payroll/payroll-loan-dialog';
import {
  loanDeleteAllowed,
  loanStatusLabel,
  periodsRemaining,
} from '../payroll/payroll-loans.format';
import { money } from '../payroll/payroll-lock';
import { restDaysLabel } from '../payroll/payroll-premiums.format';
import { PayrollRateDialog } from '../payroll/payroll-rate-dialog';
import { PayrollApi } from '../payroll/payroll.api';
import {
  PayrollRatesResponse,
  StaffAllowance,
  StaffLoan,
  StaffLoanDetail,
} from '../payroll/payroll.models';
import {
  DirectoryRole,
  DirectoryStatus,
  EmployeeCard,
  EmployeeDirectoryApi,
  EmploymentDocType,
  EmploymentDocument,
  HandoverMove,
  HandoverReason,
  StaffOpenWork,
} from './employee-directory.api';
import { deactivationNotices } from './open-work';
import {
  DIRECTORY_ROLE_LABELS,
  DirectoryPersonDialog,
  DirectoryShopOption,
  EmployeeDocumentUploadDialog,
} from './employee-directory.dialogs';
import { EmploymentLetterDialog, EmploymentLetterKind } from './employment-letter';
import {
  HrChangeLogRow,
  hrChangeFieldLabel,
  hrChangeValue,
  sortChangeLogNewestFirst,
} from './hr-change-log';
import { formatPayslipMoney } from '../payroll/payroll-payslip';
import { formatDay, formatDayRange } from '../hr-dates';

const STATUS_LABELS: Record<DirectoryStatus, string> = {
  active: 'Active',
  on_leave_today: 'On leave today',
  inactive: 'Inactive',
};

const DOC_LABELS: Record<EmploymentDocType, string> = {
  offer: 'Offer',
  contract: 'Contract',
  id: 'ID',
  other: 'Other',
  coe_request_note: 'COE request note',
};

@Component({
  selector: 'app-employee-detail',
  imports: [
    MatButton,
    MatIconButton,
    MatIconModule,
    MatMenuModule,
    MatTableModule,
    MatTabsModule,
    PageHeaderComponent,
    CardComponent,
    BannerComponent,
    SkeletonComponent,
    EmptyStateComponent,
    StatusBadgeComponent,
    KeyValueComponent,
    KeyValueGridComponent,
    KpiScorecardsPanel,
  ],
  templateUrl: './employee-detail.html',
  styles: `
    .emp-tabs {
      margin-top: var(--space-4);
    }

    .emp-tab-content {
      padding-top: var(--space-4);
      display: grid;
      gap: var(--space-6);
    }

    .emp-docs-head {
      display: flex;
      justify-content: flex-end;
      flex-wrap: wrap;
      gap: var(--space-2);
      margin-bottom: var(--space-2);
    }

    .emp-rate-actions {
      display: flex;
      justify-content: flex-end;
      flex-wrap: wrap;
      gap: var(--space-2);
      margin-bottom: var(--space-2);
    }

    .emp-balances-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr));
      gap: var(--space-4);
    }

    .emp-balance-card {
      background: var(--ops-surface-container, #f8f9fa);
      border: 1px solid var(--ops-border, #e4e6ee);
      border-radius: 8px;
      padding: var(--space-3) var(--space-4);
    }

    .emp-balance-title {
      font-size: 0.8rem;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      color: var(--ops-muted, #5b6178);
      margin: 0 0 var(--space-2);
    }

    .emp-balance-stat {
      font-size: 1.4rem;
      font-weight: 700;
      color: var(--ops-on-surface, #1e2022);
      margin: 0;
    }

    .emp-balance-meta {
      font-size: 0.8rem;
      color: var(--ops-muted, #5b6178);
      margin: var(--space-1) 0 0;
    }

    .emp-history-list {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: var(--space-3);
    }

    .emp-history-row {
      border-bottom: 1px solid var(--ops-border, #e4e6ee);
      padding-bottom: var(--space-3);
    }

    .emp-history-head {
      display: flex;
      justify-content: space-between;
      align-items: baseline;
      flex-wrap: wrap;
      gap: var(--space-4);
    }

    .emp-history-when {
      font-size: 0.8rem;
      color: var(--ops-muted, #5b6178);
    }

    .emp-history-change,
    .emp-history-meta {
      margin: var(--space-1) 0 0;
      font-size: 0.9rem;
      overflow-wrap: anywhere;
    }

    .emp-history-meta {
      color: var(--ops-muted, #5b6178);
    }

    .emp-loan-reversed {
      color: var(--ops-muted, #5b6178);
      text-decoration: line-through;
    }
  `,
})
export class EmployeeDetailPage implements OnInit {
  protected readonly formatDay = formatDay;
  protected readonly formatDayRange = formatDayRange;
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(EmployeeDirectoryApi);
  private readonly leaveApi = inject(LeaveApi);
  private readonly payrollApi = inject(PayrollApi);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly breakpoints = inject(OpsBreakpoints);
  private readonly confirm = inject(OpsConfirmService);
  private readonly toast = inject(OpsToastService);
  private readonly shopApi = inject(ShopApiService);
  private readonly http = inject(HttpClient);
  private readonly availabilityApi = inject(StaffAvailabilityApi);
  private readonly base = environment.apiBaseUrl;

  protected readonly id = signal('');
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly person = signal<EmployeeCard | null>(null);
  /** /me/profile. The caller's own record, with no employer edit controls. */
  protected readonly selfProfile = signal(this.route.snapshot.data?.['self'] === true);
  protected readonly shops = signal<DirectoryShopOption[]>([]);
  protected readonly currentShop = signal<ShopInfo | null>(null);

  // Tabs
  protected readonly selectedTabIndex = signal(0);
  protected readonly isManager = computed(() => {
    if (this.selfProfile()) {
      return false;
    }
    const role = this.auth.me()?.staffRole;
    return role === 'owner' || role === 'admin';
  });

  protected readonly canEdit = computed(() => {
    if (this.selfProfile()) {
      return false;
    }
    const role = this.auth.me()?.staffRole;
    if (role === 'owner') {
      return true;
    }
    if (role === 'admin') {
      return this.person()?.role !== 'owner';
    }
    return false;
  });

  protected readonly isOwner = computed(() => this.auth.me()?.staffRole === 'owner');
  protected readonly restDayText = restDaysLabel;

  /** Owner sees scorecards on other people's records. Everyone sees their own finalised cards on My profile. */
  protected readonly ownerScorecards = computed(() => !this.selfProfile() && this.isOwner());

  protected readonly tabs = computed(() => {
    if (this.isManager()) {
      return [
        { id: 'overview', label: 'Overview' },
        { id: 'pay', label: 'Pay' },
        { id: 'loans', label: 'Loans / Cash advance' },
        { id: 'documents', label: 'Documents' },
        { id: 'leave', label: 'Leave' },
        ...(this.ownerScorecards() ? [{ id: 'scorecards', label: 'Scorecards' }] : []),
        { id: 'history', label: 'History' },
      ];
    }
    return [
      { id: 'overview', label: 'Overview' },
      { id: 'documents', label: 'Documents' },
      ...(this.selfProfile() ? [{ id: 'scorecards', label: 'Scorecards' }] : []),
    ];
  });

  /** Branch review period for new cards: the staff member's branch, yearly until known. */
  protected readonly branchPeriod = computed<ScorecardPeriod>(() => {
    const branchId = this.person()?.branchId;
    return this.shops().find((shop) => shop.id === branchId)?.scorecardPeriod ?? 'yearly';
  });

  protected readonly scorecardsTabActive = computed(
    () => this.tabs()[this.selectedTabIndex()]?.id === 'scorecards',
  );

  // Pay data
  protected readonly payLoading = signal(false);
  protected readonly payRates = signal<PayrollRatesResponse | null>(null);
  protected readonly allowances = signal<StaffAllowance[]>([]);
  protected readonly allowanceNames = signal<string[]>([]);
  protected readonly allowanceLoading = signal(false);
  protected readonly allowanceError = signal('');
  protected readonly allowanceColumns = [
    'name',
    'amount',
    'per',
    'taxable',
    'effectiveFrom',
    'effectiveTo',
    'active',
    'actions',
  ];
  protected readonly allowancePerLabel = allowancePerLabel;
  protected readonly money = money;

  protected readonly loans = signal<StaffLoan[]>([]);
  protected readonly loanKinds = signal<string[]>([]);
  protected readonly loanLoading = signal(false);
  protected readonly loanError = signal('');
  protected readonly loanDetail = signal<StaffLoanDetail | null>(null);
  protected readonly loanDetailLoading = signal(false);
  protected readonly loanColumns = [
    'kind',
    'principal',
    'perPeriod',
    'paid',
    'balance',
    'status',
    'periodsLeft',
    'actions',
  ];
  protected readonly loanPaymentColumns = ['period', 'amount', 'state'];
  protected readonly loanStatusLabel = loanStatusLabel;
  protected readonly periodsRemaining = periodsRemaining;

  // Documents data
  protected readonly docsLoading = signal(false);
  protected readonly documents = signal<EmploymentDocument[]>([]);
  protected readonly docColumns = ['type', 'fileName', 'createdAt', 'byteSize', 'note', 'actions'];

  // Leave data
  protected readonly leaveLoading = signal(false);
  protected readonly leaveBalances = signal<LeaveBalance[]>([]);
  protected readonly leaveHasDayLimit = leaveHasDayLimit;
  protected leaveTypeLabel(type: LeaveType): string {
    return LEAVE_TYPE_LABELS[type] ?? type;
  }
  protected readonly leaveRequests = signal<LeaveRequestRow[]>([]);
  protected readonly leaveColumns = ['leaveType', 'dates', 'status', 'note'];

  // History data
  protected readonly historyLoading = signal(false);
  protected readonly history = signal<HrChangeLogRow[]>([]);

  ngOnInit(): void {
    const routeId = this.route.snapshot.paramMap.get('id') ?? '';
    const id = this.selfProfile() ? (this.auth.me()?.staffId ?? '') : routeId;
    this.id.set(id);

    this.route.queryParamMap.subscribe((params) => {
      const tab = params.get('tab') ?? 'overview';
      const available = this.tabs();
      const idx = available.findIndex((t) => t.id === tab);
      if (idx >= 0 && idx !== this.selectedTabIndex()) {
        this.selectedTabIndex.set(idx);
      }
    });

    this.loadPerson();
    this.loadShops();
  }

  protected loadPerson(): void {
    const id = this.id();
    if (!id) {
      this.person.set(null);
      this.error.set(this.selfProfile() ? 'Your profile is not available.' : 'Employee not found.');
      this.loading.set(false);
      return;
    }
    this.loading.set(true);
    this.error.set('');

    this.api.get(id).subscribe({
      next: (card) => {
        this.person.set(card);
        this.loading.set(false);
        this.loadTabContent();
      },
      error: (err: unknown) => {
        this.error.set(apiErrorMessage(err));
        this.loading.set(false);
      },
    });
  }

  private loadShops(): void {
    // The shop list is owner-only (settings). Other roles never pick a shop here.
    if (this.isOwner()) {
      this.http.get<DirectoryShopOption[]>(`${this.base}/api/settings/branches`).subscribe({
        next: (branches) => {
          this.shops.set(branches);
        },
        error: () => this.shops.set([]),
      });
    }
    this.shopApi.getShop().subscribe({
      next: (s) => this.currentShop.set(s),
    });
  }

  protected onTabChange(index: number): void {
    this.selectedTabIndex.set(index);
    const tabId = this.tabs()[index]?.id ?? 'overview';
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { tab: tabId },
      queryParamsHandling: 'merge',
    });
    this.loadTabContent();
  }

  protected loadTabContent(): void {
    const p = this.person();
    if (!p) return;
    const tab = this.tabs()[this.selectedTabIndex()]?.id;

    if (tab === 'pay' && this.isManager()) {
      this.loadPay();
      this.loadAllowances();
    } else if (tab === 'loans' && this.isManager()) {
      this.loadLoans();
    } else if (tab === 'documents') {
      this.loadDocuments();
    } else if (tab === 'leave') {
      this.loadLeave();
    } else if (tab === 'history' && this.isManager()) {
      this.loadHistory();
    }
  }

  protected loadPay(): void {
    const p = this.person();
    if (!p) return;
    this.payLoading.set(true);
    this.payrollApi.getRates(p.id).subscribe({
      next: (res) => {
        this.payRates.set(res);
        this.payLoading.set(false);
      },
      error: () => this.payLoading.set(false),
    });
  }

  protected loadAllowances(): void {
    const p = this.person();
    if (!p) return;
    this.allowanceLoading.set(true);
    this.allowanceError.set('');
    this.payrollApi.listAllowances(p.id).subscribe({
      next: (res) => {
        this.allowances.set(res.allowances ?? []);
        this.allowanceNames.set(res.suggestedNames ?? []);
        this.allowanceLoading.set(false);
      },
      error: (err: unknown) => {
        this.allowanceError.set(apiErrorMessage(err));
        this.allowanceLoading.set(false);
      },
    });
  }

  protected openAllowance(row: StaffAllowance | null): void {
    const p = this.person();
    if (!p || !this.isManager()) return;
    this.dialog
      .open(
        StaffAllowanceDialog,
        fitDialogToHandset(
          {
            width: 'min(32rem, 78vw)',
            data: { allowance: row, suggestedNames: this.allowanceNames() },
          },
          this.breakpoints.isHandset(),
        ),
      )
      .afterClosed()
      .subscribe((result: AllowanceDialogResult | undefined) => {
        if (!result) return;
        this.allowanceError.set('');
        const call = row
          ? this.payrollApi.updateAllowance(row.id, result)
          : this.payrollApi.createAllowance({ ...result, staffId: p.id });
        call.subscribe({
          next: () => this.loadAllowances(),
          error: (err: unknown) => this.allowanceError.set(apiErrorMessage(err)),
        });
      });
  }

  protected loadLoans(): void {
    const p = this.person();
    if (!p) return;
    this.loanLoading.set(true);
    this.loanError.set('');
    this.payrollApi.listLoans(p.id, true).subscribe({
      next: (res) => {
        const rows = res.loans ?? [];
        this.loans.set(rows);
        this.loanKinds.set(res.suggestedKinds ?? []);
        this.loanLoading.set(false);
        const openId = this.loanDetail()?.id;
        if (openId && !rows.some((row) => row.id === openId)) {
          this.loanDetail.set(null);
        } else if (openId) {
          this.loadLoanPayments(openId);
        }
      },
      error: (err: unknown) => {
        this.loanError.set(apiErrorMessage(err));
        this.loanLoading.set(false);
      },
    });
  }

  protected openLoan(row: StaffLoan | null): void {
    const p = this.person();
    if (!p || !this.isManager()) return;
    this.dialog
      .open(
        StaffLoanDialog,
        fitDialogToHandset(
          {
            width: 'min(32rem, 78vw)',
            data: { loan: row, suggestedKinds: this.loanKinds() },
          },
          this.breakpoints.isHandset(),
        ),
      )
      .afterClosed()
      .subscribe((result: LoanDialogResult | undefined) => {
        if (!result) return;
        this.loanError.set('');
        const call = row
          ? this.payrollApi.updateLoan(row.id, result)
          : this.payrollApi.createLoan({ ...result, staffId: p.id });
        call.subscribe({
          next: () => this.loadLoans(),
          error: (err: unknown) => this.loanError.set(apiErrorMessage(err)),
        });
      });
  }

  protected loadLoanPayments(id: string): void {
    this.loanDetailLoading.set(true);
    this.payrollApi.getLoan(id).subscribe({
      next: (detail) => {
        this.loanDetail.set(detail);
        this.loanDetailLoading.set(false);
      },
      error: (err: unknown) => {
        this.loanError.set(apiErrorMessage(err));
        this.loanDetailLoading.set(false);
      },
    });
  }

  protected cancelLoan(row: StaffLoan): void {
    if (!this.isManager() || row.status !== 'active') return;
    this.confirm
      .confirm({
        title: 'Cancel loan',
        message: `Cancel ${row.kind}? Future instalments stop. Payments already taken stay.`,
        confirmLabel: 'Cancel loan',
        tone: 'danger',
        requireReason: true,
        reasonLabel: 'Reason',
      })
      .subscribe((res) => {
        const reason = res.reason?.trim() ?? '';
        if (!res.confirmed || !reason) return;
        this.loanError.set('');
        this.payrollApi.cancelLoan(row.id, reason).subscribe({
          next: () => this.loadLoans(),
          error: (err: unknown) => this.loanError.set(apiErrorMessage(err)),
        });
      });
  }

  protected canDeleteLoan(row: StaffLoan): boolean {
    const detail = this.loanDetail();
    if (detail?.id === row.id) {
      return loanDeleteAllowed(row, detail.payments);
    }
    return loanDeleteAllowed(row);
  }

  protected deleteLoan(row: StaffLoan): void {
    if (!this.isManager() || !this.canDeleteLoan(row)) return;
    this.confirm
      .confirm({
        title: 'Delete loan',
        message: `Delete ${row.kind}? This is only allowed when the loan has never been paid.`,
        confirmLabel: 'Delete',
        tone: 'danger',
      })
      .subscribe((res) => {
        if (!res.confirmed) return;
        this.loanError.set('');
        this.payrollApi.deleteLoan(row.id).subscribe({
          next: () => {
            if (this.loanDetail()?.id === row.id) {
              this.loanDetail.set(null);
            }
            this.toast.success('Loan deleted.');
            this.loadLoans();
          },
          error: (err: unknown) => {
            const message = apiErrorMessage(err);
            this.loanError.set(message);
            this.toast.error(message);
          },
        });
      });
  }

  protected loanTone(status: string): StatusBadgeTone {
    switch (status) {
      case 'active':
        return 'info';
      case 'paid':
        return 'ok';
      case 'cancelled':
        return 'neutral';
      default:
        return 'neutral';
    }
  }

  protected deleteAllowance(row: StaffAllowance): void {
    if (!this.isManager()) return;
    this.confirm
      .confirm({
        title: 'Delete allowance',
        message: `Delete ${row.name}? Locked payslips keep the copy already saved.`,
        confirmLabel: 'Delete',
        tone: 'danger',
      })
      .subscribe((res) => {
        if (!res.confirmed) return;
        this.allowanceError.set('');
        this.payrollApi.deleteAllowance(row.id).subscribe({
          next: () => {
            this.toast.success('Allowance deleted.');
            this.loadAllowances();
          },
          error: (err: unknown) => {
            const message = apiErrorMessage(err);
            this.allowanceError.set(message);
            this.toast.error(message);
          },
        });
      });
  }

  protected loadDocuments(): void {
    const p = this.person();
    if (!p) return;
    this.docsLoading.set(true);
    const obs = this.isManager() ? this.api.listDocuments(p.id) : this.api.listMine();
    obs.subscribe({
      next: (docs) => {
        this.documents.set(docs);
        this.docsLoading.set(false);
      },
      error: () => this.docsLoading.set(false),
    });
  }

  protected loadLeave(): void {
    const p = this.person();
    if (!p) return;
    this.leaveLoading.set(true);
    const currentYear = new Date().getFullYear();

    if (this.isManager()) {
      this.leaveApi.shopBalances(currentYear).subscribe({
        next: (allBalances) => {
          const match = allBalances.find((b) => b.staffId === p.id);
          this.leaveBalances.set(match?.balances ?? []);
        },
      });
    } else {
      this.leaveApi.balances(currentYear).subscribe({
        next: (b) => this.leaveBalances.set(b),
      });
    }

    this.leaveApi.list(currentYear).subscribe({
      next: (reqs) => {
        const staffReqs = reqs.filter((r) => r.staffId === p.id);
        this.leaveRequests.set(staffReqs);
        this.leaveLoading.set(false);
      },
      error: () => this.leaveLoading.set(false),
    });
  }

  protected loadHistory(): void {
    const p = this.person();
    if (!p) return;
    this.historyLoading.set(true);
    this.api.listChangeLog(p.id).subscribe({
      next: (body) => {
        this.history.set(sortChangeLogNewestFirst(body.rows ?? []));
        this.historyLoading.set(false);
      },
      error: () => this.historyLoading.set(false),
    });
  }

  protected openEdit(): void {
    const p = this.person();
    if (!p || !this.canEdit()) return;
    const ref = this.dialog.open(DirectoryPersonDialog, {
      data: {
        mode: 'edit',
        person: p,
        isOwner: this.isOwner(),
        shops: this.shops(),
      },
      width: 'min(48rem, 78vw)',
      maxWidth: 'calc(100vw - 32px)',
    });
    ref.afterClosed().subscribe((saved) => {
      if (saved) {
        this.toast.success('Saved.');
        this.loadPerson();
      }
    });
  }

  protected changeRate(): void {
    const p = this.person();
    if (!p) return;
    const ref = this.dialog.open(
      PayrollRateDialog,
      fitDialogToHandset(
        {
          data: {
            staffId: p.id,
            staffName: p.displayName,
          },
          width: 'min(32rem, 78vw)',
          maxWidth: 'calc(100vw - 32px)',
        },
        this.breakpoints.isHandset(),
      ),
    );
    ref.afterClosed().subscribe((changed) => {
      if (changed) {
        this.toast.success('Rate saved.');
        this.loadPay();
      }
    });
  }

  protected openAddDocument(): void {
    const p = this.person();
    if (!p) return;
    const ref = this.dialog.open(EmployeeDocumentUploadDialog, {
      data: {
        person: p,
      },
      width: 'min(32rem, 78vw)',
      maxWidth: 'calc(100vw - 32px)',
    });
    ref.afterClosed().subscribe((added) => {
      if (added) {
        this.toast.success('Document added.');
        this.loadDocuments();
      }
    });
  }

  protected openLetter(kind: EmploymentLetterKind): void {
    const p = this.person();
    if (!p) return;
    const ref = this.dialog.open(EmploymentLetterDialog, {
      data: {
        kind,
        person: p,
        shop: this.currentShop(),
        payBasis: p.payBasis ?? null,
        ratePhp: p.ratePhp ?? null,
      },
      width: 'min(48rem, 78vw)',
      maxWidth: 'calc(100vw - 32px)',
    });
    ref.afterClosed().subscribe((saved) => {
      if (saved) {
        this.toast.success('Letter saved to employment docs.');
        this.loadDocuments();
      }
    });
  }

  protected openHrDocument(kind: HrDocumentKind): void {
    const p = this.person();
    if (!p) return;
    this.dialog.open(HrDocumentDialog, {
      data: {
        kind,
        person: p,
        shop: this.currentShop(),
      },
      width: 'min(48rem, 78vw)',
      maxWidth: 'calc(100vw - 32px)',
    });
  }

  protected toggleActive(active: boolean): void {
    const p = this.person();
    if (!p) return;
    if (active) {
      this.confirm
        .confirm({
          title: `Reactivate ${p.displayName}?`,
          message: `They will appear as active and can be scheduled or clocked in.`,
          confirmLabel: 'Reactivate',
          tone: 'default',
        })
        .subscribe((res) => {
          if (!res.confirmed) return;
          this.patchActive(p, true);
        });
      return;
    }
    // Deactivation: open jobs must be handed over first (the server returns 409 otherwise).
    this.api.openWork(p.id).subscribe({
      next: (work) => {
        if (work.jobs.length > 0) {
          this.openHandoverDialog(p, work, 'separate');
        } else {
          this.confirmDeactivate(p, work);
        }
      },
      error: (err: unknown) => this.reportError(err),
    });
  }

  /** "Move open jobs to…": rebalancing, so the person stays active. */
  protected openMoveJobs(): void {
    const p = this.person();
    if (!p) return;
    this.api.openWork(p.id).subscribe({
      next: (work) => {
        if (work.jobs.length === 0) {
          this.toast.success(`${p.displayName} has no open jobs.`);
          return;
        }
        this.openHandoverDialog(p, work, 'rebalance');
      },
      error: (err: unknown) => this.reportError(err),
    });
  }

  private confirmDeactivate(p: EmployeeCard, work: StaffOpenWork): void {
    const lines = [
      'They will be hidden from the active roster. Rate history and documents are preserved.',
    ];
    if (work.pendingLeave.length > 0) {
      lines.push('Their pending leave requests will be cancelled.');
    }
    if (work.openPunch) {
      lines.push(`Their open punch from ${work.openPunch.label} will be closed now.`);
    }
    this.confirm
      .confirm({
        title: `Deactivate ${p.displayName}?`,
        message: lines.join(' '),
        confirmLabel: 'Deactivate',
        tone: 'danger',
      })
      .subscribe((res) => {
        if (!res.confirmed) return;
        this.patchActive(p, false);
      });
  }

  private openHandoverDialog(p: EmployeeCard, work: StaffOpenWork, mode: HandoverMode): void {
    this.availabilityApi.forDay().subscribe({
      next: (day) => {
        const data: MoveOpenJobsDialogData = {
          mode,
          personId: p.id,
          personName: p.displayName,
          jobs: work.jobs,
          staff: day.staff,
          pendingLeave: mode === 'separate' ? work.pendingLeave : [],
          approvedLeave: mode === 'separate' ? work.approvedLeave : [],
          openPunch: mode === 'separate' ? work.openPunch : null,
        };
        const ref = this.dialog.open<MoveOpenJobsDialog, MoveOpenJobsDialogData, HandoverMove[]>(
          MoveOpenJobsDialog,
          fitDialogToHandset(
            { width: 'min(48rem, 78vw)', maxWidth: 'calc(100vw - 32px)', data },
            this.breakpoints.isHandset(),
          ),
        );
        ref.afterClosed().subscribe((moves) => {
          if (!moves) return;
          this.submitHandover(p, moves, mode);
        });
      },
      error: (err: unknown) => this.reportError(err),
    });
  }

  private submitHandover(p: EmployeeCard, moves: HandoverMove[], mode: HandoverMode): void {
    const reason: HandoverReason = mode === 'separate' ? 'separated' : 'rebalanced';
    this.api.handover(p.id, { moves, reason }).subscribe({
      next: () => {
        if (mode === 'separate') {
          this.patchActive(p, false);
          return;
        }
        this.toast.success(`Moved ${moves.length} open job${moves.length === 1 ? '' : 's'}.`);
        this.loadPerson();
      },
      error: (err: unknown) => this.reportError(err),
    });
  }

  private patchActive(p: EmployeeCard, active: boolean): void {
    this.api.update(p.id, { active }).subscribe({
      next: (card) => {
        if (active) {
          this.toast.success('Person reactivated.');
        } else {
          this.toast.success('Person deactivated.');
          for (const notice of deactivationNotices(card.deactivation)) {
            this.toast.success(notice);
          }
        }
        this.loadPerson();
      },
      error: (err: unknown) => {
        // A 409 here means open jobs were assigned after the check. Show the server's message and reload.
        this.reportError(err);
        if (err instanceof HttpErrorResponse && err.status === 409) {
          this.loadPerson();
        }
      },
    });
  }

  private reportError(err: unknown): void {
    const message = apiErrorMessage(err);
    this.error.set(message);
    this.toast.error(message);
  }

  protected downloadDocument(doc: EmploymentDocument): void {
    this.api.download(doc.id).subscribe((blob) => {
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = doc.fileName;
      a.click();
      URL.revokeObjectURL(url);
    });
  }

  // Helpers
  protected statusTone(p: EmployeeCard): StatusBadgeTone {
    if (p.status === 'on_leave_today') return 'warn';
    return p.active ? 'ok' : 'neutral';
  }

  protected statusLabel(p: EmployeeCard): string {
    if (p.status === 'on_leave_today') return STATUS_LABELS.on_leave_today;
    return p.active ? STATUS_LABELS.active : STATUS_LABELS.inactive;
  }

  protected roleLabel(role: DirectoryRole): string {
    return DIRECTORY_ROLE_LABELS[role] ?? role;
  }

  protected docLabel(type: EmploymentDocType): string {
    return DOC_LABELS[type] ?? type;
  }

  protected formatBytes(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  protected formatTenure(hireDate?: string | null): string {
    if (!hireDate) return '—';
    const hire = new Date(hireDate);
    const now = new Date();
    let years = now.getFullYear() - hire.getFullYear();
    let months = now.getMonth() - hire.getMonth();
    if (months < 0) {
      years--;
      months += 12;
    }
    const parts: string[] = [];
    if (years > 0) parts.push(`${years} ${years === 1 ? 'yr' : 'yrs'}`);
    if (months > 0) parts.push(`${months} ${months === 1 ? 'mo' : 'mos'}`);
    return parts.length ? parts.join(' ') : 'Less than a month';
  }

  /** Peso amounts read like Payroll (same formatter): ₱22,000.00. */
  protected formatPeso(value: number | null | undefined): string {
    return formatPayslipMoney(value);
  }

  protected fieldLabel(field: string): string {
    return hrChangeFieldLabel(field);
  }

  protected valueLabel(field: string, value: string | null | undefined): string {
    return hrChangeValue(field, value);
  }

  protected historyWhen(atUtc: string): string {
    return formatManilaDateTime(atUtc);
  }

  protected historyContext(row: HrChangeLogRow): string {
    if (row.periodFrom || row.periodTo) {
      return `Period ${row.periodFrom ?? '—'} to ${row.periodTo ?? '—'}`;
    }
    if (row.year != null) {
      return `Year ${row.year}`;
    }
    return '';
  }

  protected leaveTone(status: string): StatusBadgeTone {
    if (status === 'approved') return 'ok';
    if (status === 'rejected') return 'danger';
    return 'warn';
  }
}
