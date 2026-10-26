import type { DirectoryRole, PayBasis } from './employee-directory.api';

/**
 * Default for "Must clock" on a new person. The owner never clocks in. Other roles use the app's
 * `clocksByDefault` (hr-host), the same rule the API uses when no value is sent.
 */
export function mustClockDefault(
  role: DirectoryRole,
  clocksByDefault: (role: DirectoryRole) => boolean,
): boolean {
  return role === 'owner' ? false : clocksByDefault(role);
}

/** Monthly pay turns Must clock off. The user can tick it again. Other pay bases leave it as is. */
export function mustClockAfterBasisChange(current: boolean, basis: PayBasis | null): boolean {
  return basis === 'monthly' ? false : current;
}
