import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatCheckbox } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { apiErrorMessage, manilaToday, BannerComponent, DialogComponent } from '@hr-host';
import { Announcement } from './announcement-visibility';
import { AnnouncementsApi, AnnouncementWrite } from './announcements.api';

export interface AnnouncementDialogData {
  announcement?: Announcement | null;
}

@Component({
  selector: 'app-announcement-dialog',
  imports: [
    FormsModule,
    MatButton,
    MatFormField,
    MatLabel,
    MatInput,
    MatCheckbox,
    DialogComponent,
    BannerComponent,
  ],
  template: `
    <ops-dialog [title]="data.announcement ? 'Edit announcement' : 'New announcement'">
      @if (error()) {
        <ops-banner tone="danger" role="alert" [message]="error()" />
      }
      <form id="announcement-form" class="announcement-form" (ngSubmit)="save()">
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Title</mat-label>
          <input matInput name="title" [(ngModel)]="title" maxlength="160" required />
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Body</mat-label>
          <textarea
            matInput
            name="body"
            [(ngModel)]="body"
            rows="5"
            maxlength="4000"
            required
          ></textarea>
        </mat-form-field>
        <div class="announcement-date-row">
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>Start date</mat-label>
            <input matInput name="startDate" type="date" [(ngModel)]="startDate" required />
          </mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>End date</mat-label>
            <input matInput name="endDate" type="date" [(ngModel)]="endDate" required />
          </mat-form-field>
          <mat-checkbox name="active" [(ngModel)]="active">Active</mat-checkbox>
        </div>
      </form>
      <div dialogActions>
        <button matButton="text" type="button" (click)="dialogRef.close(false)">Cancel</button>
        <button matButton="filled" type="submit" form="announcement-form" [disabled]="saving()">
          {{ saving() ? 'Saving…' : data.announcement ? 'Save' : 'Post' }}
        </button>
      </div>
    </ops-dialog>
  `,
  styles: `
    .announcement-form {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
    }
    .announcement-form mat-form-field {
      width: 100%;
    }
    .announcement-date-row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-4);
    }
    .announcement-date-row mat-form-field {
      flex: 1 1 180px;
    }
  `,
})
export class AnnouncementDialog {
  protected readonly dialogRef = inject(MatDialogRef<AnnouncementDialog>);
  protected readonly data = inject<AnnouncementDialogData>(MAT_DIALOG_DATA);
  private readonly api = inject(AnnouncementsApi);

  private readonly today = manilaToday();
  protected title = this.data.announcement?.title ?? '';
  protected body = this.data.announcement?.body ?? '';
  protected startDate = this.data.announcement?.startDate ?? this.today;
  protected endDate = this.data.announcement?.endDate ?? this.today;
  protected active = this.data.announcement?.active ?? true;

  protected readonly saving = signal(false);
  protected readonly error = signal('');

  protected save(): void {
    if (!this.title.trim() || !this.body.trim()) {
      this.error.set('Title and body are required.');
      return;
    }

    const payload: AnnouncementWrite = {
      title: this.title.trim(),
      body: this.body.trim(),
      startDate: this.startDate,
      endDate: this.endDate,
      active: this.active,
    };

    this.saving.set(true);
    this.error.set('');

    const request = this.data.announcement?.id
      ? this.api.update(this.data.announcement.id, payload)
      : this.api.create(payload);

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.dialogRef.close(true);
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.error.set(apiErrorMessage(err));
      },
    });
  }
}
