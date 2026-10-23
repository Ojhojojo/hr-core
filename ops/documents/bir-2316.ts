import { jsPDF } from 'jspdf';
import { roundMoney } from '@hr-host';
import type { Bir2316MissingKey, Bir2316Sheet } from '../payroll/payroll.api';

/**
 * Ceiling on 13th-month pay and other benefits that stay non-taxable in a calendar year.
 * Amounts above it are taxable. This is the current BIR limit and may change; keep it here only.
 */
export const BIR_2316_THIRTEENTH_NON_TAXABLE_CAP = 90_000;

/** Amounts HR enters on the 2316 dialog. None of them come from payroll. */
export interface Bir2316HrInputs {
  thirteenthAndOtherBenefits: number;
  deMinimis: number;
  otherNonTaxable: number;
  previousTaxableCompensation: number;
  previousTaxWithheld: number;
}

/** Payroll totals for the year, as returned by the BIR 2316 endpoint. */
export type Bir2316PayrollAmounts = Pick<
  Bir2316Sheet['amounts'],
  'gross' | 'sss' | 'philHealth' | 'pagIbig' | 'wht'
>;

export interface Bir2316Computation {
  /** Item: Gross compensation income from present employer. */
  grossCompensation: number;
  /** Item: 13th month pay and other benefits, as entered. */
  thirteenthAndOtherBenefits: number;
  /** The part of the 13th month and other benefits that stays non-taxable (up to the cap). */
  thirteenthNonTaxable: number;
  /** The part above the cap. It is taxable. */
  thirteenthExcessTaxable: number;
  deMinimis: number;
  /** Item: SSS, GSIS, PHIC and Pag-IBIG contributions and union dues (employee shares). */
  contributions: number;
  otherNonTaxable: number;
  /** Item: total non-taxable / exempt compensation. */
  totalNonTaxable: number;
  /** Item: taxable compensation income from present employer. */
  taxableCompensationPresent: number;
  previousTaxableCompensation: number;
  totalTaxableCompensation: number;
  taxWithheldPresent: number;
  previousTaxWithheld: number;
  /** Item: total amount of taxes withheld. */
  totalTaxWithheld: number;
}

/** Labels that match the BIR Form 2316 item names HR copies from. */
export const BIR_2316_ITEM_LABELS = {
  grossCompensation: 'Gross compensation income from present employer',
  thirteenthAndOtherBenefits: '13th month pay and other benefits',
  thirteenthNonTaxable: '13th month pay and other benefits, non-taxable part (up to the cap)',
  thirteenthExcessTaxable: '13th month pay and other benefits, excess over the cap (taxable)',
  deMinimis: 'De minimis benefits',
  contributions: 'SSS, GSIS, PHIC & Pag-IBIG contributions and union dues',
  otherNonTaxable: 'Other non-taxable compensation',
  totalNonTaxable: 'Total non-taxable / exempt compensation',
  taxableCompensationPresent: 'Taxable compensation income from present employer',
  previousTaxableCompensation: 'Taxable compensation income from previous employer',
  totalTaxableCompensation: 'Total taxable compensation income',
  taxWithheldPresent: 'Tax withheld by present employer',
  previousTaxWithheld: 'Tax withheld by previous employer',
  totalTaxWithheld: 'Total amount of taxes withheld',
} as const;

export const BIR_2316_MISSING_LABELS: Record<Bir2316MissingKey, string> = {
  employee_tin: "Employee's TIN",
  employee_address: 'Registered address',
  employee_zip_code: 'ZIP code',
  employee_birth_date: 'Birth date',
  employer_tin: "Employer's TIN",
  employer_address: 'Shop address',
};

/** Negative, blank and non-finite inputs count as zero. Money is rounded to centavos. */
export function sanitizeMoney(value: number | null | undefined): number {
  const amount = Number(value);
  return Number.isFinite(amount) && amount > 0 ? roundMoney(amount) : 0;
}

