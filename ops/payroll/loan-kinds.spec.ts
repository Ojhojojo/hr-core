import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { afterEach, describe, expect, it } from 'vitest';
import { of } from 'rxjs';
import { OpsConfirmService } from '@hr-host';
import { LoanKindSettings, loanKindProblem } from './loan-kinds';
import { HR_LOAN_KINDS } from './payroll-loan-kinds';
import { StaffLoanDialog } from './payroll-loan-dialog';

describe('loanKindProblem', () => {
  it('reads a 409 problem detail before any other message', () => {
    expect(
      loanKindProblem({
        error: {
          title: 'Conflict',
          status: 409,
          detail: 'A loan kind with this name already exists.',
        },
      }),
    ).toBe('A loan kind with this name already exists.');
    expect(
      loanKindProblem({
        error: { detail: 'This loan kind is in use. Deactivate it instead of deleting it.' },
      }),
    ).toContain('Deactivate');
  });
});

describe('Loan kinds screen', () => {
  afterEach(() => TestBed.resetTestingModule());

  async function mount() {
    await TestBed.configureTestingModule({
      imports: [LoanKindSettings],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: OpsConfirmService, useValue: { confirm: () => of({ confirmed: true }) } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(LoanKindSettings);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    return { fixture, http, host: fixture.nativeElement as HTMLElement };
  }

  it('saves a new cut-off 1 kind and shows a 409 detail', async () => {
    const { fixture, http, host } = await mount();
    fixture.componentInstance.load();
    fixture.detectChanges();
    http.expectOne((r) => r.url.endsWith('/api/ops/payroll/loan-kinds')).flush({ kinds: [] });
    fixture.detectChanges();

    const cmp = fixture.componentInstance as unknown as {
      addKind(): void;
      setName(index: number, value: string): void;
      setRule(index: number, value: string): void;
      setOrdinal(index: number, value: number): void;
      save(): void;
    };
    cmp.addKind();
    cmp.setName(0, 'Pag-IBIG loan');
    cmp.setRule(0, 'cutoff');
    cmp.setOrdinal(0, 1);
    fixture.detectChanges();
    cmp.save();

    const post = http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/loan-kinds'));
    expect(post.request.body).toEqual({
      name: 'Pag-IBIG loan',
      active: true,
      cutoffOrdinal: 1,
    });
    post.flush(
      { title: 'Conflict', status: 409, detail: 'A loan kind with this name already exists.' },
      { status: 409, statusText: 'Conflict' },
    );
    fixture.detectChanges();
    expect(host.textContent).toContain('already exists');
    http.verify();
  });

  it('refuses to delete a kind that is in use', async () => {
    const { fixture, http, host } = await mount();
    fixture.componentInstance.load();
    fixture.detectChanges();
    http
      .expectOne((r) => r.url.endsWith('/loan-kinds'))
      .flush({
        kinds: [{ id: 'kind-1', name: 'SSS loan', active: true, cutoffOrdinal: 1 }],
      });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const name = host.querySelector('input[name="loanKindName0"]') as HTMLInputElement;
    expect(name.value).toBe('SSS loan');

    (host.querySelector('button[name="deleteLoanKind0"]') as HTMLButtonElement).click();
    const del = http.expectOne((r) => r.method === 'DELETE');
    del.flush(
      {
        title: 'Conflict',
        status: 409,
        detail: 'This loan kind is in use. Deactivate it instead of deleting it.',
      },
      { status: 409, statusText: 'Conflict' },
    );
    fixture.detectChanges();
    expect(host.textContent).toContain('Deactivate');
    http.verify();
  });
});

describe('Staff loan dialog kinds', () => {
  afterEach(() => TestBed.resetTestingModule());

  async function mount(loanKinds: boolean, loan: { loanKindId?: string } | null = null) {
    const closed: unknown[] = [];
    await TestBed.configureTestingModule({
      imports: [StaffLoanDialog],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { loan, suggestedKinds: ['Cash advance'] } },
        { provide: MatDialogRef, useValue: { close: (value: unknown) => closed.push(value) } },
        ...(loanKinds ? [{ provide: HR_LOAN_KINDS, useValue: true }] : []),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(StaffLoanDialog);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    return { fixture, http, host: fixture.nativeElement as HTMLElement, closed };
  }

  it('keeps a free-text kind when the host leaves the flag off', async () => {
    const { host, http } = await mount(false);
    expect(host.querySelector('input[name="kind"]')).toBeTruthy();
    expect(host.querySelector('mat-select[name="loanKindId"]')).toBeNull();
    expect(http.match((r) => r.url.includes('/loan-kinds')).length).toBe(0);
    http.verify();
  });

  it('picks from the list for a new loan when the host turns the flag on', async () => {
    const { fixture, host, http, closed } = await mount(true);
    const get = http.expectOne((r) => r.url.endsWith('/loan-kinds'));
    get.flush({
      kinds: [
        { id: 'kind-1', name: 'Pag-IBIG loan', active: true, cutoffOrdinal: 1 },
        { id: 'kind-2', name: 'Old advance', active: false, cutoffOrdinal: null },
      ],
    });
    fixture.detectChanges();
    expect(host.querySelector('input[name="kind"]')).toBeNull();
    expect(host.querySelector('mat-select[name="loanKindId"]')).toBeTruthy();

    (host.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(host.textContent).toContain('Pick a loan kind.');

    const cmp = fixture.componentInstance as unknown as {
      loanKindId: string;
      principal: number;
      perPeriodAmount: number;
      startPeriod: string;
      save(): void;
    };
    cmp.loanKindId = 'kind-1';
    cmp.principal = 1000;
    cmp.perPeriodAmount = 200;
    cmp.startPeriod = '2026-01-01';
    cmp.save();
    expect(closed[0]).toMatchObject({ kind: 'Pag-IBIG loan', loanKindId: 'kind-1' });
    http.verify();
  });

  it('keeps free text for a legacy loan even when the flag is on', async () => {
    const { host, http } = await mount(true, {});
    expect(host.querySelector('input[name="kind"]')).toBeTruthy();
    expect(http.match((r) => r.url.includes('/loan-kinds')).length).toBe(0);
    http.verify();
  });
});
