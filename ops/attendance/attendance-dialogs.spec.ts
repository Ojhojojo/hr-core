import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { By } from '@angular/platform-browser';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MatDatepickerInput } from '@angular/material/datepicker';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ClosePunchDialog, PunchTimeField } from './attendance-dialogs';

function pickDate(fixture: ComponentFixture<unknown>, name: string, value: Date | string): void {
  const input = fixture.debugElement.query(By.css(`input[name="${name}"]`));
  const dateInput = input.injector.get(MatDatepickerInput);
  dateInput.dateChange.emit({ value } as never);
}

function setField(fixture: ComponentFixture<unknown>, name: string, value: string): void {
  const field = fixture.debugElement.query(By.css(`[name="${name}"]`));
  const el = field.nativeElement as HTMLElement;
  if (el.tagName === 'MAT-SELECT') {
    field.triggerEventHandler('selectionChange', { value });
    return;
  }
  const input = el as HTMLInputElement | HTMLSelectElement;
  input.value = value;
  const eventName = input.tagName === 'SELECT' ? 'change' : 'input';
  field.triggerEventHandler(eventName, { target: input });
}

function setPunchTime(fixture: ComponentFixture<unknown>, name: string, hhmm: string): void {
  const fields = fixture.debugElement.queryAll(By.directive(PunchTimeField));
  const match = fields.find((f) => f.componentInstance.name() === name);
  expect(match).toBeTruthy();
  match!.componentInstance.value.set(hhmm);
  fixture.detectChanges();
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

  it('uses a Material timepicker field instead of native type=time', async () => {
    const fixture = await mount(vi.fn());
    const root = fixture.nativeElement as HTMLElement;
    const field = root.querySelector('app-punch-time-field');
    expect(root.querySelector('input[type="time"]')).toBeNull();
    expect(field).toBeTruthy();
    expect(field?.querySelector('mat-timepicker')).toBeTruthy();
    expect(field?.querySelector('input.mat-timepicker-input')).toBeTruthy();
    // Dump once if the toggle is missing so we can see projection.
    const toggle =
      field?.querySelector('mat-timepicker-toggle') ??
      field?.querySelector('.mat-mdc-form-field-icon-suffix');
    expect(toggle, field?.innerHTML).toBeTruthy();
  });

  it('sends clock-out as yyyy-MM-ddTHH:mm from the time field', async () => {
    const close = vi.fn();
    const fixture = await mount(close);

    pickDate(fixture, 'outDate', '2026-01-05');
    setPunchTime(fixture, 'outTime', '17:45');
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
    setPunchTime(fixture, 'outTime', '17:45');
    submit(fixture, 'close-punch-form');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(close).not.toHaveBeenCalled();
    const alert =
      (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]') ??
      document.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('A reason is required to close the punch.');
  });
});

describe('PunchTimeField', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('keeps HH:mm on the model and renders a timepicker', async () => {
    await TestBed.configureTestingModule({
      imports: [PunchTimeField],
      providers: [provideZonelessChangeDetection(), provideNoopAnimations()],
    }).compileComponents();

    const fixture = TestBed.createComponent(PunchTimeField);
    fixture.componentRef.setInput('name', 'outTime');
    fixture.componentInstance.value.set('17:45');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.componentInstance.value()).toBe('17:45');
    expect(fixture.nativeElement.querySelector('mat-timepicker')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('input.mat-timepicker-input')).toBeTruthy();
  });
});