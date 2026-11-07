import { InjectionToken } from '@angular/core';
import type { DirectoryRole } from './employee-directory.api';

/** One row in the People dialog role picker. */
export interface DirectoryRoleOption {
  /** Value stored on the person and sent to the API. The host `StaffRole` enum must include it. */
  value: string;
  label: string;
  hint: string;
  /**
   * Set only for a stored role that is not in the list the host supplied.
   * The option stays in the picker so the select is not blank and a save can send that role back.
   */
  legacy?: boolean;
}

/**
 * A role value the picker can hold: one of the four {@link DirectoryRole} names, or any
 * `value` from {@link HR_DIRECTORY_ROLES}. `DirectoryRole` stays the four-name union.
 */
export type DirectoryRoleValue = DirectoryRole | (string & {});

export const DIRECTORY_ROLE_LABELS: Record<DirectoryRole, string> = {
  owner: 'Owner',
  admin: 'Admin',
  technician: 'Technician',
  sales: 'Sales',
};

const DIRECTORY_ROLE_HINTS: Record<DirectoryRole, string> = {
  owner: 'Full access, including staff and settings.',
  admin: 'Runs the shop day to day.',
  technician: 'Works jobs on the floor.',
  sales: 'Front desk: customers and estimates.',
};

/** The picker rakkii already has, in the same order as the options it replaces. */
export const DEFAULT_DIRECTORY_ROLES: readonly DirectoryRoleOption[] = [
  { value: 'admin', label: DIRECTORY_ROLE_LABELS.admin, hint: DIRECTORY_ROLE_HINTS.admin },
  {
    value: 'technician',
    label: DIRECTORY_ROLE_LABELS.technician,
    hint: DIRECTORY_ROLE_HINTS.technician,
  },
  { value: 'sales', label: DIRECTORY_ROLE_LABELS.sales, hint: DIRECTORY_ROLE_HINTS.sales },
  { value: 'owner', label: DIRECTORY_ROLE_LABELS.owner, hint: DIRECTORY_ROLE_HINTS.owner },
];

const LEGACY_ROLE_HINT = 'Current role. It is not in the role list.';

/**
 * Optional host role list for Add person and Edit person.
 * Omit the provider and the picker uses {@link DEFAULT_DIRECTORY_ROLES}.
 * An empty list is treated the same as omitting it.
 */
export const HR_DIRECTORY_ROLES = new InjectionToken<readonly DirectoryRoleOption[]>(
  'HR_DIRECTORY_ROLES',
);

function isDirectoryRole(role: string): role is DirectoryRole {
  return role === 'owner' || role === 'admin' || role === 'technician' || role === 'sales';
}

function listedRoles(
  hostRoles: readonly DirectoryRoleOption[] | null | undefined,
): readonly DirectoryRoleOption[] {
  return hostRoles && hostRoles.length > 0 ? hostRoles : DEFAULT_DIRECTORY_ROLES;
}

function optionFrom(role: DirectoryRoleOption): DirectoryRoleOption {
  const label = role.label?.trim() ? role.label : role.value;
  return {
    value: role.value,
    label,
    hint: role.hint ?? '',
  };
}

function legacyOption(value: string): DirectoryRoleOption {
  if (isDirectoryRole(value)) {
    return {
      value,
      label: DIRECTORY_ROLE_LABELS[value],
      hint: DIRECTORY_ROLE_HINTS[value],
      legacy: true,
    };
  }
  return {
    value,
    label: value,
    hint: LEGACY_ROLE_HINT,
    legacy: true,
  };
}

/**
 * Options for the role picker. A host list replaces the default. A stored or preset role
 * that is missing from that list is appended so the select still shows it.
 */
export function directoryRoleChoices(
  hostRoles: readonly DirectoryRoleOption[] | null | undefined,
  currentRole?: string | null,
): DirectoryRoleOption[] {
  const options = listedRoles(hostRoles).map(optionFrom);
  if (currentRole && !options.some((role) => role.value === currentRole)) {
    options.push(legacyOption(currentRole));
  }
  return options;
}

/** Label for a role in the directory. Host labels win, then the four built-in labels, then the raw value. */
export function directoryRoleLabel(
  role: string,
  hostRoles?: readonly DirectoryRoleOption[] | null,
): string {
  const host = hostRoles?.find((item) => item.value === role);
  if (host?.label?.trim()) {
    return host.label;
  }
  if (isDirectoryRole(role)) {
    return DIRECTORY_ROLE_LABELS[role];
  }
  return role;
}

/**
 * Role property for a person PATCH. Absent when the selection is still the stored role,
 * so a value the host enum no longer accepts can be saved without being sent again.
 * A changed role is sent and the API still validates it.
 */
export function directoryEditRole(
  storedRole: string,
  selectedRole: string,
): { role?: DirectoryRole } {
  if (selectedRole === storedRole) {
    return {};
  }
  return { role: selectedRole as DirectoryRole };
}

/** Accessible name for a role option: the label, then the hint when there is one. */
export function directoryRoleAriaLabel(
  option: Pick<DirectoryRoleOption, 'label' | 'hint'>,
): string {
  const hint = option.hint?.trim();
  return hint ? `${option.label}. ${hint}` : option.label;
}

/**
 * Role selected when the dialog opens. A stored role wins, then a preset, then technician
 * when that value is in the list, then the first role that is not owner.
 */
export function initialDirectoryRole(args: {
  personRole?: string | null;
  presetRole?: string | null;
  hostRoles?: readonly DirectoryRoleOption[] | null;
}): string {
  if (args.personRole) {
    return args.personRole;
  }
  if (args.presetRole) {
    return args.presetRole;
  }
  const choices = directoryRoleChoices(args.hostRoles);
  if (choices.some((role) => role.value === 'technician')) {
    return 'technician';
  }
  return choices.find((role) => role.value !== 'owner')?.value ?? choices[0]?.value ?? 'technician';
}
