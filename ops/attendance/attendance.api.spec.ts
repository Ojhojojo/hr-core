import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { AttendanceApi } from './attendance.api';

describe('AttendanceApi', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  function setup() {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    return { api: TestBed.inject(AttendanceApi), http: TestBed.inject(HttpTestingController) };
  }

  it('GETs the register with approval=pending', () => {
    const { api, http } = setup();
    api.register({ from: '2026-09-13', to: '2026-09-19', approval: 'pending' }).subscribe();

    const req = http.expectOne(
      '/api/ops/attendance/register?from=2026-09-13&to=2026-09-19&approval=pending',
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('approval')).toBe('pending');
    req.flush({
      from: '2026-09-13',
      to: '2026-09-19',
      rows: [
        {
          staffId: 'staff-1',
          staffName: 'Danny',
          branchId: 'branch-1',
          branchName: 'Roosevelt Main',
          branchCode: 'main-roosevelt',
          inAtUtc: '2026-09-17T00:02:00.000Z',
          inAtManila: '2026-09-17 8:02 AM',
          inDateManila: '2026-09-17',
          outAtUtc: null,
          outAtManila: null,
          hours: null,
          isOpen: true,
          punchId: 'punch-1',
          approvalStatus: 'pending',
          offsiteReason: 'At a client site',
        },
      ],
    });
  });

  it('POSTs approve and reject with an optional note', () => {
    const { api, http } = setup();
    api.approve('punch-1', 'Looks good').subscribe();
    const approve = http.expectOne('/api/ops/attendance/punches/punch-1/approve');
    expect(approve.request.method).toBe('POST');
    expect(approve.request.body).toEqual({ note: 'Looks good' });
    approve.flush({ id: 'punch-1', approvalStatus: 'approved' });

    api.approve('punch-2').subscribe();
    const approveEmpty = http.expectOne('/api/ops/attendance/punches/punch-2/approve');
    expect(approveEmpty.request.body).toEqual({});
    approveEmpty.flush({ id: 'punch-2', approvalStatus: 'approved' });

    api.reject('punch-3', '  Not at the job  ').subscribe();
    const reject = http.expectOne('/api/ops/attendance/punches/punch-3/reject');
    expect(reject.request.method).toBe('POST');
    expect(reject.request.body).toEqual({ note: 'Not at the job' });
    reject.flush({ id: 'punch-3', approvalStatus: 'rejected' });

    api.reject('punch-4', '   ').subscribe();
    const rejectEmpty = http.expectOne('/api/ops/attendance/punches/punch-4/reject');
    expect(rejectEmpty.request.body).toEqual({});
    rejectEmpty.flush({ id: 'punch-4', approvalStatus: 'rejected' });
  });

  it('passes the approval filter on CSV download', () => {
    const { api, http } = setup();
    api.downloadCsv({ from: '2026-09-13', to: '2026-09-19', approval: 'rejected' }).subscribe();

    const req = http.expectOne(
      '/api/ops/attendance/register.csv?from=2026-09-13&to=2026-09-19&approval=rejected',
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('approval')).toBe('rejected');
    req.flush(new Blob(['Name\n']));
  });

  it('passes from, to, and mine to GET /overtime', () => {
    const { api, http } = setup();
    api.overtime('2026-09-13', '2026-09-19', true).subscribe();

    const mineReq = http.expectOne(
      '/api/ops/attendance/overtime?from=2026-09-13&to=2026-09-19&mine=true',
    );
    expect(mineReq.request.method).toBe('GET');
    expect(mineReq.request.params.get('mine')).toBe('true');
    mineReq.flush({ from: '2026-09-13', to: '2026-09-19', rows: [] });

    api.overtime('2026-09-13', '2026-09-19').subscribe();
    const regularReq = http.expectOne('/api/ops/attendance/overtime?from=2026-09-13&to=2026-09-19');
    expect(regularReq.request.method).toBe('GET');
    expect(regularReq.request.params.has('mine')).toBe(false);
    regularReq.flush({ from: '2026-09-13', to: '2026-09-19', rows: [] });
  });
});
