import type { DirectoryRole, PayBasis } from './employee-directory.api';

/**
 * Default for "Must clock" on a new person. The owner never clocks in. Other roles use the app's
 * `clocksByDefault` (hr-host), the same rule the API uses when no value is sent.
 * `role` may be a host role outside the four built-in names; the callback still sees that value.
 */
export function mustClockDefault(
  role: string,
  clocksByDefault: (role: DirectoryRole) => boolean,
): boolean {
  return role === 'owner' ? false : clocksByDefault(role as DirectoryRole);
}

/**
 * Must clock after the role picker changes. Owner is always off. A manual tick stays put
 * (`mustClockTouched`). A new person who has not touched it follows {@link mustClockDefault}.
 * Editing an existing person leaves the saved flag alone, except when the role becomes owner.
 */
export function mustClockAfterRoleChange(
  role: string,
  options: {
    isNewPerson: boolean;
    mustClockTouched: boolean;
    current: boolean;
    payBasis: PayBasis | null;
    clocksByDefault: (role: DirectoryRole) => boolean;
  },
): boolean {
  if (role === 'owner') {
    return false;
  }
  if (!options.isNewPerson || options.mustClockTouched) {
    return options.current;
  }
  return mustClockAfterBasisChange(
    mustClockDefault(role, options.clocksByDefault),
    options.payBasis,
  );
}

/** Monthly pay turns Must clock off. The user can tick it again. Other pay bases leave it as is. */
export function mustClockAfterBasisChange(current: boolean, basis: PayBasis | null): boolean {
  return basis === 'monthly' ? false : current;
}
