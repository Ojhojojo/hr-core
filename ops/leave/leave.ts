import { Component, computed, inject, signal } from '@angular/core';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import {
  OpsBreakpoints,
  fitDialogToHandset,
  AuthService,
  apiErrorMessage,
  BannerComponent,
  MetricStripComponent,
  OpsConfirmService,
  OpsToastService,
  PageHeaderComponent,
  SkeletonComponent,
  StatCardComponent,
  StatusBadgeComponent,
  StatusBadgeTone,
  StaffAvailabilityApi,
} from '@hr-host';
import { MatMenu, MatMenuItem, MatMenuTrigger } from '@angular/material/menu';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import {
  LeaveApi,
  LeaveBalance,
  LeaveHalfDay,
  LeavePerson,
  LeaveRequestRow,
  LeaveType,
  StaffLeaveBalances,
} from './leave.api';
import {
  AddLeaveDialog,
  ApproveLeaveDialog,
  ApproveLeaveDialogData,
  ApproveLeaveDialogResult,
  CancelLeaveDialog,
  CancelLeaveDialogData,
  RequestLeaveDialog,
  SetAllotmentDialog,
} from './leave.dialogs';

const TYPE_LABELS: Record<LeaveType, string> = {
  vacation: 'Vacation',
  sick: 'Sick',
  unpaid: 'Unpaid',
  service_incentive: 'Service incentive (SIL)',
};

@Component({
  selector: 'app-leave',
  imports: [
    MatButton,
    MatIconButton,
    MatMenu,
    MatMenuItem,
    MatMenuTrigger,
    MatPaginatorModule,
    MatTableModule,
    RouterLink,
    PageHeaderComponent,
    BannerComponent,
    SkeletonComponent,
    MetricStripComponent,
    StatCardComponent,
    StatusBadgeComponent,
  ],
  templateUrl: './leave.html',
  styles: [
    `
      .mat-column-role {
        text-transform: capitalize;
      }
      .mat-column-vacation,
      .mat-column-sick,
      .mat-column-unpaid,
      .mat-column-service_incentive,
      .mat-column-days {
        text-align: center;
      }
      .mat-column-actions {
        text-align: right;
      }
      th.mat-column-vacation,
      th.mat-column-sick,
      th.mat-column-unpaid,
      th.mat-column-service_incentive,
      th.mat-column-days {
        text-align: center !important;
      }
      /* Column widths apply to the real table only; below 900px rows collapse to cards. */
      @media (min-width: 900.02px) {
        .mat-column-staff {
          width: 26%;
          min-width: 140px;
        }
        .mat-column-role {
          width: 16%;
          min-width: 90px;
        }
        .mat-column-vacation,
        .mat-column-sick,
        .mat-column-unpaid,
        .mat-column-service_incentive {
          width: 19%;
          min-width: 110px;
        }
        .mat-column-dates {
          min-width: 170px;
        }
        .mat-column-type {
          min-width: 90px;
        }
        .mat-column-days {
          min-width: 60px;
        }
        .mat-column-status {
          min-width: 100px;
        }
        .mat-column-actions {
          min-width: 160px;
        }
      }
      .ops-table th:first-child,
      .ops-table td:first-child {
        padding-left: var(--space-4);
      }
      .ops-table th:last-child,
      .ops-table td:last-child {
        padding-right: var(--space-4);
      }
      .leave-actions-cell {
        display: flex;
        align-items: center;
        justify-content: flex-end;
        flex-wrap: wrap;
        gap: var(--space-2);
      }
      .ops-balance-cell {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        gap: var(--space-1);
      }
      .leave-empty-msg {
        padding: var(--space-4);
      }
    `,
  ],
})
export class LeavePage {
  private readonly api = inject(LeaveApi);
  private readonly availabilityApi = inject(StaffAvailabilityApi);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly breakpoints = inject(OpsBreakpoints);
  private readonly confirm = inject(OpsConfirmService);
  private readonly toast = inject(OpsToastService);

