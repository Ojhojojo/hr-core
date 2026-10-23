import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { handoverCandidates } from '../employee-directory/open-work';
import {
  apiErrorMessage,
  BannerComponent,
  DialogComponent,
  availabilityWarning,
  type StaffAvailabilityRow,
} from '@hr-host';
import {
  LeaveAffectedJob,
  LeaveApi,
  LeaveHalfDay,
  LeavePerson,
  LeaveReassignment,
  LeaveType,
  StaffLeaveBalances,
} from './leave.api';

@Component({
  selector: 'app-request-leave-dialog',
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
    <ops-dialog title="Request leave">
      @if (error()) {
        <ops-banner tone="danger" role="alert" [message]="error()" />
      }
      <form id="leave-request" class="leave-dialog-form" (ngSubmit)="submit()">
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Type</mat-label>
          <select matNativeControl name="leaveType" [(ngModel)]="leaveType">
            <option value="vacation">Vacation</option>
            <option value="sick">Sick</option>
            <option value="unpaid">Unpaid</option>
            <option value="service_incentive">Service incentive (SIL)</option>
          </select>
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Start date</mat-label>
          <input matInput name="startDate" type="date" [(ngModel)]="startDate" required />
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>End date</mat-label>
          <input matInput name="endDate" type="date" [(ngModel)]="endDate" required />
        </mat-form-field>
        @if (startDate && startDate === endDate) {
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>Half day</mat-label>
            <select matNativeControl name="halfDay" [(ngModel)]="halfDay">
              <option value="">Full day</option>
              <option value="am">Morning (AM)</option>
              <option value="pm">Afternoon (PM)</option>
            </select>
          </mat-form-field>
        }
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Note (optional)</mat-label>
          <input matInput name="note" [(ngModel)]="note" maxlength="500" />
        </mat-form-field>
      </form>
      <div dialogActions>
        <button matButton="text" type="button" (click)="dialogRef.close(false)">Cancel</button>
        <button
          matButton="filled"
          type="submit"
          form="leave-request"
          name="requestLeave"
          [disabled]="saving()"
        >
          Request
        </button>
      </div>
    </ops-dialog>
  `,
  styles: `
    .leave-dialog-form {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
    }
    .leave-dialog-form mat-form-field {
      width: 100%;
    }
  `,
})
export class RequestLeaveDialog {
  protected readonly dialogRef = inject(MatDialogRef<RequestLeaveDialog>);
  private readonly api = inject(LeaveApi);

  protected leaveType: LeaveType = 'vacation';
  protected startDate = '';
  protected endDate = '';
  protected halfDay: LeaveHalfDay | '' = '';
  protected note = '';
  protected readonly saving = signal(false);
  protected readonly error = signal('');

  protected submit(): void {
    if (!this.startDate || !this.endDate) {
      this.error.set('Start and end dates are required.');
      return;
    }

    this.saving.set(true);
    this.error.set('');
    this.api
      .create({
        leaveType: this.leaveType,
        startDate: this.startDate,
        endDate: this.endDate,
        halfDay: halfDayValue(this.startDate, this.endDate, this.halfDay),
        note: this.note ? this.note.trim() : undefined,
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.dialogRef.close(true);
        },
        error: (err) => {
          this.saving.set(false);
          this.error.set(apiErrorMessage(err));
        },
      });
  }
}

/** A half day only applies to a single date. Full day sends nothing. */
function halfDayValue(
  start: string,
  end: string,
  halfDay: LeaveHalfDay | '',
): LeaveHalfDay | undefined {
  return start && start === end && halfDay ? halfDay : undefined;
}

export interface AddLeaveDialogData {
  people: LeavePerson[];
}

@Component({
  selector: 'app-add-leave-dialog',
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
    <ops-dialog title="Add leave">
      @if (error()) {
        <ops-banner tone="danger" role="alert" [message]="error()" />
      }
      <form id="add-leave-form" class="leave-dialog-form" data-add-leave (ngSubmit)="submit()">
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Person</mat-label>
          <select matNativeControl name="addLeaveStaff" [(ngModel)]="staffId">
            @for (person of people; track person.id) {
              <option [value]="person.id">{{ person.displayName }}</option>
            }
          </select>
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Type</mat-label>
          <select matNativeControl name="addLeaveType" [(ngModel)]="leaveType">
            <option value="vacation">Vacation</option>
            <option value="sick">Sick</option>
            <option value="unpaid">Unpaid</option>
            <option value="service_incentive">Service incentive (SIL)</option>
          </select>
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Start date</mat-label>
          <input matInput name="addLeaveStartDate" type="date" [(ngModel)]="startDate" required />
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>End date</mat-label>
          <input matInput name="addLeaveEndDate" type="date" [(ngModel)]="endDate" required />
        </mat-form-field>
        @if (startDate && startDate === endDate) {
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>Half day</mat-label>
            <select matNativeControl name="addLeaveHalfDay" [(ngModel)]="halfDay">
              <option value="">Full day</option>
              <option value="am">Morning (AM)</option>
              <option value="pm">Afternoon (PM)</option>
            </select>
          </mat-form-field>
        }
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Note (optional)</mat-label>
          <input matInput name="addLeaveNote" [(ngModel)]="note" maxlength="500" />
        </mat-form-field>
      </form>
      <div dialogActions>
        <button matButton="text" type="button" (click)="dialogRef.close(false)">Cancel</button>
        <button
          matButton="filled"
          type="submit"
          form="add-leave-form"
          name="submitAddLeave"
          [disabled]="saving()"
        >
          Record leave
        </button>
      </div>
    </ops-dialog>
  `,
  styles: `
    .leave-dialog-form {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
    }
    .leave-dialog-form mat-form-field {
      width: 100%;
    }
  `,
})
export class AddLeaveDialog {
  protected readonly dialogRef = inject(MatDialogRef<AddLeaveDialog>);
  private readonly data = inject<AddLeaveDialogData>(MAT_DIALOG_DATA);
  private readonly api = inject(LeaveApi);

