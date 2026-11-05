# Agent notes

## Editing hr-core

hr-core may be edited, but only with additive, backward-compatible changes that keep rakkii working, verified against rakkii before merge.

This matches Bienh's standing rule.

- New optional inputs, new exports, opt-in flags, and additive migrations only.
- No renames, removals, or changed defaults.
- Company-specific values, including payroll cut-off dates and deduction schedules, are settings or config data, never hard-coded.
- If a breaking change seems necessary, stop and report the tradeoff instead of making it.

Rakkii and Qurio each keep a pinned copy of `api/` and `ops/` and compile it in. A change here is not live in an app until that app bumps the pin and, when `HrModel` gains a table or column, applies the matching additive migration.

See README.md for the host contract (partial classes, `IHrDb`, `@hr-host`).
