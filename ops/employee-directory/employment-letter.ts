import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { jsPDF } from 'jspdf';
import { ShopInfo, apiErrorMessage, manilaToday } from '@hr-host';
import { EmployeeCard, EmployeeDirectoryApi, EmploymentDocType } from './employee-directory.api';

export type EmploymentLetterKind = 'offer' | 'coe';

export interface EmploymentLetterDraft {
  kind: EmploymentLetterKind;
  issuedOn: string;
  tradingName: string;
  legalName: string;
  address: string;
  phone: string;
  workerName: string;
  position: string;
  startDate: string;
  payBasis: string;
  ratePhp: string;
}

export interface EmploymentLetterDialogData {
  kind: EmploymentLetterKind;
  person: EmployeeCard;
  shop: ShopInfo | null;
  payBasis?: string | null;
  ratePhp?: number | null;
}

const MONTHS = [
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

export function formatLetterDate(iso: string): string {
  const [y, m, d] = iso.split('-').map(Number);
  if (!y || !m || !d) {
    return iso;
  }
  return `${MONTHS[m - 1]} ${d}, ${y}`;
}

export function letterTitle(kind: EmploymentLetterKind): string {
  return kind === 'offer' ? 'Offer of employment' : 'Certificate of employment';
}

export function emptyLetterDraft(
  data: EmploymentLetterDialogData,
  issuedOn = manilaToday(),
): EmploymentLetterDraft {
  const trading = data.shop?.tradingName?.trim() || 'Rakkii Cars';
  const legal = data.shop?.legalName?.trim() || trading;
  const rate =
    data.ratePhp == null || Number.isNaN(Number(data.ratePhp)) ? '' : String(data.ratePhp);
  return {
    kind: data.kind,
    issuedOn,
    tradingName: trading,
    legalName: legal,
    address: data.shop?.address?.trim() ?? '',
    phone: data.shop?.phone?.trim() ?? '',
    workerName: data.person.displayName,
    position: data.person.position?.trim() || data.person.role,
    startDate: data.person.hireDate ?? '',
    payBasis: data.payBasis?.trim() ?? '',
    ratePhp: rate,
  };
}

export function letterParagraphs(draft: EmploymentLetterDraft): string[] {
  const company = draft.legalName.trim() || draft.tradingName.trim() || 'the company';
  const worker = draft.workerName.trim() || 'the employee';
  const position = draft.position.trim() || 'staff';
  const start = draft.startDate ? formatLetterDate(draft.startDate) : 'the agreed start date';
  const place = [draft.address.trim(), draft.phone.trim()].filter(Boolean).join(' · ');
  const rate = draft.ratePhp.trim();
  const basis = draft.payBasis.trim();
  const payLine =
    rate && basis ? `Pay is PHP ${rate} on a ${basis} basis.` : rate ? `Pay is PHP ${rate}.` : '';

  if (draft.kind === 'offer') {
    return [
      `${formatLetterDate(draft.issuedOn)}`,
      `Dear ${worker},`,
      `${company} offers you the position of ${position}, starting ${start}.${place ? ` The shop is at ${place}.` : ''}`,
      payLine,
      'Please sign below to accept this offer.',
    ].filter(Boolean);
  }

  return [
    `${formatLetterDate(draft.issuedOn)}`,
    `This certifies that ${worker} is employed by ${company} as ${position}, starting ${start}.${place ? ` Place of work: ${place}.` : ''}`,
    payLine,
    'Issued upon request for whatever legal purpose it may serve.',
  ].filter(Boolean);
}

export function letterFileSlug(draft: EmploymentLetterDraft): string {
  return (
    draft.workerName
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-|-$/g, '') || 'staff'
  );
}

export function letterFileName(draft: EmploymentLetterDraft): string {
  const prefix = draft.kind === 'offer' ? 'offer' : 'coe';
  return `${prefix}-${letterFileSlug(draft)}.pdf`;
}

