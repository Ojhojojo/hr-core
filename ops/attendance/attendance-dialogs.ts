import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { DateAdapter, ErrorStateMatcher } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatError, MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { BannerComponent, DialogComponent, OpsDateModule, OpsToneDirective } from '@hr-host';
import { AttendanceStaffOption } from './attendance.models';

/**
 * Typed text that is not a real day, such as 13/25/2026 (#583). Blank is left to the
 * required checks. These date inputs have no ngModel, so the check runs on each input event.
 */
function isBadTypedDate(raw: string, dates: DateAdapter<unknown>): boolean {
  const text = raw.trim();
  if (!text) {
    return false;
  }
  const parsed = dates.parse(text, 'MM/DD/YYYY');
  return parsed == null || !dates.isValid(parsed);
}

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
    MatError,
    MatFormField,
    MatLabel,
    MatInput,
    OpsDateModule,
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
        <div class="punch-datetime-row">
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>Clock in date (Manila)</mat-label>
            <input
              matInput
              #inDateText
              #inDateCtrl="matInput"
              [matDatepicker]="inDatePicker"
              name="inDate"
              placeholder="MM/DD/YYYY"
              [value]="inDate()"
              [errorStateMatcher]="inDateMatcher"
              (input)="onInDateInput(inDateText.value, inDateCtrl)"
              (dateChange)="setInDate($event.value)"
              required
            />
            <mat-datepicker-toggle matIconSuffix [for]="inDatePicker" />
            <mat-datepicker #inDatePicker />
            <mat-error>Enter a date as MM/DD/YYYY.</mat-error>
          </mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>Clock in time</mat-label>
            <input
              matInput
              name="inTime"
              type="time"
              [value]="inTime()"
              (input)="setInTime($event)"
              required
            />
          </mat-form-field>
        </div>
        <div class="punch-datetime-row">
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>Clock out date (Manila, optional)</mat-label>
            <input
              matInput
              #outDateText
              #outDateCtrl="matInput"
              [matDatepicker]="outDatePicker"
              name="outDate"
              placeholder="MM/DD/YYYY"
              [value]="outDate()"
              [errorStateMatcher]="outDateMatcher"
              (input)="onOutDateInput(outDateText.value, outDateCtrl)"
              (dateChange)="setOutDate($event.value)"
            />
            <mat-datepicker-toggle matIconSuffix [for]="outDatePicker" />
            <mat-datepicker #outDatePicker />
            <mat-error>Enter a date as MM/DD/YYYY.</mat-error>
          </mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>Clock out time (Manila, optional)</mat-label>
            <input
              matInput
              name="outTime"
              type="time"
              [value]="outTime()"
              (input)="setOutTime($event)"
            />
          </mat-form-field>
        </div>
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
    .punch-datetime-row {
      display: grid;
      grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
      gap: var(--space-2);
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
  protected readonly inDate = signal('');
  protected readonly inTime = signal('');
  protected readonly outDate = signal('');
  protected readonly outTime = signal('');
  protected readonly error = signal('');
  private readonly dates = inject(DateAdapter);
  protected readonly inDateBad = signal(false);
  protected readonly outDateBad = signal(false);
  protected readonly inDateMatcher: ErrorStateMatcher = {
    isErrorState: () => this.inDateBad(),
  };
  protected readonly outDateMatcher: ErrorStateMatcher = {
    isErrorState: () => this.outDateBad(),
  };

  protected setStaff(event: Event): void {
    this.staffId.set((event.target as HTMLSelectElement).value);
  }

  protected setInTime(event: Event): void {
    this.inTime.set((event.target as HTMLInputElement).value);
  }

  protected setOutTime(event: Event): void {
    this.outTime.set((event.target as HTMLInputElement).value);
  }

  protected onInDateInput(raw: string, field: MatInput): void {
    this.inDateBad.set(isBadTypedDate(raw, this.dates));
    field.updateErrorState();
  }

  protected onOutDateInput(raw: string, field: MatInput): void {
    this.outDateBad.set(isBadTypedDate(raw, this.dates));
    field.updateErrorState();
  }

  /** A blur with bad text reports null. Keep the typed text rather than clearing it (#583). */
  protected setInDate(value: string | null): void {
    if (value || !this.inDateBad()) {
      this.inDate.set(value ?? '');
    }
  }

  protected setOutDate(value: string | null): void {
    if (value || !this.outDateBad()) {
      this.outDate.set(value ?? '');
    }
  }

  protected save(): void {
    if (this.inDateBad() || this.outDateBad()) {
      return;
    }

    if (!this.staffId()) {
      this.error.set('Choose someone who uses the time clock.');
      return;
    }

    if (!this.inDate() || !this.inTime()) {
      this.error.set('Enter a clock-in date and time.');
      return;
    }

    const result: PunchForDialogResult = {
      staffId: this.staffId(),
      inAtManila: `${this.inDate()}T${this.inTime()}`,
    };
    if (this.outTime()) {
      const outDate = this.outDate() || this.inDate();
      result.outAtManila = `${outDate}T${this.outTime()}`;
    }

    this.dialogRef.close(result);
  }
}

