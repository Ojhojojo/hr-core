import { describe, expect, it } from 'vitest';
import { paidDaysLabel } from './paid-days-label';

describe('paidDaysLabel', () => {
  it('names paid leave beside punched days', () => {
    expect(paidDaysLabel(0, 1)).toBe('0 days paid · 1 leave day paid');
    expect(paidDaysLabel(1, 0.5)).toBe('1 day paid · 0.5 leave days paid');
    expect(paidDaysLabel(null, 2)).toBe('0 days paid · 2 leave days paid');
  });

  it('keeps the plain label when there is no paid leave', () => {
    expect(paidDaysLabel(5, 0)).toBe('5 days paid');
    expect(paidDaysLabel(3, undefined)).toBe('3 days paid');
    expect(paidDaysLabel(1, null)).toBe('1 day paid');
  });
});
