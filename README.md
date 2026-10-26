# hr-core

Shared HR for Rakkii and Qurio: staff, the employee directory, employment documents,
attendance and clock, leave, payroll (earnings, statutory, runs, loans),
announcements, and the HR and settings change logs.

| Folder | What it is | Where an app puts it |
| --- | --- | --- |
| `api/` | C# (.NET 10) source under `HrCore.*` namespaces | `packages/hr-core/api`, compiled into the API as linked files |
| `ops/` | Angular screens | `apps/ops/src/hr-core`, inside the Angular app |

There is no package feed. Each app keeps a pinned copy of this repo's folders and
updates it with its sync script, so the version an app runs is always one commit
here. Make HR changes in this repo first, then bump the pin in each app.

## What the app supplies (API)

The package uses partial classes and partial methods, so the build fails if the app
leaves any of these out.

| The app provides | Rakkii example |
| --- | --- |
| `StaffRole` enum, which must include `Owner` and `Admin` | `apps/api/Shared/Domain/StaffRole.cs` |
| `HrRoles` answers: who is desk staff, who clocks in by default, who sees only themselves in the staff list, and the role for unknown people | `apps/api/Features/HrAdapters/ShopHrAppContract.cs` |
| `StaffAccessFlags`: app-specific staff permissions, read from and written to the staff JSON | same file |
| A DbContext that implements `IHrDb` and calls `HrModel.Configure(modelBuilder)` | `apps/api/Shared/Data/AppDbContext.cs` |
| `IStaffInvite` (how new staff get a sign-in) | `apps/api/Shared/Infrastructure/SupabaseStaffInvite.cs` |
| Optionally `IStaffWorkHooks`, for open work a person holds (Rakkii: jobs) | `apps/api/Features/Jobs/StaffWork/JobStaffWork.cs` |
| Its own fields on `Organization`, `Branch` and `Staff`, as partial class parts | `apps/api/Shared/Domain/*.Shop.cs` |

Then in `Program.cs`, call `services.AddHrCore<AppDbContext>()` and `app.MapHrCore()`.

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
- `canPickHomeShop(me)`: whether the signed-in owner may choose a person's home shop
- `clocksByDefault(role)`: whether a new person of that role starts with Must clock on. Use the same rule as the API's `HrRoles.ClocksByDefault`

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