  protected readonly year = new Date().getFullYear();
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  /** Signed-in user's own rows (`mine=true`). Empty for the owner, who has no personal leave. */
  protected readonly ownRequests = signal<LeaveRequestRow[]>([]);
  /** Shop rows for the decision list and history. Owner and admin only. */
  protected readonly shopRequests = signal<LeaveRequestRow[]>([]);
  protected readonly balances = signal<LeaveBalance[]>([]);
  protected readonly teamBalances = signal<StaffLeaveBalances[]>([]);
  protected readonly people = signal<LeavePerson[]>([]);

  /**
   * Balances load in parallel with the request list, so `loading` cannot gate them:
   * it clears when the request list lands, not when the balances do. These flags keep
   * a card at '—' until its own request answers, so a real 0 is never faked.
   */
  protected readonly balancesLoaded = signal(false);
  protected readonly teamBalancesLoaded = signal(false);

  protected readonly isOwner = computed(() => this.auth.me()?.staffRole === 'owner');
  protected readonly canDecide = computed(() => {
    const role = this.auth.me()?.staffRole;
    return role === 'owner' || role === 'admin';
  });

  protected readonly currentStaffId = computed(() => {
    const me = this.auth.me();
    if (!me) return null;

    const directStaffId = (me as { staffId?: string }).staffId;
    if (directStaffId) return directStaffId;

    if (me.displayName) {
      const match = this.teamBalances().find(
        (t) =>
          t.displayName.toLowerCase() === me.displayName?.toLowerCase() &&
          (!me.staffRole || t.role === me.staffRole),
      );
      if (match) return match.staffId;
    }

    if (me.displayName) {
      const matchPerson = this.people().find(
        (p) =>
          p.displayName.toLowerCase() === me.displayName?.toLowerCase() &&
          (!me.staffRole || p.role === me.staffRole),
      );
      if (matchPerson) return matchPerson.id;
    }

    return me.authUserId;
  });

  protected isOwnRequest(row: LeaveRequestRow): boolean {
    const myStaffId = this.currentStaffId();
    if (myStaffId && row.staffId === myStaffId) {
      return true;
    }
    const authId = this.auth.me()?.authUserId;
    if (authId && row.staffId === authId) {
      return true;
    }
    return false;
  }

  protected readonly pendingRequests = computed(() =>
    this.shopRequests().filter((r) => r.status === 'pending'),
  );
  protected readonly historyRequests = computed(() =>
    this.shopRequests().filter((r) => r.status !== 'pending'),
  );

  protected readonly teamRows = computed(() => {
    const team = this.teamBalances().filter((t) => !!t.staffId && !!t.displayName);
    if (team.length > 0) {
      return team;
    }
    // Fallback: If shopBalances is empty or loading, show all active non-owner employees from directory
    return this.people().map((p) => ({
      staffId: p.id,
      displayName: p.displayName,
      role: p.role ?? 'staff',
      balances: [
        { leaveType: 'vacation' as LeaveType, allotment: 0, used: 0, pending: 0, remaining: 0 },
        { leaveType: 'sick' as LeaveType, allotment: 0, used: 0, pending: 0, remaining: 0 },
        { leaveType: 'unpaid' as LeaveType, allotment: 0, used: 0, pending: 0, remaining: 0 },
      ],
    }));
  });

  protected readonly eligiblePeople = computed(() => {
    if (this.people().length > 0) {
      return this.people();
    }
    return this.teamBalances()
      .filter((t) => !!t.staffId && !!t.displayName)
      .map((t) => ({
        id: t.staffId,
        displayName: t.displayName,
        role: t.role,
      }));
  });

