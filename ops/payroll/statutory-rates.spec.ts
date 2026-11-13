import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { afterEach, describe, expect, it } from 'vitest';
import { of } from 'rxjs';
import { OpsConfirmService } from '@hr-host';
import { StatutoryRateSettings, statutoryRateProblem } from './statutory-rates';

describe('statutoryRateProblem', () => {
  it('reads a 400 field error before any other message', () => {
    expect(
      statutoryRateProblem({
        error: {
          title: 'One or more validation errors occurred.',
          status: 400,
          errors: { effectiveFrom: ['A row for this kind and date already exists.'] },
        },
      }),
    ).toBe('A row for this kind and date already exists.');
  });
});

describe('Statutory rates screen', () => {
  afterEach(() => TestBed.resetTestingModule());

  async function mount() {
    await TestBed.configureTestingModule({
      imports: [StatutoryRateSettings],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: OpsConfirmService, useValue: { confirm: () => of({ confirmed: true }) } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(StatutoryRateSettings);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    return { fixture, http, host: fixture.nativeElement as HTMLElement };
  }

  it('saves a 2027 SSS row and shows a 400 overlap detail', async () => {
    const { fixture, http, host } = await mount();
    fixture.componentInstance.load();
    fixture.detectChanges();
    http.expectOne((r) => r.url.endsWith('/api/ops/payroll/statutory-rates')).flush({ rates: [] });
    fixture.detectChanges();

    const cmp = fixture.componentInstance as unknown as {
      addRate(): void;
      setKind(index: number, value: string): void;
      setDate(index: number, value: string): void;
      setNumber(index: number, field: string, value: number): void;
      save(): void;
    };
    cmp.addRate();
    cmp.setKind(0, 'sss');
    cmp.setDate(0, '2027-01-01');
    cmp.setNumber(0, 'employeeRate', 0.06);
    fixture.detectChanges();
    cmp.save();

    const post = http.expectOne((r) => r.method === 'POST' && r.url.endsWith('/statutory-rates'));
    expect(post.request.body.kind).toBe('sss');
    expect(post.request.body.effectiveFrom).toBe('2027-01-01');
    expect(post.request.body.employeeRate).toBe(0.06);
    post.flush(
      {
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: { effectiveFrom: ['A row for this kind and date already exists.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();
    expect(host.textContent).toContain('already exists');
  });
});
