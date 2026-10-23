import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { ClockApi } from './clock.api';

describe('ClockApi', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  function setup() {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    return { api: TestBed.inject(ClockApi), http: TestBed.inject(HttpTestingController) };
  }

  it('POSTs clock-in GPS and offsiteReason as JSON', () => {
    const { api, http } = setup();
    api.clockIn({ latitude: 14.69, longitude: 121.0 }, null, 'At a client site').subscribe();

    const req = http.expectOne('/api/ops/attendance/clock-in');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      latitude: 14.69,
      longitude: 121.0,
      offsiteReason: 'At a client site',
    });
    req.flush({ id: 'punch-1' });
  });

  it('POSTs clock-in with only offsiteReason when GPS is unavailable', () => {
    const { api, http } = setup();
    api.clockIn(null, null, 'Phone GPS off').subscribe();

    const req = http.expectOne('/api/ops/attendance/clock-in');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ offsiteReason: 'Phone GPS off' });
    req.flush({ id: 'punch-2' });
  });

  it('omits a blank offsiteReason on clock-in', () => {
    const { api, http } = setup();
    api.clockIn({ latitude: 14.6406, longitude: 121.0051 }, null, '  ').subscribe();

    const req = http.expectOne('/api/ops/attendance/clock-in');
    expect(req.request.body).toEqual({
      latitude: 14.6406,
      longitude: 121.0051,
    });
    req.flush({ id: 'punch-3' });
  });

  it('POSTs clock-out offsiteReason and appends it on FormData when a photo is attached', () => {
    const { api, http } = setup();
    api.clockOut(null, null, 'Heading home').subscribe();
    const json = http.expectOne('/api/ops/attendance/clock-out');
    expect(json.request.method).toBe('POST');
    expect(json.request.body).toEqual({ offsiteReason: 'Heading home' });
    json.flush({ id: 'punch-4' });

    const photo = new File(['x'], 'clock.jpg', { type: 'image/jpeg' });
    api.clockIn({ latitude: 14.69, longitude: 121.0 }, photo, 'At a client site').subscribe();
    const form = http.expectOne('/api/ops/attendance/clock-in');
    expect(form.request.body).toBeInstanceOf(FormData);
    const data = form.request.body as FormData;
    expect(data.get('offsiteReason')).toBe('At a client site');
    expect(data.get('latitude')).toBe('14.69');
    expect(data.get('longitude')).toBe('121');
    expect((data.get('photo') as File).name).toBe('clock.jpg');
    form.flush({ id: 'punch-5' });
  });
});