  // Table columns
  protected readonly employeeColumns = [
    'staff',
    'dates',
    'type',
    'days',
    'status',
    'note',
    'actions',
  ];
  protected readonly pendingColumns = ['staff', 'dates', 'type', 'days', 'note', 'actions'];
  protected readonly teamColumns = [
    'staff',
    'role',
    'vacation',
    'sick',
    'unpaid',
    'service_incentive',
  ];
  protected readonly historyColumns = [
    'staff',
    'dates',
    'type',
    'days',
    'status',
    'note',
    'actions',
  ];

  // Pagination
  protected readonly historyPageIndex = signal(0);
  protected readonly historyPageSize = signal(10);
  protected readonly historyPageSizeOptions = [5, 10, 25];
  protected readonly pagedHistoryRequests = computed(() => {
    const items = this.historyRequests();
    const size = this.historyPageSize();
    const maxPage = Math.max(0, Math.ceil(items.length / size) - 1);
    const page = Math.min(this.historyPageIndex(), maxPage);
    const start = page * size;
    return items.slice(start, start + size);
  });

  protected readonly myRequestsPageIndex = signal(0);
  protected readonly myRequestsPageSize = signal(10);
  protected readonly myRequestsPageSizeOptions = [5, 10, 25];
  protected readonly pagedMyRequests = computed(() => {
    const items = this.ownRequests();
    const size = this.myRequestsPageSize();
    const maxPage = Math.max(0, Math.ceil(items.length / size) - 1);
    const page = Math.min(this.myRequestsPageIndex(), maxPage);
    const start = page * size;
    return items.slice(start, start + size);
  });

  onHistoryPage(event: PageEvent): void {
    this.historyPageIndex.set(event.pageIndex);
    this.historyPageSize.set(event.pageSize);
  }

  onMyRequestsPage(event: PageEvent): void {
    this.myRequestsPageIndex.set(event.pageIndex);
    this.myRequestsPageSize.set(event.pageSize);
  }

  constructor() {
    this.reload(true);
    if (this.isOwner()) {
      this.api.listPeople().subscribe({
        next: (people) => {
          const employees = people.filter((person) => person.role !== 'owner');
          this.people.set(employees);
        },
        error: (err) => this.error.set(apiErrorMessage(err)),
      });
    }
  }

  protected typeLabel(type: LeaveType): string {
    return TYPE_LABELS[type] ?? type;
  }

  protected halfDayLabel(halfDay: LeaveHalfDay | null): string {
    if (halfDay === 'am') return ' (AM)';
    if (halfDay === 'pm') return ' (PM)';
    return '';
  }

  /** The requester can cancel their own pending leave. Owner and admin can also cancel pending or approved leave on the shop. */
  protected canCancel(row: LeaveRequestRow): boolean {
    if (this.isOwnRequest(row)) {
      return row.status === 'pending';
    }
    return this.canDecide() && (row.status === 'pending' || row.status === 'approved');
  }

  protected cancelRequest(row: LeaveRequestRow): void {
    const approved = row.status === 'approved';
    const data: CancelLeaveDialogData = {
      title: approved ? 'Cancel approved leave?' : 'Cancel leave request?',
      message: approved
        ? `This releases ${row.days} day(s) back to ${row.displayName}'s balance. Say why it is being cancelled.`
        : 'The request will be withdrawn and no longer count against the balance.',
      reasonRequired: approved,
    };
    const ref = this.dialog.open(
      CancelLeaveDialog,
      fitDialogToHandset(
        { width: 'min(32rem, 78vw)', maxWidth: 'calc(100vw - 32px)', data },
        this.breakpoints.isHandset(),
      ),
    );
    ref.afterClosed().subscribe((reason) => {
      if (reason === undefined) return;
      this.error.set('');
      this.api.cancel(row.id, reason || undefined).subscribe({
        next: () => {
          this.toast.success('Leave cancelled.');
          this.reload(false);
        },
        error: (err) => {
          const message = apiErrorMessage(err);
          this.error.set(message);
          this.toast.error(message);
        },
      });
    });
  }

