import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { afterEach, describe, expect, it } from 'vitest';
import { PayrollDepositSettings, depositProblem, formatDepositAmount } from './deposits';

describe('depositProblem', () => {
  it('reads a field error before the problem detail', () => {
    expect(
      depositProblem({
        error: {
          title: 'One or more validation errors occurred.',
          status: 400,
          errors: {
            depositedAmount: ['Amount must be zero or more, with at most 2 decimal places.'],
          },
        },
      }),
    ).toBe('Amount must be zero or more, with at most 2 decimal places.');
    expect(depositProblem({ error: { detail: 'Deposits are recorded on a locked run.' } })).toContain(
      'locked',
    );
  });
});

describe('formatDepositAmount', () => {
  it('shows a short deposit as a positive peso amount and an over deposit with a minus', () => {
    expect(formatDepositAmount(50)).toBe('₱50.00');
    expect(formatDepositAmount(-50)).toBe('−₱50.00');
  });
});

describe('Deposit screen', () => {
  afterEach(() => TestBed.resetTestingModule());

  async function mount() {
    await TestBed.configureTestingModule({
      imports: [PayrollDepositSettings],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(PayrollDepositSettings);
    fixture.componentRef.setInput('runId', 'run-1');
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    return { fixture, http, host: fixture.nativeElement as HTMLElement };
  }

  it('lists the line whose deposit does not match net', async () => {
    const { fixture, http, host } = await mount();
    fixture.componentInstance.load();
    fixture.detectChanges();
    http.expectOne((r) => r.method === 'GET' && r.url.endsWith('/api/ops/payroll/runs/run-1')).flush({
      run: { id: 'run-1', totalNet: 18050 },
      lines: [
        {
          staffId: 'staff-1',
          staffName: 'Casey Diaz',
          net: 10050,
          depositedAmount: 10000,
          discrepancy: 50,
        },
        { staffId: 'staff-2', staffName: 'Dana Ruiz', net: 8000 },
      ],
      discrepancies: [
        {
          staffId: 'staff-1',
          staffName: 'Casey Diaz',
          net: 10050,
          depositedAmount: 10000,
          discrepancy: 50,
        },
      ],
    });
    fixture.detectChanges();
    expect(host.querySelector('[data-deposit-discrepancies]')?.textContent).toContain('Casey Diaz');
    expect(host.querySelector('[data-deposit-discrepancies]')?.textContent).toContain('₱50.00');
    expect(host.querySelector('[data-staff="staff-1"] [data-line-discrepancy]')?.textContent).toContain(
      '₱50.00',
    );
    expect(host.textContent).not.toContain('Dana Ruiz · discrepancy');
    http.verify();
  });

  it('saves a deposited amount and shows a field error from the API', async () => {
    const { fixture, http, host } = await mount();
    fixture.componentInstance.load();
    fixture.detectChanges();
    http.expectOne((r) => r.method === 'GET').flush({
      run: { id: 'run-1' },
      lines: [{ staffId: 'staff-1', staffName: 'Casey Diaz', net: 10050 }],
    });
    fixture.detectChanges();
    expect(host.querySelector('[data-deposit-clear]')?.textContent).toContain('No lines with a difference');

    const cmp = fixture.componentInstance as unknown as {
      setAmount(staffId: string, value: number | null): void;
      save(line: { staffId: string; staffName: string; net: number }): void;
    };
    cmp.setAmount('staff-1', -1);
    cmp.save({ staffId: 'staff-1', staffName: 'Casey Diaz', net: 10050 });
    const rejected = http.expectOne(
      (r) => r.method === 'PUT' && r.url.endsWith('/api/ops/payroll/runs/run-1/deposit'),
    );
    expect(rejected.request.body).toEqual({ staffId: 'staff-1', depositedAmount: -1 });
    rejected.flush(
      {
        status: 400,
        errors: { depositedAmount: ['Amount must be zero or more, with at most 2 decimal places.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();
    expect(host.textContent).toContain('at most 2 decimal places');

    cmp.setAmount('staff-1', 10000);
    cmp.save({ staffId: 'staff-1', staffName: 'Casey Diaz', net: 10050 });
    const saved = http.expectOne((r) => r.method === 'PUT');
    expect(saved.request.body).toEqual({ staffId: 'staff-1', depositedAmount: 10000 });
    saved.flush({
      runId: 'run-1',
      staffId: 'staff-1',
      staffName: 'Casey Diaz',
      net: 10050,
      depositedAmount: 10000,
      discrepancy: 50,
    });
    http.expectOne((r) => r.method === 'GET').flush({
      run: { id: 'run-1' },
      lines: [
        {
          staffId: 'staff-1',
          staffName: 'Casey Diaz',
          net: 10050,
          depositedAmount: 10000,
          discrepancy: 50,
        },
      ],
      discrepancies: [
        {
          staffId: 'staff-1',
          staffName: 'Casey Diaz',
          net: 10050,
          depositedAmount: 10000,
          discrepancy: 50,
        },
      ],
    });
    fixture.detectChanges();
    expect(host.textContent).toContain('Casey Diaz deposited ₱10,000.00');
    expect(host.querySelector('[data-deposit-discrepancies]')?.textContent).toContain('₱50.00');
    http.verify();
  });
});