/** A4 PDF from draft fields — used for Save and Print. */
export function buildEmploymentLetterPdf(draft: EmploymentLetterDraft): Blob {
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

  const writeWrapped = (text: string, fontSize: number, style: 'normal' | 'bold' = 'normal') => {
    doc.setFont('times', style);
    doc.setFontSize(fontSize);
    const lines = doc.splitTextToSize(text, maxWidth) as string[];
    ensureSpace(lines.length * lineGap + 2);
    doc.text(lines, margin, y);
    y += lines.length * lineGap;
  };

  const heading = draft.tradingName.trim() || draft.legalName.trim() || 'Rakkii Cars';
  writeWrapped(heading, 16, 'bold');
  y += 1;

  if (draft.legalName.trim()) {
    writeWrapped(draft.legalName.trim(), 11);
  }
  const place = [draft.address.trim(), draft.phone.trim()].filter(Boolean).join(' · ');
  if (place) {
    writeWrapped(place, 11);
  }

  y += 6;
  writeWrapped(letterTitle(draft.kind), 14, 'bold');
  y += 4;

  for (const para of letterParagraphs(draft)) {
    writeWrapped(para, 11);
    y += 3;
  }

  y += 10;
  ensureSpace(20);
  doc.setDrawColor(17, 17, 17);
  doc.line(margin, y, margin + 60, y);
  y += 7;
  writeWrapped('Signature', 11);

  return doc.output('blob');
}

export function employmentLetterPdfFile(draft: EmploymentLetterDraft): File {
  return new File([buildEmploymentLetterPdf(draft)], letterFileName(draft), {
    type: 'application/pdf',
  });
}

