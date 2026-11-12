/**
 * #682: paid leave is inside the gross but is not a punched day. Say both, so a line with pay
 * and no punches explains itself: "0 days paid · 1 leave day paid".
 */
export function paidDaysLabel(
  daysPaid: number | null | undefined,
  leaveDaysPaid?: number | null,
): string {
  const days = daysPaid ?? 0;
  const base = `${days} day${days === 1 ? '' : 's'} paid`;
  const leave = leaveDaysPaid ?? 0;
  if (leave <= 0) {
    return base;
  }
  return `${base} · ${leave} leave day${leave === 1 ? '' : 's'} paid`;
}
