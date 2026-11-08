import * as host from '@hr-host';

/**
 * Signed-in user type, read from the host's `AuthService` rather than a
 * hard-coded `AuthMe` or `Me`. Rakkii's user has `profileKind`. Qurio's does not.
 * The fixture is a plain object returned as that type, so either shape compiles.
 */
type HostService = InstanceType<typeof host.AuthService>;
type HostMeValue = HostService extends { me: infer MeField }
  ? MeField extends () => infer Value
    ? Value
    : never
  : never;

export type HostMe = NonNullable<HostMeValue>;

/** Role stored on the signed-in user. Owner and admin exist on every host. */
export type HostRole = NonNullable<HostMe extends { staffRole?: infer Role } ? Role : string>;

export function hostOwnerRole(): HostRole {
  return 'owner' as HostRole;
}

export function hostAdminRole(): HostRole {
  return 'admin' as HostRole;
}

/**
 * A role that is not owner or admin, taken from a host role export when the host
 * publishes one. These screens treat every other role the same way, so the
 * non-manager assertions stay covered when the host list has no third role.
 */
export function hostFloorRole(): HostRole {
  const floor = hostStaffRoles().find((role) => role !== 'owner' && role !== 'admin');
  return floor ?? ('staff' as HostRole);
}

/** Roles the host publishes as a value, plus owner and admin. */
export function hostStaffRoles(): HostRole[] {
  const found = new Set<string>(['owner', 'admin']);
  for (const [key, value] of Object.entries(host as Record<string, unknown>)) {
    if (!/role/i.test(key)) {
      continue;
    }
    for (const role of readRoles(value)) {
      found.add(role);
    }
  }
  return [...found] as HostRole[];
}

function readRoles(value: unknown): string[] {
  if (typeof value === 'string') {
    return [value];
  }
  if (Array.isArray(value)) {
    return value.flatMap((item) => {
      if (typeof item === 'string') {
        return [item];
      }
      if (item && typeof item === 'object' && 'value' in item) {
        const role = (item as { value?: unknown }).value;
        return typeof role === 'string' ? [role] : [];
      }
      return [];
    });
  }
  if (value && typeof value === 'object') {
    return Object.values(value as Record<string, unknown>).filter(
      (item): item is string => typeof item === 'string',
    );
  }
  return [];
}

/**
 * Signed-in staff for a spec. `profileKind` sits on the loose object so a host
 * whose user type lacks that field still accepts the fixture. Hosts that read
 * it still see `staff`.
 */
export function hostStaffMe(role: HostRole, patch: Record<string, unknown> = {}): HostMe {
  const me: Record<string, unknown> = {
    authUserId: String(role),
    email: `${String(role)}@example.com`,
    profileKind: 'staff',
    staffRole: role,
    branchId: null,
    branchCode: null,
    isStaff: true,
    displayName: 'Tester',
    ...patch,
  };
  return me as unknown as HostMe;
}
