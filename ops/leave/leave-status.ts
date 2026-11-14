/**
 * Reader-facing label for a leave request status: "Pending", "Approved", and so on.
 * An unknown stored value is capitalised rather than shown raw.
 */
export function leaveStatusLabel(status: string | null | undefined): string {
  switch (status) {
    case 'pending':
      return 'Pending';
    case 'approved':
      return 'Approved';
    case 'rejected':
      return 'Rejected';
    case 'cancelled':
      return 'Cancelled';
    case null:
    case undefined:
    case '':
      return '—';
    default: {
      const spaced = status.replace(/_/g, ' ').trim();
      return spaced.charAt(0).toUpperCase() + spaced.slice(1);
    }
  }
}
