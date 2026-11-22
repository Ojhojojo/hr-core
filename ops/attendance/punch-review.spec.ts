import { describe, expect, it } from 'vitest';
import { canReviewPunch, punchStatusLabel, punchStatusTone } from './punch-review';

describe('canReviewPunch', () => {
  it('allows Owner and General Manager Access on pending punches', () => {
    expect(canReviewPunch('owner', 'pending')).toBe(true);
    expect(canReviewPunch('admin', 'pending', 'general_manager')).toBe(true);
  });

  it('blocks Purchaser Access and non-desk roles', () => {
    expect(canReviewPunch('admin', 'pending', 'purchaser')).toBe(false);
    expect(canReviewPunch('admin', 'pending', 'workshop_ops')).toBe(false);
    expect(canReviewPunch('admin', 'pending')).toBe(false);
    expect(canReviewPunch('technician', 'pending')).toBe(false);
    expect(canReviewPunch('sales', 'pending')).toBe(false);
  });

  it('blocks settled punches and missing role/status', () => {
    expect(canReviewPunch('owner', 'approved')).toBe(false);
    expect(canReviewPunch('admin', 'rejected', 'general_manager')).toBe(false);
    expect(canReviewPunch('owner', null)).toBe(false);
    expect(canReviewPunch(null, 'pending')).toBe(false);
  });
});

describe('punch status helpers', () => {
  it('labels and tones pending / rejected / approved', () => {
    expect(punchStatusLabel('pending')).toBe('Pending');
    expect(punchStatusLabel('rejected')).toBe('Rejected');
    expect(punchStatusLabel('approved')).toBe('Approved');
    expect(punchStatusTone('pending')).toBe('warn');
    expect(punchStatusTone('rejected')).toBe('danger');
    expect(punchStatusTone('approved')).toBe('ok');
  });
});
