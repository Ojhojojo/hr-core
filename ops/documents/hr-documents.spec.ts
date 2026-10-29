import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { describe, expect, it } from 'vitest';
import { AuthMe, AuthService, ShopInfo, formatPhp } from '@hr-host';
import { EmployeeCard } from '../employee-directory/employee-directory.api';
import { HrDocumentDialog } from './hr-document-dialog';

const shop: ShopInfo = {
  tradingName: 'Harbor Lane',
  legalName: 'Harbor Lane Motors OPC',
  address: '1 Harbor Road',
  phone: '0917-111-2222',
  facebook: '',
  email: '',
  tin: '111-222-333-000',
};

const person: EmployeeCard = {
  id: 'p-1',
  role: 'technician',
  displayName: 'Ana Active',
  email: null,
  mobile: null,
  hireDate: '2024-02-01',
  active: true,
  emergencyContactName: null,
  emergencyContactPhone: null,
  position: 'Lead painter',
  department: 'Body and paint',
  employeeNo: 'RC-014',
  tin: '123-456-789-000',
  sss: '01-2345678-9',
  philHealth: '12-345678901-2',
  pagIbig: '1234-5678-9012',
};

function authStub(me: AuthMe) {
  return {
    me: signal(me),
    loading: signal(false),
    isSignedIn: () => true,
    isStaff: () => true,
    refreshMe: async () => {},
  };
}

describe('HrDocumentDialog', () => {
  async function mountDialog(kind: 'nte' | 'nod' | 'quitclaim', withPerson = true) {
    await TestBed.configureTestingModule({
      imports: [HrDocumentDialog],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { kind, person: withPerson ? person : null, shop } },
        { provide: MatDialogRef, useValue: { close: () => undefined } },
      ],
    }).compileComponents();
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(HrDocumentDialog);
    fixture.detectChanges();
    http
      .expectOne(
        (req) => req.url.includes('/api/employee-directory') && !req.url.includes('/documents'),
      )
      .flush({ headcount: 1, items: [person], configHealth: [] });
    fixture.detectChanges();
    await fixture.whenStable();
    return { http, fixture };
  }

  it('saves the letter onto the worker employment documents', async () => {
    const { http, fixture } = await mountDialog('quitclaim');
    const dialog = fixture.componentInstance as unknown as {
      draft: { finalPayPhp: string };
      touch: () => void;
    };
    dialog.draft.finalPayPhp = '15000';
    dialog.touch();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(document.body.textContent).toContain(formatPhp(15000));

    (document.body.querySelector('button[name="saveHrDoc"]') as HTMLButtonElement).click();
    const save = http.expectOne(
      (req) => req.method === 'POST' && req.url === '/api/employee-directory/p-1/documents',
    );
    const body = save.request.body as FormData;
    expect(body.get('docType')).toBe('other');
    expect(body.get('note')).toBe('Quitclaim');
    const file = body.get('file') as File;
    expect(file.name).toBe('quitclaim-ana-active.html');
    expect(file.type).toBe('text/html');
    const html = await file.text();
    expect(html).toContain(formatPhp(15000));
    expect(html).toContain('Harbor Lane Motors OPC');
    save.flush({ id: 'doc-1' });
    http.verify();
  });

  it('keeps save disabled until a worker is picked', async () => {
    const { http } = await mountDialog('nte', false);
    const save = document.body.querySelector('button[name="saveHrDoc"]') as HTMLButtonElement;
    expect(save.disabled).toBe(true);
    http.verify();
  });
});

function personAuth(role: AuthMe['staffRole']): AuthMe {
  return {
    authUserId: role ?? 'staff',
    email: `${role}@rakkiicars.ph`,
    profileKind: 'staff',
    staffRole: role,
    branchId: 'branch-1',
    branchCode: 'demo',
    isStaff: true,
    displayName: 'Tester',
  };
}
