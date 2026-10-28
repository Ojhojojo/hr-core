import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { BannerComponent, DialogComponent, OpsToneDirective } from '@hr-host';
import { AttendanceStaffOption } from './attendance.models';

export interface PunchForDialogData {
  staff: AttendanceStaffOption[];
}

export interface PunchForDialogResult {
  staffId: string;
  inAtManila: string;
  outAtManila?: string;
}

export interface ClosePunchDialogData {
  staffName: string;
  inAtManila: string;
}

export interface ClosePunchDialogResult {
  outAtManila: string;
  reason: string;
}

export interface AttendancePhotoDialogData {
  imageUrl: string;
  side: 'in' | 'out';
  staffName: string;
}

export interface ReviewPunchDialogData {
  decision: 'approved' | 'rejected';
  staffName: string;
  offsiteReason?: string | null;
}

export interface ReviewPunchDialogResult {
  note?: string;
}

@Component({
  selector: 'app-punch-for-dialog',
  imports: [
    FormsModule,
    MatButton,
    MatFormField,
    MatLabel,
    MatInput,
    DialogComponent,
    BannerComponent,
  ],
  template: `
    <ops-dialog
      title="Punch for…"
      infoTooltip="Times are Manila; leave clock-out empty to keep the shift open."
    >
      @if (error()) {
        <ops-banner tone="danger" role="alert" [message]="error()" />
      }
      <form id="punch-for-form" class="punch-dialog-form" (ngSubmit)="save()">
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Staff</mat-label>
          <select
            matNativeControl
            name="punchStaff"
            [value]="staffId()"
            (change)="setStaff($event)"
          >
            <option value="">Choose staff</option>
            @for (person of data.staff; track person.id) {
              <option [value]="person.id">{{ person.displayName }}</option>
            }
          </select>
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Clock in (Manila)</mat-label>
          <input
            matInput
            name="inAt"
            type="datetime-local"
            [value]="inAt()"
            (input)="setIn($event)"
            required
          />
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Clock out (Manila, optional)</mat-label>
          <input
            matInput
            name="outAt"
            type="datetime-local"
            [value]="outAt()"
            (input)="setOut($event)"
          />
        </mat-form-field>
      </form>
      <div dialogActions>
        <button matButton="text" type="button" (click)="dialogRef.close()">Cancel</button>
        <button matButton="filled" type="submit" form="punch-for-form">Save punch</button>
      </div>
    </ops-dialog>
  `,
  styles: `
    .punch-subtitle {
      margin: 0 0 var(--space-4);
      font-size: 0.9rem;
    }
    .punch-dialog-form {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
    }
    .punch-dialog-form mat-form-field {
      width: 100%;
    }
  `,
})
export class PunchForDialog {
  readonly dialogRef = inject(MatDialogRef<PunchForDialog, PunchForDialogResult>);
  readonly data = inject<PunchForDialogData>(MAT_DIALOG_DATA);
  protected readonly staffId = signal('');
  protected readonly inAt = signal('');
  protected readonly outAt = signal('');
  protected readonly error = signal('');

  protected setStaff(event: Event): void {
    this.staffId.set((event.target as HTMLSelectElement).value);
  }

  protected setIn(event: Event): void {
    this.inAt.set((event.target as HTMLInputElement).value);
  }

  protected setOut(event: Event): void {
    this.outAt.set((event.target as HTMLInputElement).value);
  }

  protected save(): void {
    if (!this.staffId()) {
      this.error.set('Choose someone who uses the time clock.');
      return;
    }

    if (!this.inAt()) {
      this.error.set('Enter a clock-in time.');
      return;
    }

    const result: PunchForDialogResult = {
      staffId: this.staffId(),
      inAtManila: this.inAt(),
    };
    if (this.outAt()) {
      result.outAtManila = this.outAt();
    }

    this.dialogRef.close(result);
  }
}

@Component({
  selector: 'app-close-punch-dialog',
  imports: [
    FormsModule,
    MatButton,
    MatFormField,
    MatLabel,
    MatInput,
    DialogComponent,
    BannerComponent,
  ],
  template: `
    <ops-dialog title="Close punch" [subtitle]="data.staffName + ' clocked in ' + data.inAtManila">
      @if (error()) {
        <ops-banner tone="danger" role="alert" [message]="error()" />
      }
      <form id="close-punch-form" class="punch-dialog-form" (ngSubmit)="save()">
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Clock out (Manila)</mat-label>
          <input
            matInput
            name="outAt"
            type="datetime-local"
            [value]="outAt()"
            (input)="setOut($event)"
            required
          />
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Reason</mat-label>
          <input matInput name="reason" [value]="reason()" (input)="setReason($event)" required />
        </mat-form-field>
      </form>
      <div dialogActions>
        <button matButton="text" type="button" (click)="dialogRef.close()">Cancel</button>
        <button matButton="filled" type="submit" form="close-punch-form">Close punch</button>
      </div>
    </ops-dialog>
  `,
  styles: `
    .punch-subtitle {
      margin: 0 0 var(--space-4);
      font-size: 0.9rem;
    }
    .punch-dialog-form {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
    }
    .punch-dialog-form mat-form-field {
      width: 100%;
    }
  `,
})
export class ClosePunchDialog {
  readonly dialogRef = inject(MatDialogRef<ClosePunchDialog, ClosePunchDialogResult>);
  readonly data = inject<ClosePunchDialogData>(MAT_DIALOG_DATA);
  protected readonly outAt = signal('');
  protected readonly reason = signal('');
  protected readonly error = signal('');