export function emptyBir2316Inputs(): Bir2316HrInputs {
  return {
    thirteenthAndOtherBenefits: 0,
    deMinimis: 0,
    otherNonTaxable: 0,
    previousTaxableCompensation: 0,
    previousTaxWithheld: 0,
  };
}

/**
 * Pure 2316 arithmetic. Payroll gross excludes the 13th month and the HR-entered benefits, so
 * those are added to gross here. Non-taxable items are then taken out of gross, which leaves
 * payroll gross less employee contributions plus any 13th-month excess over the cap.
 */
export function computeBir2316(
  payroll: Bir2316PayrollAmounts,
  inputs: Bir2316HrInputs,
): Bir2316Computation {
  const thirteenth = sanitizeMoney(inputs.thirteenthAndOtherBenefits);
  const deMinimis = sanitizeMoney(inputs.deMinimis);
  const otherNonTaxable = sanitizeMoney(inputs.otherNonTaxable);
  const previousTaxable = sanitizeMoney(inputs.previousTaxableCompensation);
  const previousWithheld = sanitizeMoney(inputs.previousTaxWithheld);

  const payrollGross = sanitizeMoney(payroll.gross);
  const wht = sanitizeMoney(payroll.wht);
  const contributions = roundMoney(
    sanitizeMoney(payroll.sss) + sanitizeMoney(payroll.philHealth) + sanitizeMoney(payroll.pagIbig),
  );

  const grossCompensation = roundMoney(payrollGross + thirteenth + deMinimis + otherNonTaxable);
  const thirteenthNonTaxable = Math.min(thirteenth, BIR_2316_THIRTEENTH_NON_TAXABLE_CAP);
  const thirteenthExcessTaxable = roundMoney(thirteenth - thirteenthNonTaxable);
  const totalNonTaxable = roundMoney(
    thirteenthNonTaxable + deMinimis + contributions + otherNonTaxable,
  );
  const taxableCompensationPresent = roundMoney(grossCompensation - totalNonTaxable);

  return {
    grossCompensation,
    thirteenthAndOtherBenefits: thirteenth,
    thirteenthNonTaxable,
    thirteenthExcessTaxable,
    deMinimis,
    contributions,
    otherNonTaxable,
    totalNonTaxable,
    taxableCompensationPresent,
    previousTaxableCompensation: previousTaxable,
    totalTaxableCompensation: roundMoney(taxableCompensationPresent + previousTaxable),
    taxWithheldPresent: wht,
    previousTaxWithheld: previousWithheld,
    totalTaxWithheld: roundMoney(wht + previousWithheld),
  };
}

/** Human-readable list of missing values, in the order the sheet shows them. */
export function missingLabels(missing: readonly Bir2316MissingKey[]): string[] {
  return missing.map((key) => BIR_2316_MISSING_LABELS[key] ?? key);
}

const MONTH_NAMES = [
  'January',
  'February',
  'March',
  'April',
  'May',
  'June',
  'July',
  'August',
  'September',
  'October',
  'November',
  'December',
];

export function monthNames(months: readonly number[]): string[] {
  return months.map((m) => MONTH_NAMES[m - 1] ?? String(m));
}