@Component({
  selector: 'app-close-punch-dialog',
  imports: [
    FormsModule,
    MatButton,
    MatError,
    MatFormField,
    MatLabel,
    MatInput,
    OpsDateModule,
    DialogComponent,
    BannerComponent,
  ],
  template: `
    <ops-dialog title="Close punch" [subtitle]="data.staffName + ' clocked in ' + data.inAtManila">
      @if (error()) {
        <ops-banner tone="danger" role="alert" [message]="error()" />
      }
      <form id="close-punch-form" class="punch-dialog-form" (ngSubmit)="save()">
        <div class="punch-datetime-row">
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>Clock out date (Manila)</mat-label>
            <input
              matInput
              #closeOutText
              #closeOutCtrl="matInput"
              [matDatepicker]="closeOutPicker"
              name="outDate"
              placeholder="MM/DD/YYYY"
              [value]="outDate()"
              [errorStateMatcher]="outDateMatcher"
              (input)="onOutDateInput(closeOutText.value, closeOutCtrl)"
              (dateChange)="setOutDate($event.value)"
              required
            />
            <mat-datepicker-toggle matIconSuffix [for]="closeOutPicker" />
            <mat-datepicker #closeOutPicker />
            <mat-error>Enter a date as MM/DD/YYYY.</mat-error>
          </mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>Clock out time</mat-label>
            <input
              matInput
              name="outTime"
              type="time"
              [value]="outTime()"
              (input)="setOutTime($event)"
              required
            />
          </mat-form-field>
        </div>
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
    .punch-datetime-row {
      display: grid;
      grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
      gap: var(--space-2);
    }
    .punch-dialog-form mat-form-field {
      width: 100%;
    }
  `,
})
export class ClosePunchDialog {
  readonly dialogRef = inject(MatDialogRef<ClosePunchDialog, ClosePunchDialogResult>);
  readonly data = inject<ClosePunchDialogData>(MAT_DIALOG_DATA);
  protected readonly outDate = signal('');
  protected readonly outTime = signal('');
  protected readonly reason = signal('');
  protected readonly error = signal('');
  private readonly dates = inject(DateAdapter);
  protected readonly outDateBad = signal(false);
  protected readonly outDateMatcher: ErrorStateMatcher = {
    isErrorState: () => this.outDateBad(),
  };

  protected setOutTime(event: Event): void {
    this.outTime.set((event.target as HTMLInputElement).value);
  }

  protected setReason(event: Event): void {
    this.reason.set((event.target as HTMLInputElement).value);
  }

  protected onOutDateInput(raw: string, field: MatInput): void {
    this.outDateBad.set(isBadTypedDate(raw, this.dates));
    field.updateErrorState();
  }

  /** A blur with bad text reports null. Keep the typed text rather than clearing it (#583). */
  protected setOutDate(value: string | null): void {
    if (value || !this.outDateBad()) {
      this.outDate.set(value ?? '');
    }
  }

  protected save(): void {
    if (this.outDateBad()) {
      return;
    }

    const reason = this.reason().trim();
    if (!this.outDate() || !this.outTime()) {
      this.error.set('Enter a clock-out date and time.');
      return;
    }

    if (!reason) {
      this.error.set('A reason is required to close the punch.');
      return;
    }

    this.dialogRef.close({ outAtManila: `${this.outDate()}T${this.outTime()}`, reason });
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
    MatError,
    MatFormField,
    MatLabel,
    MatInput,
    OpsDateModule,
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
            #workDateText
            #workDateCtrl="matInput"
            matInput
            [matDatepicker]="otDatePicker"
            name="workDate"
            placeholder="MM/DD/YYYY"
            [value]="workDate()"
            [errorStateMatcher]="workDateMatcher"
            (input)="onWorkDateInput(workDateText.value, workDateCtrl)"
            (dateChange)="setWorkDate($event.value)"
            required
          />
          <mat-datepicker-toggle matIconSuffix [for]="otDatePicker" />
          <mat-datepicker #otDatePicker />
          <mat-error>Enter a date as MM/DD/YYYY.</mat-error>
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
  private readonly dates = inject(DateAdapter);
  protected readonly workDateBad = signal(false);
  protected readonly workDateMatcher: ErrorStateMatcher = {
    isErrorState: () => this.workDateBad(),
  };

  protected setDate(event: Event): void {
    this.workDate.set((event.target as HTMLInputElement).value);
  }

  protected setHours(event: Event): void {
    this.hours.set((event.target as HTMLInputElement).value);
  }

  protected setReason(event: Event): void {
    this.reason.set((event.target as HTMLInputElement).value);
  }

  protected onWorkDateInput(raw: string, field: MatInput): void {
    this.workDateBad.set(isBadTypedDate(raw, this.dates));
    field.updateErrorState();
  }

  /** A blur with bad text reports null. Keep the typed text rather than clearing it (#583). */
  protected setWorkDate(value: string | null): void {
    if (value || !this.workDateBad()) {
      this.workDate.set(value ?? '');
    }
  }

  protected save(): void {
    if (this.workDateBad()) {
      return;
    }

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
