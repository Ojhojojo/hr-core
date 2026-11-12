import { describe, expect, it } from 'vitest';
import { pendingHeldLabel } from './leave-pending';

describe('pendingHeldLabel (#681)', () => {
  it('says how many days pending requests are holding', () => {
    expect(pendingHeldLabel(1)).toBe('1 pending');
    expect(pendingHeldLabel(2.5)).toBe('2.5 pending');
  });

  it('stays quiet when nothing is pending', () => {
    expect(pendingHeldLabel(0)).toBeNull();
    expect(pendingHeldLabel(null)).toBeNull();
    expect(pendingHeldLabel(undefined)).toBeNull();
  });
});