  protected remaining(type: LeaveType): number {
    return this.balance(type).remaining;
  }

  protected balance(type: LeaveType): LeaveBalance {
    return (
      this.balances().find((row) => row.leaveType === type) ?? {
        leaveType: type,
        allotment: 0,
        used: 0,
        pending: 0,
        remaining: 0,
      }
    );
  }

  protected memberBalance(member: StaffLeaveBalances, type: LeaveType): LeaveBalance {
    return (
      member.balances?.find((row) => row.leaveType === type) ?? {
        leaveType: type,
        allotment: 0,
        used: 0,
        pending: 0,
        remaining: 0,
      }
    );
  }

  /** Placeholder until the balance request answers, so the card never shows a fake 0. */
  protected balanceValue(type: LeaveType): string | number {
    return this.balancesLoaded() ? this.balance(type).remaining : '—';
  }

  protected balanceSubtext(type: LeaveType): string {
    if (!this.balancesLoaded()) {
      return '';
    }
    const row = this.balance(type);
    return `Allotted: ${row.allotment} · Used: ${row.used} · Pending: ${row.pending}`;
  }

  /** Same placeholder rule for the owner team table, which falls back to zero-filled rows. */
  protected memberRemaining(member: StaffLeaveBalances, type: LeaveType): string | number {
    return this.teamBalancesLoaded() ? this.memberBalance(member, type).remaining : '—';
  }

  protected memberAllotment(member: StaffLeaveBalances, type: LeaveType): string | number {
    return this.teamBalancesLoaded() ? this.memberBalance(member, type).allotment : '—';
  }

  protected statusTone(status: string): StatusBadgeTone {
    if (status === 'approved') return 'ok';
    if (status === 'rejected') return 'danger';
    if (status === 'cancelled') return 'neutral';
    return 'warn';
  }

  protected openRequestLeave(): void {
    const ref = this.dialog.open(
      RequestLeaveDialog,
      fitDialogToHandset(
        {
          width: 'min(32rem, 78vw)',
          maxWidth: 'calc(100vw - 32px)',
        },
        this.breakpoints.isHandset(),
      ),
    );
    ref.afterClosed().subscribe((saved) => {
      if (saved) {
        this.toast.success('Leave saved.');
        this.reload(false);
      }
    });
  }

  protected openAddLeave(): void {
    const ref = this.dialog.open(
      AddLeaveDialog,
      fitDialogToHandset(
        {
          width: 'min(32rem, 78vw)',
          maxWidth: 'calc(100vw - 32px)',
          data: { people: this.eligiblePeople() },
        },
        this.breakpoints.isHandset(),
      ),
    );
    ref.afterClosed().subscribe((saved) => {
      if (saved) {
        this.toast.success('Leave saved.');
        this.reload(false);
      }
    });
  }

  protected openSetAllotment(): void {
    const ref = this.dialog.open(
      SetAllotmentDialog,
      fitDialogToHandset(
        {
          width: 'min(32rem, 78vw)',
          maxWidth: 'calc(100vw - 32px)',
          data: {
            people: this.eligiblePeople(),
            teamRows: this.teamRows(),
            year: this.year,
          },
        },
        this.breakpoints.isHandset(),
      ),
    );
    ref.afterClosed().subscribe((saved) => {
      if (saved) {
        this.toast.success('Leave saved.');
        this.reload(false);
      }
    });
  }

  protected decide(row: LeaveRequestRow, decision: 'approved' | 'rejected'): void {
    if (decision === 'rejected') {
      this.confirm
        .confirm({
          title: 'Reject leave request?',
          message: 'The requested days will be released back to the employee balance.',
          confirmLabel: 'Reject',
          tone: 'danger',
        })
        .subscribe((res) => {
          if (!res.confirmed) return;
          this.submitDecision(row.id, decision);
        });
    } else {
      this.approve(row);
    }
  }

