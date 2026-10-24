/** Same rule as the API: one @, a dot in the domain, no spaces. */
export function isValidEmail(email: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@.][^\s@]*$/.test(email.trim());
}

/** TIN is optional. When given: digits with optional dashes or spaces, 9 to 14 digits. */
export function isValidTin(tin: string): boolean {
  const trimmed = tin.trim();
  if (!trimmed) {
    return true;
  }
  if (!/^[\d\s-]+$/.test(trimmed)) {
    return false;
  }
  const digits = trimmed.replace(/\D/g, '').length;
  return digits >= 9 && digits <= 14;
}

/** First problem with the Add / Edit person fields, or null when they look fine. */
export function personFieldError(fields: { email?: string; tin: string }): string | null {
  if (fields.email !== undefined && !isValidEmail(fields.email)) {
    return 'Enter a valid email address.';
  }
  if (!isValidTin(fields.tin)) {
    return 'TIN must be 9 to 14 digits, for example 123-456-789-000.';
  }
  return null;
}
