import { Component, computed, inject, signal } from '@angular/core';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import {
  OpsBreakpoints,
  fitDialogToHandset,
  AuthService,
  apiErrorMessage,
  addDays,
  manilaToday,
  BannerComponent,
  CardComponent,
  EmptyStateComponent,
  OpsToastService,
  PageHeaderComponent,
  PeriodNavComponent,
  PeriodRange,
  SkeletonComponent,
  StatusBadgeComponent,
} from '@hr-host';
import { MatChipListbox, MatChipListboxChange, MatChipOption } from '@angular/material/chips';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatOption, MatSelect } from '@angular/material/select';
import { MatMenu, MatMenuItem, MatMenuTrigger } from '@angular/material/menu';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatTableModule } from '@angular/material/table';
import { AttendanceApi } from './attendance.api';
import {
  AttendancePhotoDialog,
  ClosePunchDialog,
  OvertimeDecisionDialog,
  OvertimeRequestDialog,
  PunchForDialog,
  ReviewPunchDialog,
} from './attendance-dialogs';
import {
  AttendanceApprovalFilter,
  AttendanceRegisterQuery,
  AttendanceRegisterRow,
  AttendanceStaffOption,
  OvertimeRow,
} from './attendance.models';
import { canReviewPunch, punchStatusLabel, punchStatusTone } from './punch-review';
import { formatDay, formatDayPrefix } from '../hr-dates';

@Component({
  selector: 'app-ops-attendance',
  imports: [
    MatButton,
    MatIconButton,
    MatChipListbox,
    MatChipOption,
    MatFormField,
    MatLabel,
    MatSelect,
    MatOption,
    MatMenu,
    MatMenuItem,
    MatMenuTrigger,
    MatPaginatorModule,
    MatTableModule,
    PageHeaderComponent,
    CardComponent,
    EmptyStateComponent,
    BannerComponent,
    SkeletonComponent,
    PeriodNavComponent,
    StatusBadgeComponent,
  ],
  templateUrl: './attendance.html',
  styles: [
    `
      .ops-attend-toolbar {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        justify-content: space-between;
        gap: var(--space-4);
        margin-bottom: var(--space-6);
      }
      .ops-attend-staff-select {
        min-width: 200px;
      }
      .attendance-loading-wrap {
        padding: var(--space-4);
      }
      /* Tables stack into cards below 900px (see shell/ops-theme.scss); the min-width only
         makes sense while the table is a real table that scrolls sideways. */
      @media (min-width: 900.02px) {
        /* The staff name stays on one readable line while the table scrolls sideways. */
        .mat-column-name {
          min-width: 10rem;
          overflow-wrap: anywhere;
        }
        /* Room for the badge plus a wrapped offsite/review note. */
        .ops-attend-table .mat-column-status {
          min-width: 16rem;
        }
        /* Sticky name column with an opaque background so scrolled columns slide under it. */
        .ops-attend-table .mat-mdc-table-sticky {
          position: sticky;
          left: 0;
          z-index: 1;
          background: var(--mat-sys-surface, var(--ops-card, #fff));
          box-shadow: 2px 0 4px rgba(40, 48, 64, 0.08);
        }
        .ops-attend-table th.mat-mdc-table-sticky {
          z-index: 2;
        }
      }
      .mat-column-actions {
        text-align: right;
      }
      /* Column widths apply to the real table only; below 900px rows collapse to cards. */
      @media (min-width: 900.02px) {
        .mat-column-name {
          width: 25%;
          min-width: 160px;
        }
        .mat-column-branch {
          width: 20%;
          min-width: 140px;
        }
        .mat-column-in,
        .mat-column-out {
          width: 22%;
          min-width: 150px;
        }
        .mat-column-hours {
          width: 11%;
          min-width: 80px;
        }
        .mat-column-status {
          min-width: 110px;
        }
        .mat-column-actions {
          min-width: 160px;
        }
      }
      .text-right {
        text-align: right;
      }
      .ops-attend-pending-badge {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        min-width: 1.25rem;
        height: 1.25rem;
        margin-left: var(--space-1);
        padding: 0 var(--space-1);
        border-radius: 999px;
        background: var(--ops-warn-bg, #fffaeb);
        color: var(--ops-warn, #b54708);
        font-size: 0.75rem;
        font-weight: 700;
      }
      .ops-attend-actions-cell {
        display: flex;
        align-items: center;
        justify-content: flex-end;
        flex-wrap: wrap;
        gap: var(--space-2);
      }
      .ops-attend-reason {
        display: block;
        margin-top: var(--space-1);
        max-width: 20rem;
        white-space: normal;
        overflow-wrap: anywhere;
        color: var(--ops-muted);
        font-size: 0.8rem;
      }
    `,
  ],
})
export class AttendancePage {
  protected readonly formatDay = formatDay;
  protected readonly formatDayPrefix = formatDayPrefix;
  private readonly api = inject(AttendanceApi);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly breakpoints = inject(OpsBreakpoints);
  private readonly toast = inject(OpsToastService);

  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly staffId = signal('');
  protected readonly approvalFilter = signal<'all' | AttendanceApprovalFilter>('all');
  protected readonly staff = signal<AttendanceStaffOption[]>([]);
  protected readonly rows = signal<AttendanceRegisterRow[]>([]);
  protected readonly overtimeRows = signal<OvertimeRow[]>([]);
  protected readonly myOvertimeRows = signal<OvertimeRow[]>([]);
  protected readonly overtimeLoading = signal(true);
  protected readonly overtimeColumns = ['name', 'date', 'kind', 'hours', 'status', 'actions'];
  protected readonly myOvertimeColumns = ['date', 'kind', 'hours', 'status'];
  protected readonly columns = computed(() => {
    const cols = ['name', 'branch', 'in', 'out', 'hours', 'status'];
    return this.canManage() ? [...cols, 'actions'] : cols;
  });
  protected readonly loading = signal(true);
  protected readonly downloading = signal(false);
  protected readonly error = signal('');
  /** True while a review, overtime decision or close is open or saving, so a second tap does nothing. */
  protected readonly acting = signal(false);

