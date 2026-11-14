import { describe, expect, it } from 'vitest';
import { leaveStatusLabel } from './leave-status';

describe('leaveStatusLabel', () => {
  it('labels each stored status', () => {
    expect(leaveStatusLabel('pending')).toBe('Pending');
    expect(leaveStatusLabel('approved')).toBe('Approved');
    expect(leaveStatusLabel('rejected')).toBe('Rejected');
    expect(leaveStatusLabel('cancelled')).toBe('Cancelled');
  });

  it('capitalises a status it does not know instead of showing it raw', () => {
    expect(leaveStatusLabel('on_hold')).toBe('On hold');
  });

  it('shows a dash when there is no status', () => {
    expect(leaveStatusLabel(null)).toBe('—');
    expect(leaveStatusLabel(undefined)).toBe('—');
    expect(leaveStatusLabel('')).toBe('—');
  });
});
