# hr-core

Shared HR for Rakkii and Qurio: staff, the employee directory, employment documents,
attendance and clock, leave, payroll (earnings, statutory, runs, loans),
announcements, and the HR and settings change logs.

| Folder | What it is                                      | Where an app puts it                                          |
| ------ | ----------------------------------------------- | ------------------------------------------------------------- |
| `api/` | C# (.NET 10) source under `HrCore.*` namespaces | `packages/hr-core/api`, compiled into the API as linked files |
| `ops/` | Angular screens                                 | `apps/ops/src/hr-core`, inside the Angular app                |

There is no package feed. Each app keeps a pinned copy of this repo's folders and
updates it with its sync script, so the version an app runs is always one commit
here. Make HR changes in this repo first, then bump the pin in each app.

## What the app supplies (API)

The package uses partial classes and partial methods, so the build fails if the app
leaves any of these out.

| The app provides                                                                                                                                                                                                                                                                            | Rakkii example                                                                                                                 |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| `StaffRole` enum, which must include `Owner` and `Admin`                                                                                                                                                                                                                                    | `apps/api/Shared/Domain/StaffRole.cs`                                                                                          |
| `HrRoles` answers: who is desk staff, who clocks in by default, who sees only themselves in the staff list, and the role for unknown people                                                                                                                                                 | `apps/api/Features/HrAdapters/ShopHrAppContract.cs`                                                                            |
| `StaffAccessFlags`: app-specific staff permissions, read from and written to the staff JSON                                                                                                                                                                                                 | same file                                                                                                                      |
| A DbContext that implements `IHrDb` and calls `HrModel.Configure(modelBuilder)`                                                                                                                                                                                                             | `apps/api/Shared/Data/AppDbContext.cs`                                                                                         |
| Optionally `HrCoreFeatures`. `PayrollSchedules`, `EmployerRemittance`, `LoanKinds`, `PayoutHold`, and `Deposits` default to off. Pass the same instance to `HrModel.Configure(modelBuilder, features)` and `AddHrCore<AppDbContext>(features)`. Do not add `DbSet` properties for those entities until the flag is on | Qurio turns `PayrollSchedules` on, and `EmployerRemittance`, `LoanKinds`, `PayoutHold`, or `Deposits` when it wants those screens. Rakkii leaves them off |
| `IStaffInvite` (how new staff get a sign-in)                                                                                                                                                                                                                                                | `apps/api/Shared/Infrastructure/SupabaseStaffInvite.cs`                                                                        |
| Optionally `IStaffWorkHooks`, for open work a person holds (Rakkii: jobs)                                                                                                                                                                                                                   | `apps/api/Features/Jobs/StaffWork/JobStaffWork.cs`                                                                             |
| Its own fields on `Organization`, `Branch` and `Staff`, as partial class parts                                                                                                                                                                                                              | `apps/api/Shared/Domain/*.Shop.cs`                                                                                             |

Then in `Program.cs`, call `services.AddHrCore<AppDbContext>()` and `app.MapHrCore()`.

`PayrollSchedules` (on `HrCoreFeatures`) covers `BranchPayrollCutoff`, `BranchDeductionSchedule`, and `PayrollCatchUp`. It is off by default. Off, those entities are not in the EF model, `/api/ops/payroll/cutoff-schedule`, `/api/ops/payroll/deduction-schedule`, and `/api/ops/payroll/catch-ups` are not mapped, and the register and loan pricing do not query them. On, cut-offs and deduction schedules behave as they do for a host that opted in. The payroll screen hides those panels unless the host provides the `HR_PAYROLL_SCHEDULES` injection token with `true` (`ops/payroll/payroll-schedules.ts`). Rakkii leaves both off. Qurio sets both on.

`EmployerRemittance` covers the per-branch remittance switch (`BranchRemittanceSetting`), the employer-share columns on `PayrollRunLine` (`EmployerSss`, `EmployerPhilHealth`, `EmployerPagIbig`, nullable `numeric(14,2)` with no default, check `CK_PayrollRunLines_EmployerShares`), and `/api/ops/payroll/remittance`. It is off by default. Off, that entity is not in the EF model, those columns are ignored, the check is not in the model, the report routes are not mapped, and locking a run does not query them. A host that leaves the flag off keeps its current database. Payslips do not show employer shares. The payroll screen hides the Remittance panel unless the host provides `HR_EMPLOYER_REMITTANCE` with `true` (`ops/payroll/payroll-remittance.ts`). Rakkii leaves the flag off. Qurio sets it on, applies the columns, the check, and `BranchRemittanceSettings`, then an owner turns the report on for each shop.

`LoanKinds` covers the loan kind list (`LoanKind`: name, active, cut-off rule) and the nullable `StaffLoan.LoanKindId`. It is off by default. Off, that table is not in the EF model, the column is ignored, `/api/ops/payroll/loan-kinds` is not mapped, and payroll does not query them. A free-text loan keeps deducting every period. On, a loan that points at a kind follows that kind's rule: no cut-off number means every period, and `1` means cut-off 1 only on the worker's shop. The payroll screen hides the Loan kinds panel unless the host provides `HR_LOAN_KINDS` with `true` (`ops/payroll/payroll-loan-kinds.ts`). With the token on, a new loan picks from the list. A loan that has no kind id stays free text. Rakkii leaves the flag off. Qurio sets `LoanKinds` and `PayrollSchedules` on together, then applies `LoanKinds` and `StaffLoans.LoanKindId`.