  protected readonly pageIndex = signal(0);
  protected readonly pageSize = signal(10);
  protected readonly pageSizeOptions = [10, 25, 50];
  protected readonly pagedRows = computed(() => {
    const items = this.rows();
    const size = this.pageSize();
    const maxPage = Math.max(0, Math.ceil(items.length / size) - 1);
    const page = Math.min(this.pageIndex(), maxPage);
    const start = page * size;
    return items.slice(start, start + size);
  });

  protected readonly role = computed(() => this.auth.me()?.staffRole);
  protected readonly canManage = computed(() => {
    const role = this.role();
    return role === 'owner' || role === 'admin';
  });
  protected readonly canFileOvertime = computed(() => this.role() !== 'owner');
  protected readonly punchStaff = computed(() =>
    this.staff().filter((person) => person.mustClock === true && person.role !== 'owner'),
  );
  protected readonly pendingCount = computed(
    () => this.rows().filter((row) => row.approvalStatus === 'pending').length,
  );
  protected readonly punchStatusLabel = punchStatusLabel;
  protected readonly punchStatusTone = punchStatusTone;
  protected readonly canReviewPunch = canReviewPunch;
  protected readonly title = computed(() => (this.canManage() ? 'Attendance' : 'My week'));

  constructor() {
    if (this.canManage()) {
      this.api.listStaff().subscribe({
        next: (staff) => this.staff.set(staff),
        error: (err: unknown) => this.error.set(apiErrorMessage(err)),
      });
    }
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.overtimeLoading.set(true);
    this.error.set('');
    this.pageIndex.set(0);
    this.api.register(this.registerQuery()).subscribe({
      next: (body) => {
        this.from.set(body.from);
        this.to.set(body.to);
        this.rows.set(body.rows);
        this.loading.set(false);
        this.loadOvertime(body.from, body.to);
      },
      error: (err: unknown) => {
        this.error.set(apiErrorMessage(err));
        this.loading.set(false);
        this.overtimeLoading.set(false);
      },
    });
  }

  loadOvertime(from: string, to: string): void {
    this.overtimeLoading.set(true);
    let pending = this.role() === 'admin' ? 2 : 1;
    const done = () => {
      pending -= 1;
      if (pending <= 0) {
        this.overtimeLoading.set(false);
      }
    };

    this.api.overtime(from, to).subscribe({
      next: (body) => {
        this.overtimeRows.set(body.rows);
        done();
      },
      error: (err: unknown) => {
        this.error.set(apiErrorMessage(err));
        done();
      },
    });

    if (this.role() === 'admin') {
      this.api.overtime(from, to, true).subscribe({
        next: (body) => {
          this.myOvertimeRows.set(body.rows);
          done();
        },
        error: (err: unknown) => {
          this.error.set(apiErrorMessage(err));
          done();
        },
      });
    }
  }

