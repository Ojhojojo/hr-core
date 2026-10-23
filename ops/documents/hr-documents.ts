import { Component, inject } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { PageHeaderComponent, StatusBadgeComponent } from '@hr-host';
import { Bir2316Dialog } from './bir-2316-dialog';
import { HrDocumentDialog } from './hr-document-dialog';
import { HR_TEMPLATES, HrTemplateRow } from './hr-templates';

@Component({
  selector: 'app-hr-documents',
  imports: [MatButton, MatTableModule, RouterLink, PageHeaderComponent, StatusBadgeComponent],
  templateUrl: './hr-documents.html',
})
export class HrDocumentsPage {
  private readonly dialog = inject(MatDialog);

  protected readonly columns = ['title', 'description', 'status', 'action'];
  protected readonly templates = HR_TEMPLATES;

  protected open(row: HrTemplateRow): void {
    if (row.status !== 'available' || !row.kind) {
      return;
    }
    if (row.kind === 'bir-2316') {
      this.dialog.open(Bir2316Dialog, {
        width: 'min(52rem, 78vw)',
        maxWidth: 'calc(100vw - 32px)',
      });
      return;
    }
    this.dialog.open(HrDocumentDialog, {
      width: 'min(52rem, 78vw)',
      maxWidth: 'calc(100vw - 32px)',
      data: { kind: row.kind },
    });
  }
}
