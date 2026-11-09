# Agent notes

## Editing hr-core

hr-core may be edited, but only with additive, backward-compatible changes that keep rakkii working, verified against rakkii before merge.

This matches Bienh's standing rule.

- New optional inputs, new exports, opt-in flags, and additive migrations only.
- No renames, removals, or changed defaults.
- New hr-core tables ship behind an opt-in host flag, off by default, so rakkii stays drop-in. The host turns the flag on in `HrModel.Configure` and `AddHrCore` together, and passes the same value. With the flag off, the tables are not in the EF model, their routes are not mapped, and payroll does not query them. `PayrollSchedules` is that flag for cut-offs, deduction schedules, and catch-ups. The ops screen reads the optional `HR_PAYROLL_SCHEDULES` injection token, which defaults to false, so the panels and their calls stay hidden until the host provides `true`.
- `EmployerRemittance` is the host flag for the remittance report and `BranchRemittanceSetting`. Off by default. The employer-share columns on `PayrollRunLine` are always in the model (nullable, default 0). With the flag off, or the branch setting off, a lock leaves those columns null and payslips stay as they are. The ops screen reads `HR_EMPLOYER_REMITTANCE`, which defaults to false.
- Company-specific values, including payroll cut-off dates and deduction schedules, are settings or config data, never hard-coded.
- If a breaking change seems necessary, stop and report the tradeoff instead of making it.

Rakkii and Qurio each keep a pinned copy of `api/` and `ops/` and compile it in. A change here is not live in an app until that app bumps the pin and, when `HrModel` gains a table or column, applies the matching additive migration.

See README.md for the host contract (partial classes, `IHrDb`, `@hr-host`).
