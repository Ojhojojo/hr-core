import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { By } from '@angular/platform-browser';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MatDatepickerInput } from '@angular/material/datepicker';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MEMBER_ROLE } from '../host-spec-fixtures';
import { ClosePunchDialog, PunchForDialog } from './attendance-dialogs';

/**
 * A native datepicker stores a `Date`. Stringifying it is not `yyyy-MM-dd`.
 * Stubbing `toISOString` and the UTC getters onto another day makes a
 * `toISOString().slice(0, 10)` regression fail when the runner is UTC too.
 */
function disagreeUtc(local: Date, utcIso: string): Date {
  const utc = new Date(utcIso);
  vi.spyOn(local, 'toISOString').mockReturnValue(utcIso);
  vi.spyOn(local, 'getUTCFullYear').mockReturnValue(utc.getUTCFullYear());
  vi.spyOn(local, 'getUTCMonth').mockReturnValue(utc.getUTCMonth());
  vi.spyOn(local, 'getUTCDate').mockReturnValue(utc.getUTCDate());
  vi.spyOn(local, 'getUTCDay').mockReturnValue(utc.getUTCDay());
  vi.spyOn(local, 'getUTCHours').mockReturnValue(utc.getUTCHours());
  vi.spyOn(local, 'getUTCMinutes').mockReturnValue(utc.getUTCMinutes());
  vi.spyOn(local, 'getUTCSeconds').mockReturnValue(utc.getUTCSeconds());
  vi.spyOn(local, 'getUTCMilliseconds').mockReturnValue(utc.getUTCMilliseconds());
  return local;
}

const staff = [{ id: 'staff-1', displayName: 'Alex Rivera', role: MEMBER_ROLE }];

function pickDate(fixture: ComponentFixture<unknown>, name: string, value: Date | string): void {
  const input = fixture.debugElement.query(By.css(`input[name="${name}"]`));
  const dateInput = input.injector.get(MatDatepickerInput);
  dateInput.dateChange.emit({ value } as never);
}

function setField(fixture: ComponentFixture<unknown>, name: string, value: string): void {
  const field = fixture.debugElement.query(By.css(`[name="${name}"]`));
  const input = field.nativeElement as HTMLInputElement | HTMLSelectElement;
  input.value = value;
  const eventName = input.tagName === 'SELECT' ? 'change' : 'input';
  field.triggerEventHandler(eventName, { target: input });
}

function submit(fixture: ComponentFixture<unknown>, formId: string): void {
  const form =
    (fixture.nativeElement as HTMLElement).querySelector(`#${formId}`) ??
    document.getElementById(formId);
  if (!(form instanceof HTMLFormElement)) {
    throw new Error(`Form #${formId} was not rendered`);
  }
  form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
}

async function expectStillOpen(
  fixture: ComponentFixture<unknown>,
  close: ReturnType<typeof vi.fn>,
  message: string,
): Promise<void> {
  fixture.detectChanges();
  await fixture.whenStable();
  expect(close).not.toHaveBeenCalled();
  const root = fixture.nativeElement as HTMLElement;
  const alert = root.querySelector('[role="alert"]') ?? document.querySelector('[role="alert"]');
  expect(alert?.textContent).toContain(message);
}