/** One-page printable data sheet. Plain text values; the printed form uses the 2316 item names. */
export function buildBir2316Pdf(sheet: Bir2316Sheet, computation: Bir2316Computation): Blob {
  const doc = new jsPDF({ unit: 'mm', format: 'a4' });
  const margin = 16;
  const pageWidth = doc.internal.pageSize.getWidth();
  const pageHeight = doc.internal.pageSize.getHeight();
  const maxWidth = pageWidth - margin * 2;
  const lineGap = 5.2;
  let y = margin;

  const ensureSpace = (needed: number) => {
    if (y + needed > pageHeight - margin) {
      doc.addPage();
      y = margin;
    }
  };
  const text = (value: string, size = 10, style: 'normal' | 'bold' = 'normal') => {
    doc.setFont('times', style);
    doc.setFontSize(size);
    const lines = doc.splitTextToSize(value, maxWidth) as string[];
    ensureSpace(lines.length * lineGap + 1);
    doc.text(lines, margin, y);
    y += lines.length * lineGap;
  };
  const row = (label: string, value: string) => {
    doc.setFont('times', 'normal');
    doc.setFontSize(10);
    const labelLines = doc.splitTextToSize(label, maxWidth - 40) as string[];
    ensureSpace(labelLines.length * lineGap + 1);
    doc.text(labelLines, margin, y);
    doc.text(value, margin + maxWidth, y, { align: 'right' });
    y += labelLines.length * lineGap;
  };
  const php = (amount: number) =>
    `PHP ${amount.toLocaleString('en-PH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;

  text(`BIR Form 2316 data sheet, ${sheet.year}`, 14, 'bold');
  text(
    'Copy these values into the official BIR Form 2316 or eBIRForms. This is a data summary, not the form itself.',
    9,
  );
  y += 3;

  text('Employer', 11, 'bold');
  text(sheet.employer.legalName || '', 10);
  text(`TIN: ${sheet.employer.tin ?? '(missing)'}`, 10);
  text(`Address: ${sheet.employer.address ?? '(missing)'}`, 10);
  y += 2;

  text('Employee', 11, 'bold');
  text(sheet.employee.name, 10);
  text(`TIN: ${sheet.employee.tin ?? '(missing)'}`, 10);
  text(`Registered address: ${sheet.employee.registeredAddress ?? '(missing)'}`, 10);
  text(`ZIP code: ${sheet.employee.zipCode ?? '(missing)'}`, 10);
  text(`Birth date: ${sheet.employee.birthDate ?? '(missing)'}`, 10);
  y += 3;

  text('Amounts', 11, 'bold');
  const c = computation;
  const items: [string, number][] = [
    [BIR_2316_ITEM_LABELS.grossCompensation, c.grossCompensation],
    [BIR_2316_ITEM_LABELS.thirteenthAndOtherBenefits, c.thirteenthAndOtherBenefits],
    [BIR_2316_ITEM_LABELS.thirteenthNonTaxable, c.thirteenthNonTaxable],
    [BIR_2316_ITEM_LABELS.thirteenthExcessTaxable, c.thirteenthExcessTaxable],
    [BIR_2316_ITEM_LABELS.deMinimis, c.deMinimis],
    [BIR_2316_ITEM_LABELS.contributions, c.contributions],
    [BIR_2316_ITEM_LABELS.otherNonTaxable, c.otherNonTaxable],
    [BIR_2316_ITEM_LABELS.totalNonTaxable, c.totalNonTaxable],
    [BIR_2316_ITEM_LABELS.taxableCompensationPresent, c.taxableCompensationPresent],
    [BIR_2316_ITEM_LABELS.previousTaxableCompensation, c.previousTaxableCompensation],
    [BIR_2316_ITEM_LABELS.totalTaxableCompensation, c.totalTaxableCompensation],
    [BIR_2316_ITEM_LABELS.taxWithheldPresent, c.taxWithheldPresent],
    [BIR_2316_ITEM_LABELS.previousTaxWithheld, c.previousTaxWithheld],
    [BIR_2316_ITEM_LABELS.totalTaxWithheld, c.totalTaxWithheld],
  ];
  for (const [label, amount] of items) {
    row(label, php(amount));
  }
  y += 3;

  text('Payroll runs used', 11, 'bold');
  if (sheet.runs.length === 0) {
    text('No locked payroll run for this employee in this year.', 10);
  }
  for (const run of sheet.runs) {
    text(
      `${run.periodFrom} to ${run.periodTo}, ${run.shopName}, locked ${run.lockedAtUtc ?? 'n/a'}`,
      9,
    );
  }
  if (sheet.missingMonths.length > 0) {
    text(`Months with no locked run: ${monthNames(sheet.missingMonths).join(', ')}.`, 9);
  }
  y += 3;

  const missing = missingLabels(sheet.missing);
  if (missing.length > 0) {
    text(`Missing values to fill in before filing: ${missing.join(', ')}.`, 9, 'bold');
  }

  return doc.output('blob');
}
