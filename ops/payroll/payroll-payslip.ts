import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { jsPDF } from 'jspdf';
import { ShopInfo, apiErrorMessage, printPdfBlob, DialogComponent } from '@hr-host';
import { manilaLockDate } from './payroll-lock';
import { PayrollApi } from './payroll.api';
import { Payslip, PayrollDeductionDraft, PayrollStaffRow } from './payroll.models';

export { printPdfBlob } from '@hr-host';

export interface PayslipLine {
  label: string;
  amount: number;
}

export interface PayslipView {
  staffName: string;
  basis: string;
  rateLabel: string;
  rate: string;
  regularHours: string;
  otHours: string;
  otPay: string;
  daysPaid: number;
  grossLabel: string;
  gross: string;
  grossAmount: number;
  premiumLines: { label: string; amount: string }[];
  allowanceLines: { label: string; amount: string }[];
  loanLines: { label: string; amount: string }[];
  lines: { label: string; amount: string }[];
  net: string;
}

export interface PayslipIssueContext {
  staffId: string;
  issuedLine?: string | null;
}

export interface PayslipDialogData {
  shop: ShopInfo | null;
  periodFrom: string;
  periodTo: string;
  slips: PayslipView[];
  /** "Locked 7 Oct 2026 by Owner Apple" when the period is frozen. */
  lockLine?: string | null;
  /** Present for a single-staff dialog so Issue payslip can post the HR trail. */
  issue?: PayslipIssueContext | null;
}

export interface PayslipDialogResult {
  issuedAtUtc?: string;
}

export function payRate(row: PayrollStaffRow): number | null {
  const rate = row.ratePhp ?? row.rate ?? null;
  if (rate == null) {
    return null;
  }
  const amount = Number(rate);
  return Number.isFinite(amount) ? amount : null;
}

export function hasPayRate(row: PayrollStaffRow): boolean {
  return payRate(row) != null;
}

export function payslipPremiumLines(row: {
  holidayHours?: number | null;
  restDayHours?: number | null;
  nightHours?: number | null;
  premiumPay?: number | null;
}): { label: string; amount: string }[] {
  const lines: { label: string; amount: string }[] = [];
  if (row.holidayHours != null) {
    lines.push({ label: 'Holiday pay', amount: Number(row.holidayHours).toFixed(2) });
  }
  if (row.restDayHours != null) {
    lines.push({ label: 'Rest day', amount: Number(row.restDayHours).toFixed(2) });
  }
  if (row.nightHours != null) {
    lines.push({ label: 'Night diff (NSD)', amount: Number(row.nightHours).toFixed(2) });
  }
  if (row.premiumPay != null) {
    lines.push({ label: 'Premium pay', amount: formatPayslipMoney(row.premiumPay) });
  }
  return lines;
}

/** One line per allowance item. A bare total is used when items were not sent. Absent means no lines. */
export function payslipAllowanceLines(row: {
  allowances?: number | null;
  allowanceItems?: { name: string; amount: number }[] | null;
}): { label: string; amount: string }[] {
  const items = (row.allowanceItems ?? []).filter((item) => item?.name);
  if (items.length > 0) {
    return items.map((item) => ({
      label: item.name,
      amount: formatPayslipMoney(item.amount),
    }));
  }
  if (row.allowances != null) {
    return [{ label: 'COLA / allowances', amount: formatPayslipMoney(row.allowances) }];
  }
  return [];
}

/**
 * Instalment lines, each followed by "Loan balance" when that balance was sent.
 * A bare total is used when items were not sent. Absent means no lines.
 */
export function payslipLoanLines(row: {
  loanDeductions?: number | null;
  loanItems?:
    | readonly {
        label?: string | null;
        kind?: string | null;
        instalment?: number | null;
        amount?: number | null;
        loanBalance?: number | null;
        balanceAfter?: number | null;
      }[]
    | null;
}): { label: string; amount: string }[] {
  const items = row.loanItems ?? [];
  if (items.length > 0) {
    const lines: { label: string; amount: string }[] = [];
    for (const item of items) {
      if (!item) {
        continue;
      }
      const instalment = item.instalment ?? item.amount;
      if (instalment != null) {
        const label = (item.label ?? item.kind ?? '').trim() || 'Loan';
        lines.push({ label, amount: formatPayslipMoney(instalment) });
      }
      const balance = item.loanBalance != null ? item.loanBalance : item.balanceAfter;
      if (balance != null) {
        lines.push({ label: 'Loan balance', amount: formatPayslipMoney(balance) });
      }
    }
    return lines;
  }
  if (row.loanDeductions != null) {
    return [{ label: 'Loans / CA', amount: formatPayslipMoney(row.loanDeductions) }];
  }
  return [];
}

