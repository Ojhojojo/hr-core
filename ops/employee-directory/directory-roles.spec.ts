import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import {
  DEFAULT_DIRECTORY_ROLES,
  DIRECTORY_ROLE_LABELS,
  HR_DIRECTORY_ROLES,
  type DirectoryRoleOption,
  directoryRoleAriaLabel,
  directoryRoleChoices,
  directoryRoleLabel,
  initialDirectoryRole,
} from './directory-roles';

const qurioRoles: DirectoryRoleOption[] = [
  { value: 'owner', label: 'Owner', hint: 'Full access, including staff and settings.' },
  { value: 'admin', label: 'Admin', hint: 'Runs the shop day to day.' },
  { value: 'architect', label: 'Architect', hint: 'Leads the design.' },
  { value: 'engineer', label: 'Engineer', hint: 'Clocks in on site.' },
  { value: 'foreman', label: 'Foreman', hint: 'Runs the crew on site.' },
  { value: 'accounting', label: 'Accounting', hint: 'Books and payroll.' },
  { value: 'crew', label: 'Crew', hint: 'On the tools.' },
];

describe('directory role picker', () => {
  it('exposes an optional host token and keeps the built-in labels', () => {
    expect(HR_DIRECTORY_ROLES).toBeTruthy();
    expect(String(HR_DIRECTORY_ROLES)).toContain('HR_DIRECTORY_ROLES');
  });

  it('falls back to the four built-in roles, labels, and hints', () => {
    expect(directoryRoleChoices(null)).toEqual([
      { value: 'admin', label: 'Admin', hint: 'Runs the shop day to day.' },
      { value: 'technician', label: 'Technician', hint: 'Works jobs on the floor.' },
      { value: 'sales', label: 'Sales', hint: 'Front desk: customers and estimates.' },
      { value: 'owner', label: 'Owner', hint: 'Full access, including staff and settings.' },
    ]);
    expect(directoryRoleChoices(undefined)).toEqual(directoryRoleChoices(null));
    expect(directoryRoleChoices([])).toEqual([...DEFAULT_DIRECTORY_ROLES]);
    expect(DIRECTORY_ROLE_LABELS).toEqual({
      owner: 'Owner',
      admin: 'Admin',
      technician: 'Technician',
      sales: 'Sales',
    });
  });

  it('shows exactly the host list, in that order, with its labels and hints', () => {
    const choices = directoryRoleChoices(qurioRoles, 'engineer');
    expect(choices).toEqual(qurioRoles);
    expect(choices.map((role) => role.value)).toEqual([
      'owner',
      'admin',
      'architect',
      'engineer',
      'foreman',
      'accounting',
      'crew',
    ]);
    expect(qurioRoles).toHaveLength(7);
  });

  it('does not mutate the host list when a legacy role is added', () => {
    const host = qurioRoles.map((role) => ({ ...role }));
    directoryRoleChoices(host, 'technician');
    expect(host).toEqual(qurioRoles);
  });

  it('shows a stored built-in role that the host list dropped', () => {
    const choices = directoryRoleChoices(qurioRoles, 'technician');
    expect(choices.slice(0, qurioRoles.length)).toEqual(qurioRoles);
    expect(choices.at(-1)).toEqual({
      value: 'technician',
      label: 'Technician',
      hint: 'Works jobs on the floor.',
      legacy: true,
    });
    expect(initialDirectoryRole({ personRole: 'technician', hostRoles: qurioRoles })).toBe(
      'technician',
    );
  });

  it('shows a stored role that neither list names, and keeps that value selected', () => {
    const choices = directoryRoleChoices(qurioRoles, 'apprentice');
    expect(choices.at(-1)).toEqual({
      value: 'apprentice',
      label: 'apprentice',
      hint: 'Current role. It is not in the role list.',
      legacy: true,
    });
    expect(initialDirectoryRole({ personRole: 'apprentice', hostRoles: qurioRoles })).toBe(
      'apprentice',
    );
    expect(choices.some((role) => role.value === 'apprentice')).toBe(true);
  });

  it('starts a new person on technician unless the host list has no technician', () => {
    expect(initialDirectoryRole({ hostRoles: null })).toBe('technician');
    expect(initialDirectoryRole({ hostRoles: qurioRoles })).toBe('admin');
    expect(initialDirectoryRole({ presetRole: 'crew', hostRoles: qurioRoles })).toBe('crew');
  });

  it('gives each option an accessible name that includes its hint', () => {
    const [admin, technician, sales, owner] = directoryRoleChoices(null);
    expect(directoryRoleAriaLabel(admin)).toBe('Admin. Runs the shop day to day.');
    expect(directoryRoleAriaLabel(technician)).toBe('Technician. Works jobs on the floor.');
    expect(directoryRoleAriaLabel(sales)).toBe('Sales. Front desk: customers and estimates.');
    expect(directoryRoleAriaLabel(owner)).toBe('Owner. Full access, including staff and settings.');
    expect(directoryRoleAriaLabel(directoryRoleChoices(qurioRoles)[2])).toBe(
      'Architect. Leads the design.',
    );
  });

  it('labels directory rows from the host list, then the built-in names, then the raw value', () => {
    expect(directoryRoleLabel('architect', qurioRoles)).toBe('Architect');
    expect(directoryRoleLabel('technician', null)).toBe('Technician');
    expect(directoryRoleLabel('technician', qurioRoles)).toBe('Technician');
    expect(directoryRoleLabel('apprentice', null)).toBe('apprentice');
  });

  it('closed role field is the label alone, including a legacy role', () => {
    for (const option of DEFAULT_DIRECTORY_ROLES) {
      expect(directoryRoleLabel(option.value)).toBe(option.label);
      expect(directoryRoleLabel(option.value).includes(option.hint)).toBe(false);
    }
    expect(directoryRoleLabel('technician', qurioRoles)).toBe('Technician');
    expect(directoryRoleLabel('apprentice', qurioRoles)).toBe('apprentice');

    const source = readFileSync(
      join(dirname(fileURLToPath(import.meta.url)), 'employee-directory.dialogs.ts'),
      'utf8',
    );
    const select = source.slice(source.indexOf('name="role"'), source.indexOf('</mat-select>'));
    expect(select).toContain('<mat-select-trigger>');
    expect(select).toContain('directoryRoleLabel(role, hostRoles)');
    expect(select).toContain('{{ option.label }}');
    expect(select).not.toContain('option.hint');
    expect(source).toContain('<mat-hint>{{ roleHint(role) }}</mat-hint>');
  });
});
