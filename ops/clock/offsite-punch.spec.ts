import { describe, expect, it } from 'vitest';
import {
  DEFAULT_CLOCK_RADIUS_METERS,
  canSubmitPunch,
  isOutsideShopRadius,
  metresBetween,
  needsOffsiteReason,
  type GpsReading,
  type OffsitePunchInput,
  type ShopPin,
} from './offsite-punch';

const shop: ShopPin = {
  latitude: 14.6406,
  longitude: 121.0051,
  clockRadiusMeters: DEFAULT_CLOCK_RADIUS_METERS,
};

const inside: GpsReading = { status: 'ok', latitude: 14.6406, longitude: 121.0051 };
const justInside: GpsReading = { status: 'ok', latitude: 14.6415, longitude: 121.0051 };
const outside: GpsReading = { status: 'ok', latitude: 14.6906, longitude: 121.0051 };
const unknown: GpsReading = { status: 'unknown' };
const unavailable: GpsReading = { status: 'unavailable' };

function input(patch: Partial<OffsitePunchInput>): OffsitePunchInput {
  return {
    allowOffsitePunchWithApproval: true,
    gps: unknown,
    shop,
    offsiteReason: '',
    ...patch,
  };
}

describe('off-site punch', () => {
  it('treats a point inside the radius as on-site and a point beyond it as off-site', () => {
    expect(metresBetween(shop.latitude!, shop.longitude!, inside.latitude, inside.longitude)).toBe(
      0,
    );
    expect(isOutsideShopRadius(shop, justInside)).toBe(false);
    expect(isOutsideShopRadius(shop, outside)).toBe(true);
    expect(isOutsideShopRadius(shop, unavailable)).toBe(false);
    expect(isOutsideShopRadius({ ...shop, latitude: null, longitude: null }, outside)).toBe(false);
    expect(isOutsideShopRadius(null, outside)).toBe(false);
  });

  it('needs a reason only when the setting is on and the phone is off-site or GPS is unavailable', () => {
    expect(needsOffsiteReason(input({ gps: unknown }))).toBe(false);
    expect(needsOffsiteReason(input({ gps: inside }))).toBe(false);
    expect(needsOffsiteReason(input({ gps: justInside }))).toBe(false);
    expect(needsOffsiteReason(input({ gps: outside }))).toBe(true);
    expect(needsOffsiteReason(input({ gps: unavailable }))).toBe(true);
    expect(needsOffsiteReason(input({ gps: outside, knownOffsite: true }))).toBe(true);
    expect(needsOffsiteReason(input({ allowOffsitePunchWithApproval: false, gps: outside }))).toBe(
      false,
    );
    expect(
      needsOffsiteReason(input({ allowOffsitePunchWithApproval: false, gps: unavailable })),
    ).toBe(false);
  });

  it('blocks submit until a non-blank reason is given for an off-site punch', () => {
    expect(canSubmitPunch(input({ gps: unknown }))).toBe(true);
    expect(canSubmitPunch(input({ gps: inside }))).toBe(true);
    expect(canSubmitPunch(input({ gps: outside }))).toBe(false);
    expect(canSubmitPunch(input({ gps: unavailable }))).toBe(false);
    expect(canSubmitPunch(input({ gps: outside, offsiteReason: '   ' }))).toBe(false);
    expect(canSubmitPunch(input({ gps: outside, offsiteReason: 'At a client site' }))).toBe(true);
    expect(canSubmitPunch(input({ gps: unavailable, offsiteReason: 'Phone GPS off' }))).toBe(true);
    expect(canSubmitPunch(input({ allowOffsitePunchWithApproval: false, gps: unavailable }))).toBe(
      false,
    );
    expect(canSubmitPunch(input({ allowOffsitePunchWithApproval: false, gps: outside }))).toBe(
      false,
    );
    expect(canSubmitPunch(input({ allowOffsitePunchWithApproval: false, gps: inside }))).toBe(true);
  });
});