@Component({
  selector: 'app-employment-letter-dialog',
  imports: [FormsModule, MatDialogModule, MatButton, MatFormField, MatLabel, MatInput],
  encapsulation: ViewEncapsulation.None,
  template: `
    <h2 mat-dialog-title class="letter-chrome">{{ title() }}</h2>
    <mat-dialog-content>
      @if (error()) {
        <p class="ops-error" role="alert">{{ error() }}</p>
      }
      <div class="letter-fields letter-chrome">
        <mat-form-field appearance="outline">
          <mat-label>Worker name</mat-label>
          <input matInput name="letterWorker" [(ngModel)]="draft.workerName" />
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Position</mat-label>
          <input matInput name="letterPosition" [(ngModel)]="draft.position" />
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Start date</mat-label>
          <input matInput name="letterStart" type="date" [(ngModel)]="draft.startDate" />
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Pay basis</mat-label>
          <input matInput name="letterBasis" [(ngModel)]="draft.payBasis" />
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Rate (PHP)</mat-label>
          <input matInput name="letterRate" [(ngModel)]="draft.ratePhp" />
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Legal name</mat-label>
          <input matInput name="letterLegal" [(ngModel)]="draft.legalName" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="letter-span">
          <mat-label>Shop address</mat-label>
          <input matInput name="letterAddress" [(ngModel)]="draft.address" />
        </mat-form-field>
      </div>

      <article class="letter-sheet" data-employment-letter>
        <header>
          <h1>{{ draft.tradingName }}</h1>
          <p>{{ draft.legalName }}</p>
          @if (draft.address || draft.phone) {
            <p>
              {{ draft.address }}{{ draft.address && draft.phone ? ' · ' : '' }}{{ draft.phone }}
            </p>
          }
        </header>
        <h2>{{ title() }}</h2>
        @for (line of paragraphs(); track $index) {
          <p>{{ line }}</p>
        }
        <div class="letter-sign">
          <span class="letter-line"></span>
          <p>Signature</p>
        </div>
      </article>
    </mat-dialog-content>
    <mat-dialog-actions align="end" class="letter-chrome letter-actions">
      <button matButton="outlined" type="button" (click)="close()">Close</button>
      <button
        matButton="outlined"
        type="button"
        name="saveLetter"
        [disabled]="saving()"
        (click)="save()"
      >
        {{ saving() ? 'Saving…' : 'Save to employment docs' }}
      </button>
      <button matButton="filled" type="button" name="printLetter" (click)="print()">Print</button>
    </mat-dialog-actions>
  `,
  styles: `
    .letter-fields {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: var(--space-1) var(--space-3);
    }

    .letter-span {
      grid-column: 1 / -1;
    }

    .letter-sheet {
      font-family: Georgia, 'Times New Roman', serif;
      color: #1a1a1a;
      background: #fff;
      width: 210mm;
      max-width: 100%;
      margin: var(--space-4) auto 0;
      padding: 16mm 14mm;
      box-sizing: border-box;
    }

    .letter-sheet h1,
    .letter-sheet h2,
    .letter-sheet p {
      margin: 0;
    }

    .letter-sheet h1 {
      font-size: 1.45rem;
    }

    .letter-sheet h2 {
      margin-top: var(--space-6);
      font-size: 1.2rem;
    }

    .letter-sheet p {
      margin-top: var(--space-3);
      line-height: 1.45;
    }

    .letter-sign {
      margin-top: var(--space-8);
    }

    .letter-line {
      display: block;
      width: 220px;
      border-bottom: 1px solid #111;
      height: 2.2rem;
    }

    .letter-actions {
      flex-wrap: wrap;
      gap: var(--space-2);
    }

    .letter-print-hint {
      flex: 1 1 100%;
      margin: 0;
      font-size: 0.8rem;
      color: var(--ops-muted, #5b6178);
      text-align: left;
    }

    @media (max-width: 700px) {
      .letter-fields {
        grid-template-columns: 1fr;
      }
    }
  `,
})
export class EmploymentLetterDialog {
  private readonly data = inject<EmploymentLetterDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<EmploymentLetterDialog, boolean>);
  private readonly api = inject(EmployeeDirectoryApi);

  protected readonly draft = emptyLetterDraft(this.data);
  protected readonly saving = signal(false);
  protected readonly error = signal('');

  protected title(): string {
    return letterTitle(this.draft.kind);
  }

  protected paragraphs(): string[] {
    return letterParagraphs(this.draft);
  }

  protected close(): void {
    this.ref.close(false);
  }

  protected print(): void {
    this.error.set('');
    try {
      const blob = buildEmploymentLetterPdf(this.draft);
      const url = URL.createObjectURL(blob);
      const frame = document.createElement('iframe');
      frame.setAttribute('title', letterTitle(this.draft.kind));
      frame.style.position = 'fixed';
      frame.style.right = '0';
      frame.style.bottom = '0';
      frame.style.width = '0';
      frame.style.height = '0';
      frame.style.border = '0';
      frame.src = url;
      const cleanup = () => {
        frame.remove();
        URL.revokeObjectURL(url);
      };
      frame.onload = () => {
        const win = frame.contentWindow;
        if (!win) {
          cleanup();
          this.error.set('Could not open the print preview.');
          return;
        }
        win.focus();
        win.print();
        window.setTimeout(cleanup, 60_000);
      };
      document.body.appendChild(frame);
    } catch {
      this.error.set('Could not build the PDF.');
    }
  }

  protected save(): void {
    let file: File;
    try {
      file = employmentLetterPdfFile(this.draft);
    } catch {
      this.error.set('Could not build the PDF.');
      return;
    }
    const docType: EmploymentDocType = this.draft.kind === 'offer' ? 'offer' : 'other';
    const note = this.draft.kind === 'offer' ? 'Offer letter' : 'Certificate of employment';
    this.saving.set(true);
    this.error.set('');
    this.api.upload(this.data.person.id, file, docType, note).subscribe({
      next: () => {
        this.saving.set(false);
        this.ref.close(true);
      },
      error: (err: unknown) => {
        this.error.set(apiErrorMessage(err));
        this.saving.set(false);
      },
    });
  }
}