`PayoutHold` covers a per-staff hold (`StaffPayoutHold`: reason, who held it, and the release date) and the hold columns on `PayrollRunLine` (`PayoutStatus`, `HoldReason`, `ReleasedOn`, `ReleasedByStaffId`). It is off by default. Off, that table is not in the EF model, those columns are ignored, `/api/ops/payroll/payout-holds` is not mapped, and payroll does not query them. A lock stores the same lines as today. On, an owner or admin can hold a person's payout with a reason. Lock still computes the line and marks it `held`. The run's `totalHeld` is the net of lines still held, separate from `totalNet`. Releasing the hold records the Manila date (`yyyy-MM-dd`) and who released it on the line and on the HR change log. The payroll screen hides the Payout hold panel unless the host provides `HR_PAYOUT_HOLD` with `true` (`ops/payroll/payroll-payout-hold.ts`). Rakkii leaves the flag off. Qurio sets `PayoutHold` on when it wants clearance holds, then applies `StaffPayoutHolds` and the line columns.

`Deposits` covers the nullable `PayrollRunLine.DepositedAmount` (`numeric(14,2)`, no default). It is off by default. Off, that column is ignored, `PUT /api/ops/payroll/runs/{runId}/deposit` is not mapped, and payroll does not query it. A lock stores the same lines as today. On, an owner or admin records what was deposited after lock. Discrepancy is net minus that amount and is not stored. The run's `discrepancies` lists lines whose discrepancy is not zero. A negative amount or more than 2 decimal places is 400. A draft or void run is 409. Gross, deductions, and net stay frozen. Payslips do not gain deposit fields. The entry is `payroll.deposit` on the HR change log. The payroll screen hides the Deposits panel unless the host provides `HR_PAYROLL_DEPOSITS` with `true` (`ops/payroll/payroll-deposits.ts`), and then only on a locked period. Rakkii leaves the flag off. Qurio sets `Deposits` on when it wants deposit checks, then applies `PayrollRunLines.DepositedAmount`.

```ts
import { HR_PAYROLL_DEPOSITS } from './payroll/payroll-deposits';

{ provide: HR_PAYROLL_DEPOSITS, useValue: true }
```

`api/check/HrCore.Check.csproj` builds the package with a minimal stand-in host
(`api/check/Host/MinimalHost.cs`). CI runs it on every PR, so nothing app-specific can
creep in.

## What the app supplies (screens)

The screens import app code only through `@hr-host`. The app maps that name to its own
`src/app/hr-host.ts` in `tsconfig.json` and blocks any other import out of the folder
with a lint rule. `hr-host.ts` exports:

- sign-in, roles and guards (`AuthService`, `ownerOrAdminGuard`, ...)
- the business's name and address
- shared UI components, dialogs and toasts
- money and Manila date helpers
- staff availability, and `canBeAssignedJobs(role)` (who may take over open work)
- `HR_REMITTANCE_ACCESS` and `HR_EMPLOYER_SHARE_REVIEW` (`ops/payroll/payroll-remittance.ts`): who may open the remittance report (`canView(user)`), and who may confirm employer shares (`canReview(user)`). Both are Angular injection tokens with a deny-all default. A host that does not provide them sees no remittance panel and no review panel. No new `@hr-host` export is required.

```ts
import { HR_EMPLOYER_SHARE_REVIEW, HR_REMITTANCE_ACCESS } from './payroll/payroll-remittance';

providers: [
  {
    provide: HR_REMITTANCE_ACCESS,
    useValue: { canView: (user) => user?.staffRole === 'owner' || user?.staffRole === 'admin' },
  },
  {
    provide: HR_EMPLOYER_SHARE_REVIEW,
    useValue: { canReview: (user) => user?.staffRole === 'owner' },
  },
];
```

- `canPickHomeShop(me)`: whether the signed-in owner may choose a person's home shop
- `clocksByDefault(role)`: whether a new person of that role starts with Uses time clock on (`mustClock`). Use the same rule as the API's `HrRoles.ClocksByDefault`

The People dialog reads the optional `HR_DIRECTORY_ROLES` injection token (`ops/employee-directory/directory-roles.ts`). Each entry is `{ value, label, hint }`. Omit the provider and Add person and Edit person stay Admin, Technician, Sales, and Owner, in that order, with the same labels and hints. A stored role that is not in the supplied list stays on the picker so the select is not blank. Provide it the same way as `HR_PAYROLL_SCHEDULES`:

```ts
{ provide: HR_DIRECTORY_ROLES, useValue: [ /* value, label, hint */ ] }
```

Accepted role values on the API are the host `StaffRole` enum, not a list inside hr-core. `HrRoles` is still the host's partial class. A role string that is not in that enum is rejected with 400 and nothing is saved. Creating a person stays `StaffRoleFilter.OwnerOnly`.

It also passes in app-only pieces shown inside HR screens. In Rakkii these are the job
handover dialog and the KPI scorecards tab, and another app can supply stubs. The app
mounts `hrRoutes` from `hr.routes.ts` under its HR area. See Rakkii's
`apps/ops/src/app/hr-host.ts` for the full list.

## Names kept for compatibility

Some names stay as they were so Rakkii's ops app and stored data keep working:

- the `affected-jobs` route
- `jobs` and `jobId` in JSON
- the `job-files` bucket
- the "Sales incentive" payroll label
