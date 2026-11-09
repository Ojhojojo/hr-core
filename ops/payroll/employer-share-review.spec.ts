import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { afterEach, describe, expect, it } from 'vitest';
import { EmployerShareReviewPanel } from './employer-share-review';
import { EmployerShareReview } from './payroll.models';

const openReview: EmployerShareReview = {
  runId: 'run-1',
  readOnly: false,
  lines: [
    {
      staffId: 'staff-1',
      staffName: 'Casey Diaz',
      suggestedSss: 2030,
      suggestedPhilHealth: 500,
      suggestedPagIbig: 200,
    },
  ],
};

const readOnlyReview: EmployerShareReview = {
  ...openReview,
  readOnly: true,
  lines: [
    {
      ...openReview.lines[0],
      employerSss: 2030,
      employerPhilHealth: 500,
      employerPagIbig: 200,
    },
  ],
  reviewedAtUtc: '2026-11-28T01:00:00Z',
  reviewedByName: 'Reviewer',
};

describe('employer share review', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('shows the suggested shares and confirms an edited figure', async () => {
    const { fixture, http, host } = await mount();
    http
      .expectOne((r) => r.url.endsWith('/api/ops/payroll/remittance/runs/run-1'))
      .flush(openReview);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(host.querySelector('[data-share-name]')?.textContent).toContain('Casey Diaz');
    const sss = host.querySelector<HTMLInputElement>('[data-share-field="sss"]');
    expect(sss?.value).toBe('2030');
    sss!.value = '1999';
    sss!.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    host.querySelector<HTMLButtonElement>('[name="confirmEmployerShares"]')?.click();
    const put = http.expectOne(
      (r) => r.method === 'PUT' && r.url.endsWith('/api/ops/payroll/remittance/runs/run-1'),
    );
    expect(put.request.body).toEqual({
      lines: [
        { staffId: 'staff-1', employerSss: 1999, employerPhilHealth: 500, employerPagIbig: 200 },
      ],
    });
    put.flush({
      ...openReview,
      lines: [
        {
          ...openReview.lines[0],
          employerSss: 1999,
          employerPhilHealth: 500,
          employerPagIbig: 200,
        },
      ],
      reviewedAtUtc: '2026-11-28T01:00:00Z',
    });
    fixture.detectChanges();
    expect(host.querySelector('[data-share-field="sss"]')).toBeTruthy();
    http.verify();
  });

  it('is read-only after the month is exported', async () => {
    const { fixture, http, host } = await mount();
    http
      .expectOne((r) => r.url.endsWith('/api/ops/payroll/remittance/runs/run-1'))
      .flush(readOnlyReview);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(host.querySelector('[data-share-review-readonly]')?.textContent).toContain(
      'Employer shares are read-only.',
    );
    expect(host.querySelector<HTMLInputElement>('[data-share-field="sss"]')?.disabled).toBe(true);
    expect(host.querySelector('[name="confirmEmployerShares"]')).toBeNull();
    http.verify();
  });
});

async function mount() {
  await TestBed.configureTestingModule({
    imports: [EmployerShareReviewPanel],
    providers: [
      provideZonelessChangeDetection(),
      provideHttpClient(),
      provideHttpClientTesting(),
      provideNoopAnimations(),
    ],
  }).compileComponents();
  const fixture = TestBed.createComponent(EmployerShareReviewPanel);
  fixture.componentRef.setInput('runId', 'run-1');
  fixture.detectChanges();
  return {
    fixture,
    http: TestBed.inject(HttpTestingController),
    host: fixture.nativeElement as HTMLElement,
  };
}