function loanDeducted(row: {
  loanDeductions?: number | null;
  loanItems?: readonly { instalment?: number | null; amount?: number | null }[] | null;
}): number {
  if (row.loanDeductions != null) {
    const amount = Number(row.loanDeductions);
    return Number.isFinite(amount) ? amount : 0;
  }
  return (row.loanItems ?? []).reduce((sum, item) => {
    const raw = item?.instalment ?? item?.amount;
    if (raw == null) {
      return sum;
    }
    const amount = Number(raw);
    return sum + (Number.isFinite(amount) ? amount : 0);
  }, 0);
}

export function payslipDeductionLines(draft: PayrollDeductionDraft | undefined): PayslipLine[] {
  const amounts: PayslipLine[] = [
    { label: 'SSS', amount: draft?.sss ?? 0 },
    { label: 'PhilHealth', amount: draft?.philHealth ?? 0 },
    { label: 'Pag-IBIG', amount: draft?.pagIbig ?? 0 },
    { label: 'WHT', amount: draft?.wht ?? 0 },
    { label: 'Other', amount: draft?.other ?? 0 },
  ];
  return amounts.filter((line) => line.amount > 0);
}

export function formatPayslipMoney(amount: number | null | undefined): string {
  const val = Number(amount ?? 0);
  const safe = Number.isFinite(val) ? val : 0;
  return `₱${safe.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

export function payslipBasisLabel(row: PayrollStaffRow): string {
  const basis = row.basis ?? row.payBasis;
  if (!basis) {
    return '—';
  }
  return basis.charAt(0).toUpperCase() + basis.slice(1);
}

/** Slip text from a stored/API payslip (My payslip and issued snapshots). */
export function payslipViewFromApi(slip: Payslip): PayslipView {
  const monthly = slip.basis === 'monthly';
  const draft: PayrollDeductionDraft = {
    sss: slip.sss,
    philHealth: slip.philHealth,
    pagIbig: slip.pagIbig,
    wht: slip.wht,
    other: slip.other,
  };
  const lines = payslipDeductionLines(draft);
  return {
    staffName: slip.staffName,
    basis: slip.basis ? slip.basis.charAt(0).toUpperCase() + slip.basis.slice(1) : '—',
    rateLabel: monthly ? 'Monthly salary' : 'Rate',
    rate: slip.ratePhp == null ? 'No rate' : formatPayslipMoney(slip.ratePhp),
    regularHours: Number(slip.regularHours ?? 0).toFixed(2),
    otHours: Number(slip.otHours ?? 0).toFixed(2),
    otPay: formatPayslipMoney(slip.otPay ?? 0),
    daysPaid: slip.daysPaid ?? 0,
    grossLabel: monthly ? 'Salary gross' : 'Gross',
    gross: formatPayslipMoney(slip.gross),
    grossAmount: Number(slip.gross) || 0,
    premiumLines: payslipPremiumLines(slip),
    allowanceLines: payslipAllowanceLines(slip),
    loanLines: payslipLoanLines(slip),
    lines: lines.map((line) => ({ label: line.label, amount: formatPayslipMoney(line.amount) })),
    net: formatPayslipMoney(slip.net),
  };
}

/** Slip text from the register row and the on-screen deduction draft. Gross stays the register amount. */
export function buildPayslipView(
  row: PayrollStaffRow,
  draft: PayrollDeductionDraft | undefined,
): PayslipView {
  const lines = payslipDeductionLines(draft);
  const gross = Number(row.amount) || 0;
  const deducted = lines.reduce((sum, line) => sum + line.amount, 0);
  const loans = loanDeducted(row);
  const net = Math.round((gross - deducted - loans) * 100) / 100;
  const rate = payRate(row);
  const monthly = (row.basis ?? row.payBasis) === 'monthly';
  return {
    staffName: row.staffName,
    basis: payslipBasisLabel(row),
    rateLabel: monthly ? 'Monthly salary' : 'Rate',
    rate: rate == null ? 'No rate' : formatPayslipMoney(rate),
    regularHours: Number(row.regularHours ?? 0).toFixed(2),
    otHours: Number(row.otHours ?? 0).toFixed(2),
    otPay: formatPayslipMoney(row.otPay ?? 0),
    daysPaid: row.daysPaid ?? 0,
    grossLabel: monthly ? 'Salary gross' : 'Gross',
    gross: formatPayslipMoney(gross),
    grossAmount: gross,
    premiumLines: payslipPremiumLines(row),
    allowanceLines: payslipAllowanceLines(row),
    loanLines: payslipLoanLines(row),
    lines: lines.map((line) => ({ label: line.label, amount: formatPayslipMoney(line.amount) })),
    net: formatPayslipMoney(net),
  };
}

export function letterheadLines(shop: ShopInfo | null | undefined): {
  title: string;
  legal: string;
  contact: string;
} {
  const trading = shop?.tradingName?.trim() || 'Rakkii Cars';
  const legal = shop?.legalName?.trim() || trading;
  const contact = [shop?.address, shop?.phone, shop?.email]
    .map((part) => (part ?? '').trim())
    .filter(Boolean)
    .join(' · ');
  return { title: trading, legal, contact };
}

export function payslipFileSlug(name: string): string {
  const slug = name
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
  return slug || 'staff';
}

export function payslipFileName(periodFrom: string, slips: PayslipView[]): string {
  if (slips.length === 1) {
    return `payslip-${payslipFileSlug(slips[0].staffName)}-${periodFrom || 'period'}.pdf`;
  }
  return `payslips-${periodFrom || 'period'}.pdf`;
}

/** A4 PDF from payslip views — used for Print. */
export function buildPayslipPdf(
  shop: ShopInfo | null | undefined,
  periodFrom: string,
  periodTo: string,
  slips: PayslipView[],
  lockLine?: string | null,
): Blob {
  const doc = new jsPDF({ unit: 'mm', format: 'a4' });
  const margin = 16;
  const pageWidth = doc.internal.pageSize.getWidth();
  const pageHeight = doc.internal.pageSize.getHeight();
  const maxWidth = pageWidth - margin * 2;
  const lineGap = 5.2;
  const head = letterheadLines(shop);

  slips.forEach((slip, index) => {
    if (index > 0) {
      doc.addPage();
    }
    let y = margin;

    const ensureSpace = (needed: number) => {
      if (y + needed > pageHeight - margin) {
        doc.addPage();
        y = margin;
      }
    };

    const writeWrapped = (text: string, fontSize: number, style: 'normal' | 'bold' = 'normal') => {
      doc.setFont('times', style);
      doc.setFontSize(fontSize);
      const lines = doc.splitTextToSize(text, maxWidth) as string[];
      ensureSpace(lines.length * lineGap + 2);
      doc.text(lines, margin, y);
      y += lines.length * lineGap;
    };

    const writePair = (label: string, value: string) => {
      ensureSpace(lineGap + 2);
      doc.setFont('times', 'normal');
      doc.setFontSize(11);
      doc.text(label, margin, y);
      doc.text(value, pageWidth - margin, y, { align: 'right' });
      y += lineGap;
    };

    writeWrapped(head.title, 16, 'bold');
    y += 1;
    if (head.legal && head.legal !== head.title) {
      writeWrapped(head.legal, 11);
    }
    if (head.contact) {
      writeWrapped(head.contact, 11);
    }

    y += 6;
    writeWrapped('Payslip', 14, 'bold');
    writeWrapped(`${periodFrom} to ${periodTo}`, 11);
    if (lockLine) {
      writeWrapped(lockLine, 11, 'bold');
    }
    y += 2;
    writeWrapped(slip.staffName, 13, 'bold');
    y += 2;

    writePair('Basis', slip.basis);
    writePair(slip.rateLabel, slip.rate);
    writePair('Regular hours', slip.regularHours);
    writePair('OT hours', slip.otHours);
    writePair('OT pay', slip.otPay);
    writePair('Days paid', String(slip.daysPaid));
    for (const line of slip.premiumLines) {
      writePair(line.label, line.amount);
    }
    for (const line of slip.allowanceLines) {
      writePair(line.label, line.amount);
    }
    writePair(slip.grossLabel, slip.gross);
    for (const line of slip.lines) {
      writePair(line.label, line.amount);
    }
    for (const line of slip.loanLines) {
      writePair(line.label, line.amount);
    }
    writePair('Net', slip.net);

    y += 12;
    ensureSpace(28);
    doc.setDrawColor(17, 17, 17);
    doc.line(margin, y, margin + 60, y);
    doc.line(pageWidth / 2 + 4, y, pageWidth - margin, y);
    y += 7;
    doc.setFont('times', 'normal');
    doc.setFontSize(10);
    doc.text('Employee signature', margin, y);
    doc.text('Authorized signature', pageWidth / 2 + 4, y);
  });

  return doc.output('blob');
}

@Component({
  selector: 'app-payslip-dialog',
  imports: [DialogComponent, MatButton, MatDialogModule],
  encapsulation: ViewEncapsulation.None,
  template: `
    <ops-dialog [title]="data.slips.length === 1 ? 'Payslip' : 'Payslips'">
      @if (error()) {
        <p class="ops-error" role="alert">{{ error() }}</p>
      }
      @for (slip of data.slips; track slip.staffName) {
        <article class="payslip-sheet" data-payslip>
          <header class="payslip-letterhead">
            <h1>{{ head.title }}</h1>
            @if (head.legal && head.legal !== head.title) {
              <p class="payslip-legal">{{ head.legal }}</p>
            }
            @if (head.contact) {
              <p>{{ head.contact }}</p>
            }
          </header>
          <h2>Payslip</h2>
          <p class="payslip-period">{{ data.periodFrom }} to {{ data.periodTo }}</p>
          @if (data.lockLine) {
            <p class="payslip-lock" data-payslip-lock>{{ data.lockLine }}</p>
          }
          @if (issuedLine()) {
            <p class="payslip-issued" data-issued>{{ issuedLine() }}</p>
          }
          <p class="payslip-worker">{{ slip.staffName }}</p>
          <table>
            <tbody>
              <tr>
                <th>Basis</th>
                <td>{{ slip.basis }}</td>
              </tr>
              <tr>
                <th>{{ slip.rateLabel }}</th>
                <td>{{ slip.rate }}</td>
              </tr>
              <tr>
                <th>Regular hours</th>
                <td>{{ slip.regularHours }}</td>
              </tr>
              <tr>
                <th>OT hours</th>
                <td>{{ slip.otHours }}</td>
              </tr>
              <tr>
                <th>OT pay</th>
                <td>{{ slip.otPay }}</td>
              </tr>
              <tr>
                <th>Days paid</th>
                <td>{{ slip.daysPaid }}</td>
              </tr>
              @for (line of slip.premiumLines; track line.label) {
                <tr>
                  <th>{{ line.label }}</th>
                  <td>{{ line.amount }}</td>
                </tr>
              }
              @for (line of slip.allowanceLines; track $index) {
                <tr>
                  <th>{{ line.label }}</th>
                  <td>{{ line.amount }}</td>
                </tr>
              }
              <tr>
                <th>{{ slip.grossLabel }}</th>
                <td>{{ slip.gross }}</td>
              </tr>
              @for (line of slip.lines; track line.label) {
                <tr>
                  <th>{{ line.label }}</th>
                  <td>{{ line.amount }}</td>
                </tr>
              }
              @for (line of slip.loanLines; track $index) {
                <tr>
                  <th>{{ line.label }}</th>
                  <td>{{ line.amount }}</td>
                </tr>
              }
              <tr>
                <th>Net</th>
                <td>{{ slip.net }}</td>
              </tr>
            </tbody>
          </table>
          <div class="payslip-sign">
            <div>
              <span class="payslip-line"></span>
              <p>Employee signature</p>
            </div>
            <div>
              <span class="payslip-line"></span>
              <p>Authorized signature</p>
            </div>
          </div>
        </article>
      }
      <div dialogActions class="payslip-chrome">
        <button matButton="text" type="button" (click)="close()">Close</button>
        @if (canIssue()) {
          <button
            matButton="outlined"
            type="button"
            name="issuePayslip"
            [disabled]="issuing()"
            (click)="issue()"
          >
            {{ issuing() ? 'Issuing…' : 'Issue payslip' }}
          </button>
        }
        <button matButton="filled" type="button" name="printPayslip" (click)="print()">
          Print
        </button>
      </div>
    </ops-dialog>
  `,
  styles: `
    .payslip-sheet {
      font-family: Georgia, 'Times New Roman', serif;
      color: #1a1a1a;
      background: #fff;
      width: 210mm;
      max-width: 100%;
      margin: 0 auto var(--space-6);
      padding: 16mm 14mm;
      box-sizing: border-box;
    }

    .payslip-sheet h1,
    .payslip-sheet h2,
    .payslip-sheet p {
      margin: 0;
    }

    .payslip-letterhead h1 {
      font-size: 1.4rem;
    }

    .payslip-legal,
    .payslip-letterhead p,
    .payslip-period,
    .payslip-issued {
      color: #333;
      font-size: 0.95rem;
    }

    .payslip-lock,
    .payslip-issued {
      font-weight: 700;
    }

    .payslip-sheet h2 {
      margin-top: var(--space-4);
      font-size: 1.25rem;
      letter-spacing: 0.04em;
    }

    .payslip-worker {
      margin-top: var(--space-3);
      font-size: 1.15rem;
      font-weight: 700;
      overflow-wrap: anywhere;
    }

    .payslip-sheet table {
      width: 100%;
      margin-top: var(--space-4);
      border-collapse: collapse;
    }

    .payslip-sheet th,
    .payslip-sheet td {
      text-align: left;
      padding: var(--space-1) 0;
      border-bottom: 1px solid #ddd;
      font-weight: 400;
    }

    .payslip-sheet td {
      text-align: right;
    }

    .payslip-sign {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: var(--space-8);
      margin-top: var(--space-12);
    }

    .payslip-line {
      display: block;
      border-bottom: 1px solid #111;
      height: 2.2rem;
    }

    .payslip-sign p {
      margin-top: var(--space-1);
      font-size: 0.85rem;
    }

    .payslip-chrome {
      flex-wrap: wrap;
      gap: var(--space-2);
    }

    .payslip-print-hint {
      flex: 1 1 100%;
      margin: 0;
      font-size: 0.8rem;
      color: var(--ops-muted, #5b6178);
      text-align: left;
    }

    .ops-error {
      color: #b42318;
      margin: 0 0 var(--space-3);
    }
  `,
})
export class PayslipDialog {
  private readonly api = inject(PayrollApi);
  private readonly ref = inject(MatDialogRef<PayslipDialog, PayslipDialogResult>);
  protected readonly data = inject<PayslipDialogData>(MAT_DIALOG_DATA);
  protected readonly head = letterheadLines(this.data.shop);
  protected readonly issuing = signal(false);
  protected readonly error = signal('');
  protected readonly issuedLine = signal(this.data.issue?.issuedLine?.trim() || '');
  private issuedAtUtc: string | undefined;

  constructor() {
    const close = this.ref.close.bind(this.ref);
    this.ref.close = (result?: PayslipDialogResult) => {
      close(result ?? (this.issuedAtUtc ? { issuedAtUtc: this.issuedAtUtc } : undefined));
    };
  }

  protected canIssue(): boolean {
    return !!this.data.issue?.staffId && this.data.slips.length === 1;
  }

  protected close(): void {
    this.ref.close();
  }

  protected issue(): void {
    const staffId = this.data.issue?.staffId;
    if (!staffId || !this.data.periodFrom || !this.data.periodTo) {
      this.error.set('Choose a period before issuing a payslip.');
      return;
    }
    this.error.set('');
    this.issuing.set(true);
    this.api
      .issuePayslip({ staffId, from: this.data.periodFrom, to: this.data.periodTo })
      .subscribe({
        next: (payslip) => {
          this.issuing.set(false);
          const at = payslip.issuedAtUtc ?? '';
          this.issuedAtUtc = at || undefined;
          this.issuedLine.set(at ? `Issued ${manilaLockDate(at)}` : 'Issued');
        },
        error: (err: unknown) => {
          this.issuing.set(false);
          this.error.set(apiErrorMessage(err));
        },
      });
  }

  protected print(): void {
    this.error.set('');
    try {
      const blob = buildPayslipPdf(
        this.data.shop,
        this.data.periodFrom,
        this.data.periodTo,
        this.data.slips,
        this.data.lockLine,
      );
      printPdfBlob(blob, this.data.slips.length === 1 ? 'Payslip' : 'Payslips');
    } catch {
      this.error.set('Could not build the PDF.');
    }
  }
}
