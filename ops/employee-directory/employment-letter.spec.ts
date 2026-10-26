import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ShopInfo } from '@hr-host';
import {
  EmploymentLetterDialog,
  buildEmploymentLetterPdf,
  emptyLetterDraft,
  employmentLetterPdfFile,
  letterFileName,
  letterParagraphs,
} from './employment-letter';
import { EmployeeCard } from './employee-directory.api';

const shop: ShopInfo = {
  tradingName: 'Rakkii Cars',
  legalName: 'RAKKII CARS OPC',
  address: '249 Roosevelt',
  phone: '(02) 7001-9011',
  facebook: '',
  email: '',
};

const person: EmployeeCard = {
  id: 'p-1',
  role: 'sales',
  displayName: 'Int QA Sales',
  email: 'sales@rakkiicars.ph',
  mobile: null,
  hireDate: '2026-10-10',
  active: true,
  emergencyContactName: null,
  emergencyContactPhone: null,
  position: 'sales',
  payBasis: 'daily',
  ratePhp: 750,
};

describe('employment letter PDF helpers', () => {
  it('builds a PDF blob and names the file with a .pdf suffix', async () => {
    const draft = emptyLetterDraft({
      kind: 'coe',
      person,
      shop,
      payBasis: 'daily',
      ratePhp: 750,
    });
    expect(letterFileName(draft)).toBe('coe-int-qa-sales.pdf');
    expect(letterParagraphs(draft).join(' ')).toContain('Int QA Sales');
    expect(letterParagraphs(draft).join(' ')).toContain('PHP 750');

    const blob = buildEmploymentLetterPdf(draft);
    expect(blob.type).toBe('application/pdf');
    expect(blob.size).toBeGreaterThan(100);

    const file = employmentLetterPdfFile(draft);
    expect(file.name).toBe('coe-int-qa-sales.pdf');
    expect(file.type).toBe('application/pdf');
    const header = new TextDecoder().decode(await file.slice(0, 4).arrayBuffer());
    expect(header).toBe('%PDF');
  });
});

describe('EmploymentLetterDialog', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
    document.querySelectorAll('.cdk-overlay-container').forEach((el) => el.remove());
  });

  it('saves a PDF to employment docs and has no Download button', async () => {
    const close = vi.fn();
    await TestBed.configureTestingModule({
      imports: [EmploymentLetterDialog],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: MAT_DIALOG_DATA,
          useValue: {
            kind: 'coe',
            person,
            shop,
            payBasis: 'daily',
            ratePhp: 750,
          },
        },
        { provide: MatDialogRef, useValue: { close } },
      ],
    }).compileComponents();

    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(EmploymentLetterDialog);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(document.body.querySelector('button[name="downloadLetter"]')).toBeNull();
    expect(document.body.querySelector('button[name="printLetter"]')).toBeTruthy();

    (document.body.querySelector('button[name="saveLetter"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    const save = http.expectOne(
      (req) => req.method === 'POST' && req.url === '/api/employee-directory/p-1/documents',
    );
    const body = save.request.body as FormData;
    expect(body.get('docType')).toBe('other');
    expect(body.get('note')).toBe('Certificate of employment');
    const file = body.get('file') as File;
    expect(file.name).toBe('coe-int-qa-sales.pdf');
    expect(file.type).toBe('application/pdf');
    save.flush({ id: 'doc-1' });
    fixture.detectChanges();
    await fixture.whenStable();
    expect(close).toHaveBeenCalledWith(true);
  });
});