  requestOvertime(): void {
    const ref = this.dialog.open(
      OvertimeRequestDialog,
      fitDialogToHandset(
        {
          width: 'min(28rem, 78vw)',
        },
        this.breakpoints.isHandset(),
      ),
    );
    ref.afterClosed().subscribe((result) => {
      if (!result) {
        return;
      }
      this.api.fileOvertime(result).subscribe({
        next: () => {
          this.toast.success('Overtime request filed.');
          this.loadOvertime(this.from(), this.to());
        },
        error: (err: unknown) => this.error.set(apiErrorMessage(err)),
      });
    });
  }

  decideOvertime(row: OvertimeRow, decision: 'approved' | 'rejected'): void {
    if (!row.canApprove || this.acting()) {
      return;
    }
    this.acting.set(true);
    const ref = this.dialog.open(
      OvertimeDecisionDialog,
      fitDialogToHandset(
        {
          width: 'min(28rem, 78vw)',
          data: {
            decision,
            staffName: row.staffName,
            claimedHours: row.claimedHours,
          },
        },
        this.breakpoints.isHandset(),
      ),
    );
    ref.afterClosed().subscribe((result) => {
      if (!result) {
        this.acting.set(false);
        return;
      }
      const call =
        decision === 'approved'
          ? this.api.approveOvertime(row.id, result.hours ?? row.claimedHours, result.note)
          : this.api.rejectOvertime(row.id, result.note);
      call.subscribe({
        next: () => {
          this.acting.set(false);
          this.toast.success(decision === 'approved' ? 'Overtime approved.' : 'Overtime rejected.');
          this.load();
        },
        error: (err: unknown) => {
          this.acting.set(false);
          this.error.set(apiErrorMessage(err));
        },
      });
    });
  }

  overtimeKind(row: OvertimeRow): string {
    return row.kind === 'request' ? 'Request' : 'Punch';
  }

  overtimeHours(row: OvertimeRow): string {
    const claimed = Number(row.claimedHours).toFixed(2);
    if (row.status === 'approved' && row.approvedHours != null) {
      return `${Number(row.approvedHours).toFixed(2)} of ${claimed}`;
    }
    return claimed;
  }

  onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
  }

  onPeriodChange(range: PeriodRange): void {
    this.from.set(range.from);
    this.to.set(range.to);
    this.load();
  }

  setStaff(staffId: string): void {
    this.staffId.set(staffId);
    this.load();
  }

  onApprovalFilter(event: MatChipListboxChange): void {
    const value = event.value as 'all' | AttendanceApprovalFilter | undefined;
    this.approvalFilter.set(value || 'all');
    this.load();
  }

  review(row: AttendanceRegisterRow, decision: 'approved' | 'rejected'): void {
    if (!row.punchId || !canReviewPunch(this.role(), row.approvalStatus) || this.acting()) {
      return;
    }
    this.acting.set(true);

    const punchId = row.punchId;
    const ref = this.dialog.open(
      ReviewPunchDialog,
      fitDialogToHandset(
        {
          width: 'min(32rem, 78vw)',
          maxWidth: 'calc(100vw - 32px)',
          data: {
            decision,
            staffName: row.staffName,
            offsiteReason: row.offsiteReason,
          },
        },
        this.breakpoints.isHandset(),
      ),
    );
    ref.afterClosed().subscribe((result) => {
      if (result == null || !punchId) {
        this.acting.set(false);
        return;
      }

      this.error.set('');
      const call =
        decision === 'approved'
          ? this.api.approve(punchId, result.note)
          : this.api.reject(punchId, result.note);
      call.subscribe({
        next: () => {
          this.acting.set(false);
          this.toast.success(decision === 'approved' ? 'Punch approved.' : 'Punch rejected.');
          this.load();
        },
        error: (err: unknown) => {
          this.acting.set(false);
          this.error.set(apiErrorMessage(err));
        },
      });
    });
  }

  punchFor(): void {
    const ref = this.dialog.open(
      PunchForDialog,
      fitDialogToHandset(
        {
          width: 'min(32rem, 78vw)',
          maxWidth: 'calc(100vw - 32px)',
          data: { staff: this.punchStaff() },
        },
        this.breakpoints.isHandset(),
      ),
    );
    ref.afterClosed().subscribe((result) => {
      if (!result) {
        return;
      }

      this.error.set('');
      this.api.punchFor(result).subscribe({
        next: () => this.load(),
        error: (err: unknown) => this.error.set(apiErrorMessage(err)),
      });
    });
  }

  closePunch(row: AttendanceRegisterRow): void {
    if (!row.punchId || this.acting()) {
      return;
    }
    this.acting.set(true);

    const ref = this.dialog.open(
      ClosePunchDialog,
      fitDialogToHandset(
        {
          width: 'min(32rem, 78vw)',
          maxWidth: 'calc(100vw - 32px)',
          data: { staffName: row.staffName, inAtManila: row.inAtManila },
        },
        this.breakpoints.isHandset(),
      ),
    );
    ref.afterClosed().subscribe((result) => {
      if (!result || !row.punchId) {
        this.acting.set(false);
        return;
      }

      this.error.set('');
      this.api.closePunch(row.punchId, result).subscribe({
        next: () => {
          this.acting.set(false);
          this.load();
        },
        error: (err: unknown) => {
          this.acting.set(false);
          this.error.set(apiErrorMessage(err));
        },
      });
    });
  }

  hoursLabel(row: AttendanceRegisterRow): string {
    if (row.isOpen || row.hours == null) {
      return '—';
    }
    return row.hours.toFixed(2);
  }

  outLabel(row: AttendanceRegisterRow): string {
    return row.isOpen || !row.outAtManila ? 'Open' : formatDayPrefix(row.outAtManila);
  }

  openPhoto(row: AttendanceRegisterRow, side: 'in' | 'out'): void {
    if (!row.punchId) {
      return;
    }

    this.error.set('');
    this.api.photo(row.punchId, side).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const ref = this.dialog.open(
          AttendancePhotoDialog,
          fitDialogToHandset(
            {
              width: 'min(32rem, 78vw)',
              maxWidth: 'calc(100vw - 32px)',
              data: {
                imageUrl: url,
                side,
                staffName: row.staffName,
              },
            },
            this.breakpoints.isHandset(),
          ),
        );
        ref.afterClosed().subscribe(() => {
          URL.revokeObjectURL(url);
        });
      },
      error: (err: unknown) => this.error.set(apiErrorMessage(err)),
    });
  }

  downloadCsv(): void {
    this.downloading.set(true);
    this.error.set('');
    this.api.downloadCsv(this.registerQuery()).subscribe({
      next: (response) => {
        const blob = response.body;
        if (!blob) {
          this.error.set('Empty CSV.');
          this.downloading.set(false);
          return;
        }

        const fileName =
          parseFileName(response.headers.get('content-disposition')) ??
          `rakkii-attendance-${this.from() || manilaToday()}-${this.to() || manilaToday()}.csv`;
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = fileName;
        link.click();
        setTimeout(() => URL.revokeObjectURL(url), 60_000);
        this.downloading.set(false);
      },
      error: (err: unknown) => {
        this.error.set(apiErrorMessage(err));
        this.downloading.set(false);
      },
    });
  }

  private registerQuery(): AttendanceRegisterQuery {
    const approval = this.approvalFilter();
    return {
      from: this.from() || undefined,
      to: this.to() || undefined,
      staffId: this.canManage() ? this.staffId() || undefined : undefined,
      approval: this.canManage() && approval !== 'all' ? approval : undefined,
    };
  }
}

/**
 * Monday through Sunday, the same week the API uses (ManilaTime.CurrentWeek) and the shop
 * default week start. Payroll and the "This week" button use it, so every HR screen agrees.
 */
export function manilaWeekRange(anchor = manilaToday()): { from: string; to: string } {
  const [year, month, day] = anchor.split('-').map(Number);
  const dow = new Date(Date.UTC(year, month - 1, day)).getUTCDay();
  const sinceMonday = (dow + 6) % 7;
  const from = addDays(anchor, -sinceMonday);
  return { from, to: addDays(from, 6) };
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
