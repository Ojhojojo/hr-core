import { describe, expect, it } from 'vitest';
import type { StaffAvailabilityRow } from '@hr-host';
import type { DeactivationSummary, OpenWorkLeave } from './employee-directory.api';
import {
  applyMoveAll,
  deactivationNotices,
  handoverCandidates,
  handoverMoves,
  leaveLabel,
  pickerLabel,
} from './open-work';

const jobs = [{ jobId: 'j-1' }, { jobId: 'j-2' }];

function person(overrides: Partial<StaffAvailabilityRow>): StaffAvailabilityRow {
  return {
    staffId: 's-1',
    displayName: 'Ana',
    role: 'technician',
    onApprovedLeave: false,
    leaveType: null,
    halfDay: null,
    clockedIn: true,
    ...overrides,
  };
}

describe('handoverMoves', () => {
  it('returns one move per job once every job has a pick', () => {
    expect(handoverMoves(jobs, { 'j-1': 's-2', 'j-2': 's-3' })).toEqual([
      { jobId: 'j-1', toStaffId: 's-2' },
      { jobId: 'j-2', toStaffId: 's-3' },
    ]);
  });

  it('returns null while any job is still unassigned or blank', () => {
    expect(handoverMoves(jobs, { 'j-1': 's-2' })).toBeNull();
    expect(handoverMoves(jobs, { 'j-1': 's-2', 'j-2': '' })).toBeNull();
  });

  it('returns an empty list when there are no jobs', () => {
    expect(handoverMoves([], {})).toEqual([]);
  });
});

describe('applyMoveAll', () => {
  it('points every job at the same assignee', () => {
    expect(applyMoveAll(jobs, 's-9')).toEqual({ 'j-1': 's-9', 'j-2': 's-9' });
  });
});

describe('handoverCandidates and pickerLabel', () => {
  it('leaves out the person being handed over', () => {
    const rows = [person({ staffId: 'me' }), person({ staffId: 'other', displayName: 'Ben' })];
    expect(handoverCandidates(rows, 'me').map((r) => r.staffId)).toEqual(['other']);
  });

  it('leaves out sales staff, who cannot open the workshop', () => {
    const rows = [
      person({ staffId: 'tech', role: 'technician' }),
      person({ staffId: 'desk', role: 'admin' }),
      person({ staffId: 'boss', role: 'owner' }),
      person({ staffId: 'seller', role: 'sales' }),
    ];
    expect(handoverCandidates(rows, 'me').map((r) => r.staffId)).toEqual(['tech', 'desk', 'boss']);
  });

  it('adds the availability hint to the label', () => {
    expect(pickerLabel(person({ displayName: 'Ana' }))).toBe('Ana');
    expect(pickerLabel(person({ displayName: 'Ana', clockedIn: false }))).toBe(
      'Ana (Not clocked in today)',
    );
    expect(pickerLabel(person({ displayName: 'Ana', onApprovedLeave: true, halfDay: 'am' }))).toBe(
      'Ana (On leave today (AM))',
    );
  });
});

describe('leaveLabel', () => {
  it('reads a single day, a span, and a half day', () => {
    const base: OpenWorkLeave = {
      id: 'l-1',
      leaveType: 'service_incentive',
      startDate: '2045-06-01',
      endDate: '2045-06-01',
      halfDay: null,
    };
    expect(leaveLabel(base)).toBe('service incentive leave 2045-06-01');
    expect(leaveLabel({ ...base, leaveType: 'vacation', endDate: '2045-06-03' })).toBe(
      'vacation leave 2045-06-01 to 2045-06-03',
    );
    expect(leaveLabel({ ...base, leaveType: 'sick', halfDay: 'pm' })).toBe(
      'sick leave 2045-06-01 (PM)',
    );
  });
});

describe('deactivationNotices', () => {
  const none: DeactivationSummary = {
    cancelledPendingLeave: 0,
    closedOpenPunch: false,
    upcomingApprovedLeave: [],
    notes: [],
  };

  it('is empty when nothing happened or the summary is missing', () => {
    expect(deactivationNotices(undefined)).toEqual([]);
    expect(deactivationNotices(null)).toEqual([]);
    expect(deactivationNotices(none)).toEqual([]);
  });

  it('describes cancelled leave, the closed punch, upcoming leave and server notes', () => {
    const summary: DeactivationSummary = {
      cancelledPendingLeave: 2,
      closedOpenPunch: true,
      upcomingApprovedLeave: [
        {
          id: 'l-2',
          leaveType: 'vacation',
          startDate: '2045-06-01',
          endDate: '2045-06-02',
          halfDay: null,
        },
      ],
      notes: ['The open punch from 2045-01-02 is in a locked payroll period and was left open.'],
    };
    expect(deactivationNotices(summary)).toEqual([
      'Cancelled 2 pending leave requests.',
      'Closed their open punch.',
      'Approved vacation leave 2045-06-01 to 2045-06-02 stays as booked.',
      'The open punch from 2045-01-02 is in a locked payroll period and was left open.',
    ]);
  });

  it('uses the singular for one cancelled request', () => {
    expect(deactivationNotices({ ...none, cancelledPendingLeave: 1 })).toEqual([
      'Cancelled 1 pending leave request.',
    ]);
  });
});