  protected setOut(event: Event): void {
    this.outAt.set((event.target as HTMLInputElement).value);
  }

  protected setReason(event: Event): void {
    this.reason.set((event.target as HTMLInputElement).value);
  }

  protected save(): void {
    const reason = this.reason().trim();
    if (!this.outAt()) {
      this.error.set('Enter a clock-out time.');
      return;
    }

    if (!reason) {
      this.error.set('A reason is required to close the punch.');
      return;
    }

    this.dialogRef.close({ outAtManila: this.outAt(), reason });
  }
}

@Component({
  selector: 'app-attendance-photo-dialog',
  imports: [MatButton, DialogComponent],
  template: `
    <ops-dialog
      [title]="data.staffName + ' · ' + (data.side === 'in' ? 'Clock in photo' : 'Clock out photo')"
    >
      <div class="attendance-photo-container">
        <img [src]="data.imageUrl" alt="Shift photo" class="attendance-photo-img" />
      </div>
      <div dialogActions>
        <button matButton="text" type="button" name="closePhoto" (click)="dialogRef.close()">
          Close
        </button>
      </div>
    </ops-dialog>
  `,
  styles: `
    .attendance-photo-container {
      display: flex;
      justify-content: center;
      align-items: center;
      padding: var(--space-2) 0;
    }
    .attendance-photo-img {
      max-width: 100%;
      max-height: 70vh;
      border-radius: var(--ops-radius, 8px);
      object-fit: contain;
    }
  `,
})
export class AttendancePhotoDialog {
  readonly dialogRef = inject(MatDialogRef<AttendancePhotoDialog>);
  readonly data = inject<AttendancePhotoDialogData>(MAT_DIALOG_DATA);
}

@Component({
  selector: 'app-review-punch-dialog',
  imports: [
    FormsModule,
    MatButton,
    MatFormField,
    MatLabel,
    MatInput,
    DialogComponent,
    OpsToneDirective,
  ],
  template: `
    <ops-dialog
      [title]="title"
      [subtitle]="data.offsiteReason ? data.staffName + ' · ' + data.offsiteReason : data.staffName"
    >
      <form id="review-punch-form" class="punch-dialog-form" (ngSubmit)="save()">
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Note (optional)</mat-label>
          <textarea
            matInput
            name="note"
            rows="3"
            [value]="note()"
            (input)="setNote($event)"
          ></textarea>
        </mat-form-field>
      </form>
      <div dialogActions>
        <button matButton="text" type="button" (click)="dialogRef.close()">Cancel</button>
        <button
          matButton="filled"
          type="submit"
          form="review-punch-form"
          [opsTone]="data.decision === 'rejected' ? 'danger' : null"
        >
          {{ confirmLabel }}
        </button>
      </div>
    </ops-dialog>
  `,
  styles: `
    .punch-subtitle {
      margin: 0 0 var(--space-4);
      font-size: 0.9rem;
    }
    .punch-dialog-form {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
    }
    .punch-dialog-form mat-form-field {
      width: 100%;
    }
  `,
})
export class ReviewPunchDialog {
  readonly dialogRef = inject(MatDialogRef<ReviewPunchDialog, ReviewPunchDialogResult>);
  readonly data = inject<ReviewPunchDialogData>(MAT_DIALOG_DATA);
  protected readonly note = signal('');
  protected readonly title = this.data.decision === 'approved' ? 'Approve punch' : 'Reject punch';
  protected readonly confirmLabel = this.data.decision === 'approved' ? 'Approve' : 'Reject';

  protected setNote(event: Event): void {
    this.note.set((event.target as HTMLTextAreaElement).value);
  }

  protected save(): void {
    const note = this.note().trim();
    this.dialogRef.close(note ? { note } : {});
  }
}

export interface OvertimeRequestDialogResult {
  workDate: string;
  hours: number;
  reason: string;
}

export interface OvertimeDecisionDialogData {
  decision: 'approved' | 'rejected';
  staffName: string;
  claimedHours: number;
}

export interface OvertimeDecisionDialogResult {
  hours?: number;
  note?: string;
}

