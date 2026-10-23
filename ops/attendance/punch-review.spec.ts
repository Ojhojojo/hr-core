import { describe, expect, it } from 'vitest';
import { canReviewPunch, punchStatusLabel, punchStatusTone } from './punch-review';

describe('canReviewPunch', () => {
  it('allows owner and admin to review only pending punches', () => {
    expect(canReviewPunch('owner', 'pending')).toBe(true);
    expect(canReviewPunch('admin', 'pending')).toBe(true);
  });

  it('hides review for technicians, sales, and punches that are already decided', () => {
    expect(canReviewPunch('technician', 'pending')).toBe(false);
    expect(canReviewPunch('sales', 'pending')).toBe(false);
    expect(canReviewPunch('owner', 'approved')).toBe(false);
    expect(canReviewPunch('admin', 'rejected')).toBe(false);
    expect(canReviewPunch('owner', null)).toBe(false);
    expect(canReviewPunch(null, 'pending')).toBe(false);
  });

  it('labels punch approval status for the register chip', () => {
    expect(punchStatusLabel('pending')).toBe('Pending');
    expect(punchStatusLabel('approved')).toBe('Approved');
    expect(punchStatusLabel('rejected')).toBe('Rejected');
    expect(punchStatusLabel(null)).toBe('Approved');
    expect(punchStatusTone('pending')).toBe('warn');
    expect(punchStatusTone('approved')).toBe('ok');
    expect(punchStatusTone('rejected')).toBe('danger');
  });
});
