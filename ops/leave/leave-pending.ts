/**
 * #681: days left already take off pending requests, so approving one leaves the number as it is.
 * This label says how many days are being held, or nothing when none are.
 */
export function pendingHeldLabel(pending: number | null | undefined): string | null {
  if (pending == null || pending <= 0) {
    return null;
  }
  return `${pending} pending`;
}
