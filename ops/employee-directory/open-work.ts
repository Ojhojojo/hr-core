import { availabilityWarning, type StaffAvailabilityRow } from '@hr-host';
import type {
  DeactivationSummary,
  HandoverMove,
  OpenWorkJob,
  OpenWorkLeave,
} from './employee-directory.api';

/**
 * Builds one move per open job from the desk's picks. Returns null while any job still has no
 * assignee, so the dialog can keep its submit button disabled.
 */
export function handoverMoves(
  jobs: readonly Pick<OpenWorkJob, 'jobId'>[],
  choices: Readonly<Record<string, string | undefined>>,
): HandoverMove[] | null {
  const moves: HandoverMove[] = [];
  for (const job of jobs) {
    const toStaffId = choices[job.jobId];
    if (!toStaffId) {
      return null;
    }
    moves.push({ jobId: job.jobId, toStaffId });
  }
  return moves;
}

/** Points every job at the same assignee, as the "Move all to..." picker does. */
export function applyMoveAll(
  jobs: readonly Pick<OpenWorkJob, 'jobId'>[],
  staffId: string,
): Record<string, string> {
  return Object.fromEntries(jobs.map((job) => [job.jobId, staffId]));
}

/** Picker candidates: active shop staff except the person being handed over. */
export function handoverCandidates(
  staff: readonly StaffAvailabilityRow[],
  personId: string,
): StaffAvailabilityRow[] {
  return staff.filter((row) => row.staffId !== personId);
}

/** "Name (On leave today)" style label for a picker option. */
export function pickerLabel(row: StaffAvailabilityRow): string {
  const warning = availabilityWarning(row);
  return warning ? `${row.displayName} (${warning})` : row.displayName;
}

/** Plain-language lines describing what deactivation did, for the toast after it succeeds. */
export function deactivationNotices(summary: DeactivationSummary | null | undefined): string[] {
  if (!summary) {
    return [];
  }
  const lines: string[] = [];
  if (summary.cancelledPendingLeave > 0) {
    lines.push(
      `Cancelled ${summary.cancelledPendingLeave} pending leave request${summary.cancelledPendingLeave === 1 ? '' : 's'}.`,
    );
  }
  if (summary.closedOpenPunch) {
    lines.push('Closed their open punch.');
  }
  for (const leave of summary.upcomingApprovedLeave) {
    lines.push(`Approved ${leaveLabel(leave)} stays as booked.`);
  }
  return [...lines, ...summary.notes];
}

/** "vacation 2045-06-01 to 2045-06-02" style text for a leave row. */
export function leaveLabel(leave: OpenWorkLeave): string {
  const type = leave.leaveType.replace('_', ' ');
  const span =
    leave.startDate === leave.endDate ? leave.startDate : `${leave.startDate} to ${leave.endDate}`;
  const half = leave.halfDay ? ` (${leave.halfDay.toUpperCase()})` : '';
  return `${type} leave ${span}${half}`;
}
