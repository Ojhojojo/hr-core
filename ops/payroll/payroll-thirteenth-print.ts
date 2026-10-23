import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { jsPDF } from 'jspdf';
import { ShopInfo, DialogComponent, printPdfBlob } from '@hr-host';
import { formatPayslipMoney, letterheadLines } from './payroll-payslip';
import { ThirteenthMonthRow } from './payroll.models';

export interface ThirteenthPrintData {
  shop: ShopInfo | null;
  year: number;
  rows: ThirteenthMonthRow[];
  /** "Locked 7 Oct 2026 by Owner Apple" when the period on screen is frozen. */
  lockLine?: string | null;
}

export function thirteenthFileName(year: number): string {
  return `thirteenth-month-${year}.pdf`;
}

/** A4 PDF for the 13th-month estimate sheet. */
export function buildThirteenthPdf(data: ThirteenthPrintData): Blob {
  const doc = new jsPDF({ unit: 'mm', format: 'a4' });
  const margin = 14;
  const pageWidth = doc.internal.pageSize.getWidth();
  const pageHeight = doc.internal.pageSize.getHeight();
  const maxWidth = pageWidth - margin * 2;
  const lineGap = 5;
  const head = letterheadLines(data.shop);
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

  writeWrapped(head.title, 16, 'bold');
  y += 1;
  if (head.legal && head.legal !== head.title) {
    writeWrapped(head.legal, 11);
  }
  if (head.contact) {
    writeWrapped(head.contact, 11);
  }

  y += 5;
  writeWrapped(`13th-month estimate ${data.year}`, 14, 'bold');
  if (data.lockLine) {
    writeWrapped(data.lockLine, 11, 'bold');
  }
  writeWrapped('Estimate — owner verify. Not a BIR filing product.', 11, 'bold');
  y += 3;

  const cols = [margin, margin + 55, margin + 95, margin + 140];
  const writeRow = (cells: string[], bold = false) => {
    ensureSpace(lineGap + 2);
    doc.setFont('times', bold ? 'bold' : 'normal');
    doc.setFontSize(10);
    doc.text(cells[0], cols[0], y);
    doc.text(cells[1], cols[1], y);
    doc.text(cells[2], cols[2], y, { align: 'right' });
    doc.text(cells[3], cols[3], y, { align: 'right' });
    y += lineGap;
  };

  writeRow(['Staff', 'Basis', 'Basic earned', '13th month'], true);
  let total = 0;
  for (const row of data.rows) {
    const amount = Number(row.thirteenthMonth) || 0;
    total += amount;
    const basis = row.payBasis ? row.payBasis.charAt(0).toUpperCase() + row.payBasis.slice(1) : '—';
    writeRow([
      row.staffName,
      basis,
      formatPayslipMoney(row.basicEarned),
      formatPayslipMoney(amount),
    ]);
  }
  writeRow(['Total 13th month', '', '', formatPayslipMoney(total)], true);
  y += 4;
  writeWrapped('Payout is outside the app. This sheet is an estimate for the owner to check.', 11);

  return doc.output('blob');
}

