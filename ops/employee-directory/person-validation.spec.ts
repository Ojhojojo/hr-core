import { describe, expect, it } from 'vitest';
import { isValidEmail, isValidTin, personFieldError } from './person-validation';

describe('person validation', () => {
  it('accepts normal emails and rejects broken ones', () => {
    expect(isValidEmail('ana@rakkiicars.ph')).toBe(true);
    expect(isValidEmail('not-an-email')).toBe(false);
    expect(isValidEmail('ana@shop')).toBe(false);
    expect(isValidEmail('ana @shop.ph')).toBe(false);
  });

  it('allows a blank TIN and checks the digit count otherwise', () => {
    expect(isValidTin('')).toBe(true);
    expect(isValidTin('123-456-789')).toBe(true);
    expect(isValidTin('123-456-789-000')).toBe(true);
    expect(isValidTin('12')).toBe(false);
    expect(isValidTin('12a-456-789')).toBe(false);
  });

  it('reports the first problem', () => {
    expect(personFieldError({ email: 'x', tin: '12' })).toBe('Enter a valid email address.');
    expect(personFieldError({ email: 'a@b.ph', tin: '12' })).toContain('TIN');
    expect(personFieldError({ tin: '' })).toBeNull();
  });
});
