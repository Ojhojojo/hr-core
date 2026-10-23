/** Loan and cash-advance labels. Pure; no Angular. */

const STATUS_LABELS: Record<string, string> = {
  active: 'Active',
  paid: 'Paid',
  cancelled: 'Cancelled',
};

const DEFAULT_LOAN_KINDS = ['Cash advance', 'SSS loan', 'Pag-IBIG loan', 'Company loan'];

export interface LoanItemLabelInput {
  kind?: string | null;
  amount?: number | null;
  balanceAfter?: number | null;
  loanBalance?: number | null;
  balance?: number | null;
}

export function loanStatusLabel(status: string | null | undefined): string {
  const raw = (status ?? '').trim();
  if (!raw) {
    return '';
  }
  return STATUS_LABELS[raw.toLowerCase()] ?? raw;
}

/** Instalments still to take. Zero once nothing is owed or the per-period amount is not positive. */
export function periodsRemaining(balance: number, perPeriod: number): number {
  const left = Number(balance);
  const each = Number(perPeriod);
  if (!Number.isFinite(left) || !Number.isFinite(each) || left <= 0 || each <= 0) {
    return 0;
  }
  return Math.ceil(left / each);
}

/** Share of principal already repaid, clamped to 0..1. */
export function loanProgress(principal: number, balance: number): number {
  const base = Number(principal);
  const left = Number(balance);
  if (!Number.isFinite(base) || !Number.isFinite(left) || !(base > 0)) {
    return 0;
  }
  const ratio = (base - left) / base;
  if (ratio <= 0) {
    return 0;
  }
  if (ratio >= 1) {
    return 1;
  }
  return ratio;
}

/** Compact peso: ₱1,000, ₱1,000.50. */
export function formatLoanPeso(amount: number | null | undefined): string {
  const value = Number(amount ?? 0);
  const safe = Number.isFinite(value) ? value : 0;
  const negative = safe < 0;
  const abs = Math.abs(safe);
  const cents = Math.round(abs * 100) % 100;
  const text = abs.toLocaleString('en-US', {
    minimumFractionDigits: cents === 0 ? 0 : 2,
    maximumFractionDigits: 2,
  });
  return negative ? `-₱${text}` : `₱${text}`;
}

/** e.g. "Cash advance ₱1,000 (balance ₱3,000)". */
export function loanItemLabel(item: LoanItemLabelInput): string {
  const kind = (item.kind ?? '').trim() || 'Loan';
  const balance = item.balanceAfter ?? item.loanBalance ?? item.balance ?? 0;
  return `${kind} ${formatLoanPeso(item.amount)} (balance ${formatLoanPeso(balance)})`;
}

export function loanKindSuggestions(suggested: readonly string[] | null | undefined): string[] {
  const cleaned = (suggested ?? []).map((name) => name.trim()).filter((name) => name.length > 0);
  const source = cleaned.length > 0 ? cleaned : DEFAULT_LOAN_KINDS;
  const seen = new Set<string>();
  const out: string[] = [];
  for (const name of source) {
    const key = name.toLowerCase();
    if (seen.has(key)) {
      continue;
    }
    seen.add(key);
    out.push(name);
  }
  return out;
}

/** Register override key for one loan instalment. */
export function loanOverrideKey(loanId: string): string {
  return `loan:${loanId}`;
}

/** True when the register row carries a loan total or instalment lines. */
export function rowHasLoans(row: {
  loanDeductions?: number | null;
  loanItems?: readonly unknown[] | null;
}): boolean {
  return row.loanDeductions != null || (row.loanItems?.length ?? 0) > 0;
}

/**
 * Delete is allowed only when no payment exists. A loaded payment list wins,
 * including reversed rows. Without it, a positive paid total is enough to refuse.
 */
export function loanDeleteAllowed(
  loan: { paid?: number | null },
  payments?: readonly unknown[] | null,
): boolean {
  if (payments != null) {
    return payments.length === 0;
  }
  const paid = Number(loan.paid ?? 0);
  return !Number.isFinite(paid) || !(paid > 0);
}