@Component({
  selector: 'app-thirteenth-summary-dialog',
  imports: [DialogComponent, MatDialogModule, MatButton],
  encapsulation: ViewEncapsulation.None,
  template: `
    <ops-dialog title="13th-month summary">
      @if (error()) {
        <p class="ops-error" role="alert">{{ error() }}</p>
      }
      <article class="thirteenth-sheet" data-thirteenth-summary>
        <header class="thirteenth-letterhead">
          <h1>{{ head.title }}</h1>
          @if (head.legal && head.legal !== head.title) {
            <p>{{ head.legal }}</p>
          }
          @if (head.contact) {
            <p>{{ head.contact }}</p>
          }
        </header>
        <h2>13th-month estimate {{ data.year }}</h2>
        @if (data.lockLine) {
          <p class="thirteenth-lock" data-thirteenth-lock>{{ data.lockLine }}</p>
        }
        <p class="thirteenth-banner">Estimate — owner verify. Not a BIR filing product.</p>
        <table>
          <thead>
            <tr>
              <th>Staff</th>
              <th>Basis</th>
              <th>Basic earned</th>
              <th>13th month</th>
            </tr>
          </thead>
          <tbody>
            @for (row of data.rows; track row.staffId) {
              <tr>
                <td>{{ row.staffName }}</td>
                <td>{{ basisLabel(row.payBasis) }}</td>
                <td>{{ money(row.basicEarned) }}</td>
                <td>{{ money(row.thirteenthMonth) }}</td>
              </tr>
            }
            <tr>
              <td colspan="3"><strong>Total 13th month</strong></td>
              <td>
                <strong>{{ money(total()) }}</strong>
              </td>
            </tr>
          </tbody>
        </table>
        <p>Payout is outside the app. This sheet is an estimate for the owner to check.</p>
      </article>
      <div dialogActions class="thirteenth-chrome">
        <button matButton="text" type="button" mat-dialog-close>Close</button>
        <button matButton="filled" type="button" name="printThirteenthSummary" (click)="print()">
          Print
        </button>
      </div>
    </ops-dialog>
  `,
  styles: `
    .thirteenth-sheet {
      font-family: Georgia, 'Times New Roman', serif;
      color: #1a1a1a;
      background: #fff;
      width: 210mm;
      max-width: 100%;
      margin: 0 auto;
      padding: 12mm;
      box-sizing: border-box;
    }

    .thirteenth-sheet h1,
    .thirteenth-sheet h2,
    .thirteenth-sheet p {
      margin: 0;
    }

    .thirteenth-letterhead h1 {
      font-size: 1.4rem;
    }

    .thirteenth-sheet h2 {
      margin-top: 1rem;
      font-size: 1.2rem;
    }

    .thirteenth-banner {
      margin-top: 0.4rem;
      font-weight: 700;
    }

    .thirteenth-lock {
      margin-top: 0.4rem;
      font-weight: 700;
    }

    .thirteenth-sheet table {
      width: 100%;
      margin: 1rem 0;
      border-collapse: collapse;
    }

    .thirteenth-sheet th,
    .thirteenth-sheet td {
      text-align: left;
      padding: 0.35rem 0.4rem;
      border-bottom: 1px solid #ddd;
      font-weight: 400;
    }

    .thirteenth-sheet td:nth-child(n + 3),
    .thirteenth-sheet th:nth-child(n + 3) {
      text-align: right;
    }

    .thirteenth-chrome {
      flex-wrap: wrap;
      gap: 0.5rem;
    }

    .thirteenth-print-hint {
      flex: 1 1 100%;
      margin: 0;
      font-size: 0.8rem;
      color: var(--ops-muted, #5b6178);
      text-align: left;
    }

    .ops-error {
      color: #b42318;
      margin: 0 0 0.75rem;
    }
  `,
})
export class ThirteenthSummaryDialog {
  protected readonly data = inject<ThirteenthPrintData>(MAT_DIALOG_DATA);
  protected readonly head = letterheadLines(this.data.shop);
  protected readonly error = signal('');

  protected money(amount: number | null | undefined): string {
    return formatPayslipMoney(amount);
  }

  protected basisLabel(basis: string | null | undefined): string {
    if (!basis) {
      return '—';
    }
    return basis.charAt(0).toUpperCase() + basis.slice(1);
  }

  protected total(): number {
    return this.data.rows.reduce((sum, row) => sum + (Number(row.thirteenthMonth) || 0), 0);
  }

  protected print(): void {
    this.error.set('');
    try {
      const blob = buildThirteenthPdf(this.data);
      printPdfBlob(blob, `13th-month estimate ${this.data.year}`);
    } catch {
      this.error.set('Could not build the PDF.');
    }
  }
}