describe('Punch for dialog', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  async function mount(close: ReturnType<typeof vi.fn>) {
    await TestBed.configureTestingModule({
      imports: [PunchForDialog],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: { staff } },
        { provide: MatDialogRef, useValue: { close } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(PunchForDialog);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  }

  it('sends native datepicker dates as Manila yyyy-MM-ddTHH:mm', async () => {
    const close = vi.fn();
    const fixture = await mount(close);
    const inDate = disagreeUtc(new Date(2026, 0, 5), '2026-01-04T16:00:00.000Z');
    const outDate = disagreeUtc(new Date(2026, 0, 6, 23, 30), '2026-01-07T07:30:00.000Z');

    setField(fixture, 'punchStaff', 'staff-1');
    pickDate(fixture, 'inDate', inDate);
    setField(fixture, 'inTime', '08:15');
    pickDate(fixture, 'outDate', outDate);
    setField(fixture, 'outTime', '17:45');
    submit(fixture, 'punch-for-form');

    expect(close).toHaveBeenCalledWith({
      staffId: 'staff-1',
      inAtManila: '2026-01-05T08:15',
      outAtManila: '2026-01-06T17:45',
    });
    expect(inDate.toISOString).not.toHaveBeenCalled();
    expect(outDate.toISOString).not.toHaveBeenCalled();
  });

  it('uses the clock-in day when clock-out has a time and no date', async () => {
    const close = vi.fn();
    const fixture = await mount(close);
    const inDate = disagreeUtc(new Date(2026, 0, 5), '2026-01-04T16:00:00.000Z');

    setField(fixture, 'punchStaff', 'staff-1');
    pickDate(fixture, 'inDate', inDate);
    setField(fixture, 'inTime', '08:15');
    setField(fixture, 'outTime', '17:45');
    submit(fixture, 'punch-for-form');

    expect(close).toHaveBeenCalledWith({
      staffId: 'staff-1',
      inAtManila: '2026-01-05T08:15',
      outAtManila: '2026-01-05T17:45',
    });
    expect(inDate.toISOString).not.toHaveBeenCalled();
  });

  it('leaves an iso day from a string date adapter unchanged', async () => {
    const close = vi.fn();
    const fixture = await mount(close);

    setField(fixture, 'punchStaff', 'staff-1');
    pickDate(fixture, 'inDate', '2026-01-05');
    setField(fixture, 'inTime', '08:15');
    pickDate(fixture, 'outDate', '2026-01-06');
    setField(fixture, 'outTime', '17:45');
    submit(fixture, 'punch-for-form');

    expect(close).toHaveBeenCalledWith({
      staffId: 'staff-1',
      inAtManila: '2026-01-05T08:15',
      outAtManila: '2026-01-06T17:45',
    });
  });

  it('does not close when the clock-in date is missing', async () => {
    const close = vi.fn();
    const fixture = await mount(close);

    setField(fixture, 'punchStaff', 'staff-1');
    setField(fixture, 'inTime', '08:15');
    submit(fixture, 'punch-for-form');

    await expectStillOpen(fixture, close, 'Enter a clock-in date and time.');
  });

  it('does not close when the clock-in date is invalid', async () => {
    const close = vi.fn();
    const fixture = await mount(close);

    setField(fixture, 'punchStaff', 'staff-1');
    pickDate(fixture, 'inDate', new Date(Number.NaN));
    setField(fixture, 'inTime', '08:15');
    submit(fixture, 'punch-for-form');

    await expectStillOpen(fixture, close, 'Enter a clock-in date and time.');
  });

  it('does not close when the clock-out date is invalid', async () => {
    const close = vi.fn();
    const fixture = await mount(close);

    setField(fixture, 'punchStaff', 'staff-1');
    pickDate(fixture, 'inDate', new Date(2026, 0, 5));
    setField(fixture, 'inTime', '08:15');
    pickDate(fixture, 'outDate', new Date(Number.NaN));
    setField(fixture, 'outTime', '17:45');
    submit(fixture, 'punch-for-form');

    await expectStillOpen(fixture, close, 'Enter a clock-out date and time.');
  });
});

describe('Close punch dialog', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  async function mount(close: ReturnType<typeof vi.fn>) {
    await TestBed.configureTestingModule({
      imports: [ClosePunchDialog],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideNativeDateAdapter(),
        {
          provide: MAT_DIALOG_DATA,
          useValue: { staffName: 'Alex Rivera', inAtManila: '2026-01-05 8:15 AM' },
        },
        { provide: MatDialogRef, useValue: { close } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ClosePunchDialog);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  }

  it('sends a native datepicker date as Manila yyyy-MM-ddTHH:mm', async () => {
    const close = vi.fn();
    const fixture = await mount(close);
    const outDate = disagreeUtc(new Date(2026, 0, 5, 23, 30), '2026-01-06T07:30:00.000Z');

    pickDate(fixture, 'outDate', outDate);
    setField(fixture, 'outTime', '17:45');
    setField(fixture, 'reason', 'Shift ended');
    submit(fixture, 'close-punch-form');

    expect(close).toHaveBeenCalledWith({
      outAtManila: '2026-01-05T17:45',
      reason: 'Shift ended',
    });
    expect(outDate.toISOString).not.toHaveBeenCalled();
  });

  it('leaves an iso day from a string date adapter unchanged', async () => {
    const close = vi.fn();
    const fixture = await mount(close);

    pickDate(fixture, 'outDate', '2026-01-05');
    setField(fixture, 'outTime', '17:45');
    setField(fixture, 'reason', 'Shift ended');
    submit(fixture, 'close-punch-form');

    expect(close).toHaveBeenCalledWith({
      outAtManila: '2026-01-05T17:45',
      reason: 'Shift ended',
    });
  });

  it('does not close without a reason', async () => {
    const close = vi.fn();
    const fixture = await mount(close);

    pickDate(fixture, 'outDate', '2026-01-05');
    setField(fixture, 'outTime', '17:45');
    submit(fixture, 'close-punch-form');

    await expectStillOpen(fixture, close, 'A reason is required to close the punch.');
  });

  it('does not close when the clock-out date is invalid', async () => {
    const close = vi.fn();
    const fixture = await mount(close);

    pickDate(fixture, 'outDate', new Date(Number.NaN));
    setField(fixture, 'outTime', '17:45');
    setField(fixture, 'reason', 'Shift ended');
    submit(fixture, 'close-punch-form');

    await expectStillOpen(fixture, close, 'Enter a clock-out date and time.');
  });
});