@Component({
  selector: 'app-overtime-request-dialog',
  imports: [
    FormsModule,
    MatButton,
    MatFormField,
    MatLabel,
    MatInput,
    DialogComponent,
    BannerComponent,
  ],
  template: `
    <ops-dialog
      title="Request overtime"
      infoTooltip="Pay still stops at the hours you actually punch."
    >
      @if (error()) {
        <ops-banner tone="danger" role="alert" [message]="error()" />
      }
      <form id="ot-request-form" class="punch-dialog-form" (ngSubmit)="save()">
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Date</mat-label>
          <input
            matInput
            name="workDate"
            type="date"
            [value]="workDate()"
            (input)="setDate($event)"
            required
          />
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Hours</mat-label>
          <input
            matInput
            name="hours"
            type="number"
            min="0.5"
            max="24"
            step="0.5"
            [value]="hours()"
            (input)="setHours($event)"
            required
          />
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Reason</mat-label>
          <input matInput name="reason" [value]="reason()" (input)="setReason($event)" required />
        </mat-form-field>
      </form>
      <div dialogActions>
        <button matButton="text" type="button" (click)="dialogRef.close()">Cancel</button>
        <button matButton="filled" type="submit" form="ot-request-form">File request</button>
      </div>
    </ops-dialog>
  `,
  styles: `
    .punch-subtitle {
      margin: 0 0 var(--space-4);
      font-size: 0.9rem;
    }
    .punch-dialog-form {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
    }
    .punch-dialog-form mat-form-field {
      width: 100%;
    }
  `,
})
export class OvertimeRequestDialog {
  readonly dialogRef = inject(MatDialogRef<OvertimeRequestDialog, OvertimeRequestDialogResult>);
  protected readonly error = signal('');
  protected readonly workDate = signal('');
  protected readonly hours = signal('2');
  protected readonly reason = signal('');

  protected setDate(event: Event): void {
    this.workDate.set((event.target as HTMLInputElement).value);
  }

  protected setHours(event: Event): void {
    this.hours.set((event.target as HTMLInputElement).value);
  }

  protected setReason(event: Event): void {
    this.reason.set((event.target as HTMLInputElement).value);
  }

  protected save(): void {
    const hours = Number(this.hours());
    const reason = this.reason().trim();
    if (!this.workDate() || !Number.isFinite(hours) || hours <= 0 || hours > 24 || !reason) {
      this.error.set('Enter a date, hours, and a reason.');
      return;
    }
    this.dialogRef.close({ workDate: this.workDate(), hours, reason });
  }
}

@Component({
  selector: 'app-overtime-decision-dialog',
  imports: [
    FormsModule,
    MatButton,
    MatFormField,
    MatLabel,
    MatInput,
    DialogComponent,
    BannerComponent,
    OpsToneDirective,
  ],
  template: `
    <ops-dialog
      [title]="title"
      [subtitle]="data.staffName + ' · ' + data.claimedHours + ' hrs claimed'"
    >
      @if (error()) {
        <ops-banner tone="danger" role="alert" [message]="error()" />
      }
      <form id="ot-decision-form" class="punch-dialog-form" (ngSubmit)="save()">
        @if (data.decision === 'approved') {
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>Hours to approve</mat-label>
            <input
              matInput
              name="hours"
              type="number"
              min="0.01"
              step="0.25"
              [value]="hours()"
              (input)="setHours($event)"
              required
            />
          </mat-form-field>
        }
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Note (optional)</mat-label>
          <input matInput name="note" [value]="note()" (input)="setNote($event)" />
        </mat-form-field>
      </form>
      <div dialogActions>
        <button matButton="text" type="button" (click)="dialogRef.close()">Cancel</button>
        <button
          matButton="filled"
          type="submit"
          form="ot-decision-form"
          [opsTone]="data.decision === 'rejected' ? 'danger' : null"
        >
          {{ confirmLabel }}
        </button>
      </div>
    </ops-dialog>
  `,
  styles: `
    .punch-subtitle {
      margin: 0 0 var(--space-4);
      font-size: 0.9rem;
    }
    .punch-dialog-form {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
    }
    .punch-dialog-form mat-form-field {
      width: 100%;
    }
  `,
})
export class OvertimeDecisionDialog {
  readonly dialogRef = inject(MatDialogRef<OvertimeDecisionDialog, OvertimeDecisionDialogResult>);
  readonly data = inject<OvertimeDecisionDialogData>(MAT_DIALOG_DATA);
  protected readonly error = signal('');
  protected readonly hours = signal(String(this.data.claimedHours));
  protected readonly note = signal('');
  protected readonly title =
    this.data.decision === 'approved' ? 'Approve overtime' : 'Reject overtime';
  protected readonly confirmLabel = this.data.decision === 'approved' ? 'Approve' : 'Reject';

  protected setHours(event: Event): void {
    this.hours.set((event.target as HTMLInputElement).value);
  }

  protected setNote(event: Event): void {
    this.note.set((event.target as HTMLInputElement).value);
  }

  protected save(): void {
    const note = this.note().trim();
    if (this.data.decision === 'rejected') {
      this.dialogRef.close({ note: note || undefined });
      return;
    }
    const hours = Number(this.hours());
    if (!Number.isFinite(hours) || hours <= 0 || hours > this.data.claimedHours) {
      this.error.set('Approved hours must be greater than 0 and no more than the claimed hours.');
      return;
    }
    this.dialogRef.close({ hours, note: note || undefined });
  }
}
