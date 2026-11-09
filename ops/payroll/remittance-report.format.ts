import { RemittanceReport } from './payroll.models';

/** CSV of the report on screen. Amounts are 0.00 with a dot and no thousands separator. */
export function remittanceCsv(report: RemittanceReport): string {
  const lines = ['Agency,Employee share,Employer share,Total'];
  for (const agency of report.agencies ?? []) {
    lines.push(row(agency.label, agency.employeeShare, agency.employerShare, agency.total));
  }
  lines.push(row('Total', report.employeeShare, report.employerShare, report.total));
  return `${lines.join('\n')}\n`;
}

function row(label: string, employee: number, employer: number, total: number): string {
  return [label, money(employee), money(employer), money(total)].join(',');
}

function money(value: number): string {
  return (Number(value) || 0).toFixed(2);
}