  protected readonly people = this.data.people;
  protected staffId = this.people[0]?.id ?? '';
  protected leaveType: LeaveType = 'vacation';
  protected startDate = '';
  protected endDate = '';
  protected halfDay: LeaveHalfDay | '' = '';
  protected note = '';
  protected readonly saving = signal(false);
  protected readonly error = signal('');

  protected submit(): void {
    if (!this.staffId) {
      this.error.set('Choose a person.');
      return;
    }
    if (!this.startDate || !this.endDate) {
      this.error.set('Start and end dates are required.');
      return;
    }

    this.saving.set(true);
    this.error.set('');
    this.api
      .recordForStaff({
        staffId: this.staffId,
        leaveType: this.leaveType,
        startDate: this.startDate,
        endDate: this.endDate,
        halfDay: halfDayValue(this.startDate, this.endDate, this.halfDay),
        note: this.note ? this.note.trim() : undefined,
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.dialogRef.close(true);
        },
        error: (err) => {
          this.saving.set(false);
          this.error.set(apiErrorMessage(err));
        },
      });
  }
}

export interface SetAllotmentDialogData {
  people: LeavePerson[];
  teamRows: StaffLeaveBalances[];
  year: number;
}

@Component({
  selector: 'app-set-allotment-dialog',
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
    <ops-dialog title="Set allotment">
      @if (error()) {
        <ops-banner tone="danger" role="alert" [message]="error()" />
      }
      <form id="leave-allotment" class="leave-dialog-form" data-allotment (ngSubmit)="submit()">
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Person</mat-label>
          <select matNativeControl name="allotmentStaff" [(ngModel)]="staffId">
            @for (person of people; track person.id) {
              <option [value]="person.id">{{ person.displayName }}</option>
            }
          </select>
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Year</mat-label>
          <input matInput name="allotmentYear" type="number" [(ngModel)]="year" required />
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Type</mat-label>
          <select matNativeControl name="allotmentType" [(ngModel)]="leaveType">
            <option value="vacation">Vacation</option>
            <option value="sick">Sick</option>
            <option value="service_incentive">Service incentive (SIL)</option>
          </select>
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Days</mat-label>
          <input
            matInput
            name="allotmentDays"
            type="number"
            min="0"
            step="0.5"
            [(ngModel)]="days"
            required
          />
        </mat-form-field>

        <div class="ops-hint-tile">
          <span class="ops-muted">Current allotted days:</span>
          <strong>{{ currentAllottedDays() }}</strong>
        </div>
      </form>
      <div dialogActions>
        <button matButton="text" type="button" (click)="dialogRef.close(false)">Cancel</button>
        <button
          matButton="filled"
          type="submit"
          form="leave-allotment"
          name="saveAllotment"
          [disabled]="saving()"
        >
          Save allotment
        </button>
      </div>
    </ops-dialog>
  `,
  styles: `
    .leave-dialog-form {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
    }
    .leave-dialog-form mat-form-field {
      width: 100%;
    }
    .ops-hint-tile {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: var(--space-3) var(--space-4);
      background: var(--ops-col-bg, #f9fafb);
      border: 1px solid var(--ops-line, #e5e7eb);
      border-radius: var(--ops-radius, 8px);
      font-size: 0.92rem;
    }
  `,
})
export class SetAllotmentDialog {
  protected readonly dialogRef = inject(MatDialogRef<SetAllotmentDialog>);
  private readonly data = inject<SetAllotmentDialogData>(MAT_DIALOG_DATA);
  private readonly api = inject(LeaveApi);

  protected readonly people = this.data.people;
  protected readonly teamRows = this.data.teamRows;
  protected staffId = this.people[0]?.id ?? '';
  protected year = this.data.year;
  protected leaveType: LeaveType = 'vacation';
  protected days = 0;
  protected readonly saving = signal(false);
  protected readonly error = signal('');

  protected currentAllottedDays = computed(() => {
    if (!this.staffId) return 0;
    const member = this.teamRows.find((m) => m.staffId === this.staffId);
    if (!member) return 0;
    const b = member.balances?.find((row) => row.leaveType === this.leaveType);
    return b?.allotment ?? 0;
  });

  protected submit(): void {
    if (!this.staffId) {
      this.error.set('Choose a person.');
      return;
    }

    this.saving.set(true);
    this.error.set('');
    this.api
      .saveAllotment({
        staffId: this.staffId,
        year: Number(this.year),
        leaveType: this.leaveType,
        days: Number(this.days),
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.dialogRef.close(true);
        },
        error: (err) => {
          this.saving.set(false);
          this.error.set(apiErrorMessage(err));
        },
      });
  }
}

export interface CancelLeaveDialogData {
  title: string;
  message: string;
  reasonRequired: boolean;
}

/**
 * Asks for a cancel reason. Closes with the trimmed reason (empty when optional and left blank),
 * or with undefined when dismissed.
 */
@Component({
  selector: 'app-cancel-leave-dialog',
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
    <ops-dialog [title]="data.title">
      @if (error()) {
        <ops-banner tone="danger" role="alert" [message]="error()" />
      }
      <p class="leave-dialog-message">{{ data.message }}</p>
      <form id="cancel-leave-form" class="leave-dialog-form" (ngSubmit)="submit()">
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>{{ data.reasonRequired ? 'Reason' : 'Reason (optional)' }}</mat-label>
          <input matInput name="cancelReason" [(ngModel)]="reason" maxlength="500" />
        </mat-form-field>
      </form>
      <div dialogActions>
        <button matButton="text" type="button" (click)="dialogRef.close()">Keep leave</button>
        <button matButton="filled" type="submit" form="cancel-leave-form" name="confirmCancelLeave">
          Cancel leave
        </button>
      </div>
    </ops-dialog>
  `,
  styles: `
    .leave-dialog-form {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
    }
    .leave-dialog-form mat-form-field {
      width: 100%;
    }
  `,
})
export class CancelLeaveDialog {
  protected readonly dialogRef = inject(MatDialogRef<CancelLeaveDialog, string | undefined>);
  protected readonly data = inject<CancelLeaveDialogData>(MAT_DIALOG_DATA);
  protected reason = '';
  protected readonly error = signal('');

  protected submit(): void {
    const reason = this.reason.trim();
    if (this.data.reasonRequired && !reason) {
      this.error.set('A reason is required to cancel approved leave.');
      return;
    }
    this.dialogRef.close(reason);
  }
}

export interface ApproveLeaveDialogData {
  requesterId: string;
  requesterName: string;
  jobs: LeaveAffectedJob[];
  /** Shop staff for the "Reassign to" picker, with today's leave and clock-in flags. */
  staff: StaffAvailabilityRow[];
}

export interface ApproveLeaveDialogResult {
  reassignments: LeaveReassignment[];
}

@Component({
  selector: 'app-approve-leave-dialog',
  imports: [FormsModule, MatButton, MatFormField, MatInput, MatLabel, DialogComponent],
  template: `
    <ops-dialog title="Approve leave">
      <p class="leave-dialog-message">
        {{ data.requesterName }} has {{ data.jobs.length }} open job(s). Move any of them, or keep
        them as they are.
      </p>
      <form id="approve-leave-form" class="leave-dialog-form" (ngSubmit)="submit()">
        @for (job of data.jobs; track job.jobId) {
          <div class="leave-reassign-row">
            <p>
              <strong>{{ job.plate }}</strong>
              · {{ job.reference }}
              @if (job.promisedDate) {
                · promised {{ job.promisedDate }}
              }
              <br />
              <small class="ops-muted"> With {{ otherNames(job) || 'nobody else' }} </small>
            </p>
            <mat-form-field appearance="outline" subscriptSizing="dynamic">
              <mat-label>Reassign to…</mat-label>
              <select
                matNativeControl
                [name]="'reassign-' + job.jobId"
                [(ngModel)]="choices[job.jobId]"
              >
                <option value="">Keep</option>
                @for (person of candidates(); track person.staffId) {
                  <option [value]="person.staffId">
                    {{ person.displayName }}{{ warningSuffix(person) }}
                  </option>
                }
              </select>
            </mat-form-field>
          </div>
        }
      </form>
      <div dialogActions>
        <button matButton="text" type="button" (click)="dialogRef.close()">Not yet</button>
        <button
          matButton="filled"
          type="submit"
          form="approve-leave-form"
          name="confirmApproveLeave"
        >
          Approve leave
        </button>
      </div>
    </ops-dialog>
  `,
  styles: `
    .leave-dialog-form {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
    }
    .leave-dialog-form mat-form-field {
      width: 100%;
    }
    .leave-reassign-row p {
      margin: 0 0 var(--space-1);
    }
  `,
})
export class ApproveLeaveDialog {
  protected readonly dialogRef = inject(MatDialogRef<ApproveLeaveDialog, ApproveLeaveDialogResult>);
  protected readonly data = inject<ApproveLeaveDialogData>(MAT_DIALOG_DATA);
  /** Chosen new assignee per job id. Empty string means keep. */
  protected readonly choices: Record<string, string> = {};
  protected readonly candidates = computed(() =>
    handoverCandidates(this.data.staff, this.data.requesterId),
  );

  protected otherNames(job: LeaveAffectedJob): string {
    return job.otherAssignees.map((person) => person.displayName).join(', ');
  }

  protected warningSuffix(person: StaffAvailabilityRow): string {
    const warning = availabilityWarning(person);
    return warning ? ` (${warning})` : '';
  }

  protected submit(): void {
    this.dialogRef.close({
      reassignments: this.data.jobs.map((job) => ({
        jobId: job.jobId,
        toStaffId: this.choices[job.jobId] || null,
      })),
    });
  }
}
