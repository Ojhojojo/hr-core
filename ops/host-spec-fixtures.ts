/**
 * Signed-in user a spec builds for itself.
 *
 * This is not the host's `AuthMe` / `Me` type and it is not a host role.
 * `staffRole` is whatever string the test passes. Screens in this package treat
 * `owner` and `admin` as managers and every other string as not a manager, so a
 * non-manager fixture uses a role that belongs to the test (`member`). Nothing
 * here is asserted to be the host union, and there is no stand-in role.
 */
export type SpecUser = {
  authUserId: string;
  email: string;
  staffRole: string | null;
  branchId: string | null;
  branchCode: string | null;
  isStaff: boolean;
  displayName: string;
  staffId?: string | null;
  /** Present because some hosts read it. Not part of every host's user type. */
  profileKind?: string;
};

/** Role used when a spec needs someone who is not owner or admin. */
export const MEMBER_ROLE = 'member';

export function specUser(role: string, patch: Partial<SpecUser> = {}): SpecUser {
  return {
    authUserId: role || 'user',
    email: `${role || 'user'}@example.com`,
    staffRole: role,
    branchId: null,
    branchCode: null,
    isStaff: true,
    displayName: 'Tester',
    profileKind: 'staff',
    ...patch,
  };
}
