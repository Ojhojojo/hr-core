import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import {
  ShopInfo,
  ShopApiService,
  apiErrorMessage,
  manilaToday,
  downloadPdfBlob,
  printPdfBlob,
} from '@hr-host';
import { EmployeeCard, EmployeeDirectoryApi } from '../employee-directory/employee-directory.api';
import {
  HrDocumentDraft,
  HrDocumentKind,
  applyShopLetterhead,
  applyWorker,
  buildHrDocumentPdf,
  documentParagraphs,
  documentTitle,
  emptyHrDraft,
  hrDocumentPdfFileName,
  renderHrDocumentHtml,
  saveFilePrefix,
  nextRepeatConsequence,
  saveNote,
  signatureDetailLines,
  signatureLabels,
  WarningLevel,
} from './hr-templates';

export interface HrDocumentDialogData {
  kind: HrDocumentKind;
  person?: EmployeeCard | null;
  shop?: ShopInfo | null;
}

@Component({
  selector: 'app-hr-document-dialog',
  imports: [
    FormsModule,
    MatDialogModule,
    MatButton,
    MatFormField,
    MatLabel,
    MatHint,
    MatInput,
    MatSelect,
    MatOption,
  ],
  encapsulation: ViewEncapsulation.None,
  templateUrl: './hr-document-dialog.html',
  styles: `
    .hr-doc-fields {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: var(--space-1) var(--space-3);
    }

    .hr-doc-span {
      grid-column: 1 / -1;
    }

    .hr-doc-sheet {
      font-family: Georgia, 'Times New Roman', serif;
      color: #1a1a1a;
      background: #fff;
      width: 210mm;
      max-width: 100%;
      margin: var(--space-4) auto 0;
      padding: 16mm 14mm;
      box-sizing: border-box;
    }

    .hr-doc-sheet h1,
    .hr-doc-sheet h2,
    .hr-doc-sheet p {
      margin: 0;
    }

    .hr-doc-sheet h1 {
      font-size: 1.45rem;
    }

    .hr-doc-sheet h2 {
      margin-top: var(--space-6);
      font-size: 1.2rem;
    }

    .hr-doc-sheet p {
      margin-top: var(--space-3);
      line-height: 1.45;
    }

    .hr-doc-signs {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: var(--space-6) var(--space-8);
      margin-top: var(--space-12);
    }

    .hr-doc-line {
      display: block;
      border-bottom: 1px solid #111;
      height: 2.2rem;
    }

    .hr-doc-signs p {
      margin-top: var(--space-1);
    }

    .hr-doc-actions {
      flex-wrap: wrap;
      gap: var(--space-2);
    }

    .hr-doc-print-hint {
      flex: 1 1 100%;
      margin: 0;
      font-size: 0.8rem;
      color: var(--ops-muted, #5b6178);
      text-align: left;
    }

    @media (max-width: 700px) {
      .hr-doc-fields,
      .hr-doc-signs {
        grid-template-columns: 1fr;
      }
    }
  `,
})
export class HrDocumentDialog {
  private readonly data = inject<HrDocumentDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<HrDocumentDialog, boolean>);
  private readonly directory = inject(EmployeeDirectoryApi);
  private readonly shopApi = inject(ShopApiService);

  protected readonly previewTick = signal(0);
  protected readonly people = signal<EmployeeCard[]>(this.data.person ? [this.data.person] : []);
  protected readonly loadingStaff = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected staffId = this.data.person?.id ?? '';
  protected readonly draft: HrDocumentDraft = emptyHrDraft(
    this.data.kind,
    this.data.shop ?? null,
    this.data.person ?? null,
    manilaToday(),
  );

  constructor() {
    if (!this.data.shop) {
      this.shopApi.getShop().subscribe((shop) => {
        applyShopLetterhead(this.draft, shop);
        this.touch();
      });
    }
    this.directory.list(false).subscribe({
      next: (body) => {
        const items = [...(body.items ?? [])];
        const selected = this.data.person;
        if (selected && !items.some((person) => person.id === selected.id)) {
          items.unshift(selected);
        }
        this.people.set(items);
        this.loadingStaff.set(false);
      },
      error: (err: unknown) => {
        this.error.set(apiErrorMessage(err));
        this.loadingStaff.set(false);
      },
    });
  }

  protected title(): string {
    this.previewTick();
    return documentTitle(this.draft.kind);
  }

  protected paragraphs(): string[] {
    this.previewTick();
    return documentParagraphs(this.draft);
  }

  protected signatures(): string[] {
    return signatureLabels(this.draft.kind);
  }

  protected signatureDetails(label: string): string[] {
    this.previewTick();
    return signatureDetailLines(this.draft, label);
  }

  protected onWarningLevel(level: WarningLevel): void {
    this.draft.repeatConsequence = nextRepeatConsequence(
      this.draft.repeatConsequence,
      this.draft.warningLevel,
      level,
    );
    this.draft.warningLevel = level;
    this.touch();
  }

  protected onStaff(id: string): void {
    const person = this.people().find((row) => row.id === id);
    if (!person) {
      return;
    }
    this.staffId = person.id;
    applyWorker(this.draft, person);
    this.touch();
  }

  protected touch(): void {
    this.previewTick.update((n) => n + 1);
  }

  protected close(): void {
    this.ref.close(false);
  }

  protected print(): void {
    this.error.set('');
    try {
      printPdfBlob(buildHrDocumentPdf(this.draft), documentTitle(this.draft.kind));
    } catch {
      this.error.set('Could not build the PDF.');
    }
  }

  protected download(): void {
    this.error.set('');
    try {
      downloadPdfBlob(buildHrDocumentPdf(this.draft), hrDocumentPdfFileName(this.draft));
    } catch {
      this.error.set('Could not build the PDF.');
    }
  }

  protected save(): void {
    if (!this.staffId) {
      this.error.set('Pick a staff member before saving.');
      return;
    }
    const html = renderHrDocumentHtml(this.draft);
    const slug =
      this.draft.workerName
        .trim()
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, '-')
        .replace(/^-|-$/g, '') || 'staff';
    const file = new File([html], `${saveFilePrefix(this.draft.kind)}-${slug}.html`, {
      type: 'text/html',
    });
    this.saving.set(true);
    this.error.set('');
    this.directory.upload(this.staffId, file, 'other', saveNote(this.draft.kind)).subscribe({
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
