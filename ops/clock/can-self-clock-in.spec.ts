import { describe, expect, it } from 'vitest';
import { canSelfClockIn } from './clock';
import { AttendanceMe } from './clock.models';

function me(mustClock?: boolean): AttendanceMe {
  return {
    clockedIn: false,
    status: "You're clocked out",
    open: null,
    today: [],
    ...(mustClock === undefined ? {} : { mustClock }),
  };
}

describe('canSelfClockIn', () => {
  it('allows a must-clock person', () => {
    expect(canSelfClockIn(me(true))).toBe(true);
  });

  it('blocks a person who is not set to must clock', () => {
    expect(canSelfClockIn(me(false))).toBe(false);
  });

  it('allows when the status has not loaded or the API does not send mustClock', () => {
    expect(canSelfClockIn(null)).toBe(true);
    expect(canSelfClockIn(me())).toBe(true);
  });
});
