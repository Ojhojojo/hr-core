import { Component, inject, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { RouterLink } from '@angular/router';
import {
  apiErrorMessage,
  manilaToday,
  BannerComponent,
  OpsConfirmService,
  OpsToastService,
  PageHeaderComponent,
  SkeletonComponent,
  StatusBadgeComponent,
} from '@hr-host';
import { Announcement, announcementStatus, AnnouncementStatus } from './announcement-visibility';
import { AnnouncementDialog } from './announcement-dialog';
import { AnnouncementsApi } from './announcements.api';

@Component({
  selector: 'app-ops-announcements',
  imports: [
    MatButton,
    RouterLink,
    PageHeaderComponent,
    BannerComponent,
    SkeletonComponent,
    StatusBadgeComponent,
  ],
  templateUrl: './announcements.html',
  styles: `
    .announcements-loading-wrap {
      padding: var(--space-4);
    }

    .announcement-row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-3) var(--space-4);
      padding: var(--space-3) 0;
    }

    .announcement-meta {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2) var(--space-3);
      min-width: 0;
      overflow-wrap: anywhere;
    }

    .announcement-dates {
      color: var(--ops-muted);
      font-size: 0.95rem;
    }

    .announcement-row .ops-actions {
      flex: 0 0 auto;
      margin-left: auto;
    }
  `,
})
export class AnnouncementsPage {
  private readonly api = inject(AnnouncementsApi);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(OpsToastService);
  private readonly confirm = inject(OpsConfirmService);

  protected readonly today = manilaToday();
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly rows = signal<Announcement[]>([]);

  protected statusOf(row: Announcement): AnnouncementStatus {
    return announcementStatus(row, this.today);
  }

  constructor() {
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.error.set('');
    this.api.listAll().subscribe({
      next: (rows) => {
        this.rows.set(rows);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.error.set(apiErrorMessage(err));
        this.loading.set(false);
      },
    });
  }

  edit(row: Announcement): void {
    const ref = this.dialog.open(AnnouncementDialog, {
      width: 'min(48rem, 78vw)',
      maxWidth: 'calc(100vw - 32px)',
      data: { announcement: row },
    });
    ref.afterClosed().subscribe((saved) => {
      if (saved) {
        this.toast.success('Announcement updated.');
        this.reload();
      }
    });
  }

  openPost(): void {
    const ref = this.dialog.open(AnnouncementDialog, {
      width: 'min(48rem, 78vw)',
      maxWidth: 'calc(100vw - 32px)',
      data: { announcement: null },
    });
    ref.afterClosed().subscribe((saved) => {
      if (saved) {
        this.toast.success('Announcement posted.');
        this.reload();
      }
    });
  }

  remove(row: Announcement): void {
    this.confirm
      .confirm({
        title: `Remove “${row.title}”?`,
        message: 'The announcement is deleted for everyone. This cannot be undone.',
        confirmLabel: 'Remove',
        tone: 'danger',
      })
      .subscribe((result) => {
        if (result.confirmed) {
          this.removeNow(row);
        }
      });
  }

  private removeNow(row: Announcement): void {
    this.error.set('');
    this.api.remove(row.id).subscribe({
      next: () => {
        this.toast.success('Announcement removed.');
        this.reload();
      },
      error: (err: unknown) => {
        const message = apiErrorMessage(err);
        this.error.set(message);
        this.toast.error(message);
      },
    });
  }
}