  /** Approving first checks the jobs the person holds. If there are none, it approves as before. */
  private approve(row: LeaveRequestRow): void {
    this.error.set('');
    this.api.affectedJobs(row.id).subscribe({
      next: (jobs) => {
        if (jobs.length === 0) {
          this.submitDecision(row.id, 'approved');
          return;
        }
        this.availabilityApi.forDay().subscribe({
          next: (day) => this.openApproveDialog(row, jobs, day.staff),
          error: (err) => this.reportError(err),
        });
      },
      error: (err) => this.reportError(err),
    });
  }

  private openApproveDialog(
    row: LeaveRequestRow,
    jobs: ApproveLeaveDialogData['jobs'],
    staff: ApproveLeaveDialogData['staff'],
  ): void {
    const data: ApproveLeaveDialogData = {
      requesterId: row.staffId,
      requesterName: row.displayName,
      jobs,
      staff,
    };
    const ref = this.dialog.open<
      ApproveLeaveDialog,
      ApproveLeaveDialogData,
      ApproveLeaveDialogResult
    >(
      ApproveLeaveDialog,
      fitDialogToHandset(
        { width: 'min(48rem, 78vw)', maxWidth: 'calc(100vw - 32px)', data },
        this.breakpoints.isHandset(),
      ),
    );
    ref.afterClosed().subscribe((result) => {
      if (!result) return;
      this.submitDecision(row.id, 'approved', result.reassignments);
    });
  }

  private submitDecision(
    id: string,
    decision: 'approved' | 'rejected',
    reassignments?: ApproveLeaveDialogResult['reassignments'],
  ): void {
    this.error.set('');
    this.api.decide(id, decision, reassignments).subscribe({
      next: () => {
        this.toast.success(decision === 'approved' ? 'Leave approved.' : 'Leave rejected.');
        this.reload(false);
      },
      error: (err) => this.reportError(err),
    });
  }

  private reportError(err: unknown): void {
    const message = apiErrorMessage(err);
    this.error.set(message);
    this.toast.error(message);
  }

  private reload(initial: boolean): void {
    if (initial) {
      this.loading.set(true);
      this.historyPageIndex.set(0);
      this.myRequestsPageIndex.set(0);
    }

    const owner = this.isOwner();
    const decide = this.canDecide();
    let pendingLists = 0;
    const finishList = () => {
      pendingLists -= 1;
      if (pendingLists <= 0) {
        this.loading.set(false);
      }
    };

    if (decide) {
      this.teamBalancesLoaded.set(false);
      this.api.shopBalances(this.year).subscribe({
        next: (team) => {
          if (Array.isArray(team) && team.length > 0 && 'staffId' in team[0]) {
            this.teamBalances.set(team);
          }
          this.teamBalancesLoaded.set(true);
        },
        error: (err) => {
          this.error.set(apiErrorMessage(err));
          this.teamBalancesLoaded.set(true);
        },
      });
    }
    if (!owner) {
      this.balancesLoaded.set(false);
      this.api.balances(this.year).subscribe({
        next: (rows) => {
          this.balances.set(rows);
          this.balancesLoaded.set(true);
        },
        error: (err) => {
          this.error.set(apiErrorMessage(err));
          this.balancesLoaded.set(true);
        },
      });
    }

    if (decide) {
      pendingLists += 1;
      this.api.list(this.year).subscribe({
        next: (rows) => {
          this.shopRequests.set(rows);
          finishList();
        },
        error: (err) => {
          this.error.set(apiErrorMessage(err));
          finishList();
        },
      });
    } else {
      this.shopRequests.set([]);
    }

    if (!owner) {
      pendingLists += 1;
      this.api.list(this.year, true).subscribe({
        next: (rows) => {
          this.ownRequests.set(rows);
          finishList();
        },
        error: (err) => {
          this.error.set(apiErrorMessage(err));
          finishList();
        },
      });
    } else {
      this.ownRequests.set([]);
    }
  }
}
