/** Default shop clock radius, matching Branch.DefaultClockRadiusMeters. */
export const DEFAULT_CLOCK_RADIUS_METERS = 150;

export const OFFSITE_WARNING = "You're not at the shop";
export const OFFSITE_REASON_REQUIRED = 'A reason is required for an off-site punch.';

export type GpsReading =
  | { status: 'unknown' }
  | { status: 'unavailable' }
  | { status: 'ok'; latitude: number; longitude: number };

export interface ShopPin {
  latitude?: number | null;
  longitude?: number | null;
  clockRadiusMeters?: number | null;
}

export interface OffsitePunchInput {
  allowOffsitePunchWithApproval: boolean;
  gps: GpsReading;
  shop?: ShopPin | null;
  offsiteReason: string;
  /** Set when the API already said this punch is off-site. */
  knownOffsite?: boolean;
}

/** Haversine metres between two WGS84 points (same formula as the API). */
export function metresBetween(lat1: number, lon1: number, lat2: number, lon2: number): number {
  const earth = 6_371_000;
  const toRad = (degrees: number) => (degrees * Math.PI) / 180;
  const dLat = toRad(lat2 - lat1);
  const dLon = toRad(lon2 - lon1);
  const a =
    Math.sin(dLat / 2) * Math.sin(dLat / 2) +
    Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLon / 2) * Math.sin(dLon / 2);
  const c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
  return earth * c;
}

/**
 * True when GPS is a point outside the shop pin and radius.
 * Missing GPS is not "outside" — that is handled as unavailable.
 * A missing shop pin cannot be measured, so this is false.
 */
export function isOutsideShopRadius(shop: ShopPin | null | undefined, gps: GpsReading): boolean {
  if (gps.status !== 'ok') {
    return false;
  }
  if (shop?.latitude == null || shop?.longitude == null) {
    return false;
  }

  const radius =
    shop.clockRadiusMeters != null && shop.clockRadiusMeters > 0
      ? shop.clockRadiusMeters
      : DEFAULT_CLOCK_RADIUS_METERS;
  return metresBetween(shop.latitude, shop.longitude, gps.latitude, gps.longitude) > radius;
}

/**
 * An off-site reason is required when the shop allows pending off-site punches and
 * the phone is outside the radius, GPS is denied/unavailable, or the API already
 * treated the punch as off-site. GPS that has not been read yet does not require a reason.
 */
export function needsOffsiteReason(input: OffsitePunchInput): boolean {
  if (!input.allowOffsitePunchWithApproval) {
    return false;
  }
  if (input.knownOffsite) {
    return true;
  }
  if (input.gps.status === 'unknown') {
    return false;
  }
  if (input.gps.status === 'unavailable') {
    return true;
  }
  return isOutsideShopRadius(input.shop, input.gps);
}

/**
 * Self clock-in/out may be sent when a required off-site reason is filled, or when
 * the punch is on-site. GPS that has not been read yet is allowed so the first tap
 * can request location. With the setting off, a denied GPS or a point outside the
 * radius cannot be submitted.
 */
export function canSubmitPunch(input: OffsitePunchInput): boolean {
  if (needsOffsiteReason(input) && input.offsiteReason.trim().length === 0) {
    return false;
  }
  if (!input.allowOffsitePunchWithApproval) {
    if (input.gps.status === 'unavailable') {
      return false;
    }
    if (isOutsideShopRadius(input.shop, input.gps)) {
      return false;
    }
  }
  return true;
}
