using HrCore.Features.SettingsLog;

namespace HrCore.Features.Payroll;

public static class PayrollEndpoints
{
    public static IEndpointRouteBuilder MapPayrollApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/ops/payroll")
            .WithTags("Payroll")
            .AddEndpointFilter<StaffAuthorizationFilter>()
            .AddEndpointFilter(StaffRoleFilter.OwnerOrAdmin);

        api.MapGet("/", GetRegister)
            .WithName("PayrollRegister")
            .WithSummary("Payroll register")
            .WithDescription(
                "Query: from, to (YYYY-MM-DD, Asia/Manila). " +
                "Optional includeInactive (bool). " +
                OpsReportScope.QueryDoc +
                " Amount is gross (punches × stored rate). " +
                "Saved manual deductions for that exact period are included; a missing row is zeros. " +
                "Secured to Owner and Admin.");

        api.MapGet("/export", GetExport)
            .WithName("PayrollRegisterExport")
            .WithSummary("Payroll register CSV export")
            .WithDescription(
                "Same filters as /api/ops/payroll. Downloads CSV. Amount is gross; Net is gross minus manual deductions.");

        api.MapPut("/deductions", SaveDeductions)
            .WithName("PayrollDeductionsUpsert")
            .WithSummary("Save manual payroll deductions")
            .WithDescription(
                "Upserts SSS, PhilHealth, Pag-IBIG, withholding, and other amounts for one person and the payroll period. " +
                "Each amount must be zero or more. 404 when the person is outside the shop scope the register would show.");

        api.MapGet("/thirteenth-month", GetThirteenthMonth)
            .WithName("PayrollThirteenthMonth")
            .WithSummary("13th-month estimate")
            .WithDescription(
                "Query: year (YYYY, default current Asia/Manila year). " +
                OpsReportScope.QueryDoc +
                " Active staff only. Basic earned is the sum of Mode 1 and Mode 4 gross for each month in the employment window. " +
                "13th month is basic / 12. A stored override replaces basic earned. " +
                "Estimate only — not a BIR filing and not a payout.");

        api.MapGet("/thirteenth-month/export", ExportThirteenthMonth)
            .WithName("PayrollThirteenthMonthExport")
            .WithSummary("13th-month estimate CSV")
            .WithDescription("Same year and shop scope as the 13th-month list. Downloads CSV.");

        api.MapPut("/thirteenth-month/override", SaveThirteenthOverride)
            .WithName("PayrollThirteenthOverrideUpsert")
            .WithSummary("Save a 13th-month basic override")
            .WithDescription(
                "Body: staffId, year, basicEarnedOverride (zero or more). " +
                "Replaces computed basic earned for that person and year. 404 when the person is not an active staff member in scope.");

        api.MapDelete("/thirteenth-month/override", ClearThirteenthOverride)
            .WithName("PayrollThirteenthOverrideClear")
            .WithSummary("Clear a 13th-month basic override")
            .WithDescription(
                "Query: staffId, year. Removes the stored override so the estimate uses computed basic earned again.");

        api.MapGet("/bir-2316", GetBir2316)
            .WithName("PayrollBir2316")
            .WithSummary("BIR 2316 data sheet for one employee and year")
            .WithDescription(
                "Query: staffId, year (YYYY, default current Asia/Manila year). " +
                OpsReportScope.QueryDoc +
                " Sums the employee's lines from locked runs (void and draft excluded) whose period starts in the year: " +
                "gross, SSS, PhilHealth, Pag-IBIG (employee shares), and withholding. Lists the runs used, months with no locked run, " +
                "and the missing required values. 404 when the person is not on the shop scope.");

        api.MapGet("/runs", ListRuns)
            .WithName("PayrollRuns")
            .WithSummary("Locked payroll runs")
            .WithDescription(
                "Query: from, to (YYYY-MM-DD, optional; a run is listed when its period overlaps). " +
                OpsReportScope.QueryDoc +
                " Newest period first. Status is draft, locked, or void. Voided runs stay on the list.");

        api.MapGet("/runs/{id:guid}", GetRun)
            .WithName("PayrollRun")
            .WithSummary("One payroll run with its frozen lines")
            .WithDescription(
                "Locked run detail: totals plus one line per worker as locked. " +
                "404 when the run is outside the shop scope.");

        api.MapPost("/runs", LockRun)
            .WithName("PayrollRunLock")
            .WithSummary("Lock a payroll period for one shop")
            .WithDescription(
                "Body: branchId, from, to, note (optional, 500 chars). " +
                "Freezes the live register for that shop and period into run lines. " +
                "409 when the period already has a run or overlaps a locked one. " +
                "404 when the shop is outside the scope. Locked periods refuse edits; correct in the next period.");

        api.MapPost("/runs/{id:guid}/void", VoidRun)
            .AddEndpointFilter(StaffRoleFilter.OwnerOnly)
            .WithName("PayrollRunVoid")
            .WithSummary("Void a payroll run")
            .WithDescription(
                "Owner only. Sets the run to void, keeps every row and line, and reverses loan instalments " +
                "recorded on that run so the balance goes back up. Repeating the call changes nothing. " +
                "404 when the run is outside the shop scope.");

        api.MapGet("/rates/{staffId:guid}", GetRates)
            .WithName("PayrollRates")
            .WithSummary("Pay rate history for one worker")
            .WithDescription(
                "Current Staff snapshot (basis, rate, OT) plus the append-only dated history, oldest first. " +
                OpsReportScope.QueryDoc +
                " 404 when the person is not on the payroll register scope.");

        api.MapPut("/rates/{staffId:guid}", SetRate)
            .AddEndpointFilter(StaffRoleFilter.OwnerOnly)
            .WithName("PayrollRateSet")
            .WithSummary("Set a dated pay rate")
            .WithDescription(
                "Owner only. Body: effectiveFrom, basis, ratePhp (zero or more), otAfterHoursPerDay, otMultiplier, reason. " +
                "Appends one history row and refreshes the current Staff snapshot. Hourly defaults OT to 8 hours " +
                "a day at 1.00x; monthly clears both. 409 when effectiveFrom falls inside a locked run period. " +
                "404 when the person is not on the payroll register scope.");

        api.MapPost("/payslips/issue", IssuePayslip)
            .WithName("IssuePayslip")
            .WithSummary("Issue one worker's payslip")
            .WithDescription(
                "Body: staffId, from, to (YYYY-MM-DD, Asia/Manila), note (optional, 200 max). " +
                "Records the issuance on the append-only HR trail and returns the payslip values. " +
                "Locked periods return their frozen snapshot. 404 when the person is not in scope.");

        api.MapGet("/statutory-suggested", GetStatutorySuggested)
            .WithName("StatutorySuggested")
            .WithSummary("Suggested statutory amounts for one worker and month")
            .WithDescription(
                "Query: staffId, month (YYYY-MM). Optional from and to (YYYY-MM-DD) prorate the month's " +
                "suggestion by that period's share of the month's register gross; when both are set they win. " +
                "Suggested SSS, PhilHealth, Pag-IBIG, and BIR withholding from the statutory pack — " +
                "the owner verifies before saving. 404 when the person is not on the payroll register.");

        api.MapGet("/final-pay", GetFinalPay)
            .WithName("PayrollFinalPay")
            .WithSummary("Final pay estimate for a separated worker")
            .WithDescription(
                "Query: staffId. Wages since the last locked period, a pro-rated 13th month, and " +
                "cash-convertible service incentive leave. Estimate only — not a payout. " +
                "400 when no separation date is set. 404 when the person is not in scope.");

        api.MapGet("/settings", GetPremiumSettings)
            .WithName("PayrollPremiumSettings")
            .WithSummary("Holiday, rest-day, and night-differential settings")
            .WithDescription(
                "The header shop's premium percents, switches, and night window. " +
                "Percents include the base 100. Night differential is off until the owner turns it on.");

        api.MapPut("/settings", SavePremiumSettings)
            .AddEndpointFilter(StaffRoleFilter.OwnerOnly)
            .WithName("PayrollPremiumSettingsSave")
            .WithSummary("Save premium settings")
            .WithDescription(
                "Owner only. Percents are 0 through 1000. Enables and the night window (Asia/Manila) " +
                "are stored on the branch, same columns as shop desk settings.");

        api.MapGet("/holidays", ListHolidays)
            .WithName("PayrollHolidays")
            .WithSummary("Holiday calendar for a year")
            .WithDescription(
                "Query: year (YYYY). Holidays for the shop scope, including company-wide rows (no branch).");

        api.MapPost("/holidays", CreateHoliday)
            .AddEndpointFilter(StaffRoleFilter.OwnerOnly)
            .WithName("PayrollHolidayCreate")
            .WithSummary("Add a holiday")
            .WithDescription(
                "Owner only. Body: date, name (120), kind regular|special, branchId (optional, header shop). " +
                "409 when that shop already has the date.");

        api.MapPut("/holidays/{id:guid}", UpdateHoliday)
            .AddEndpointFilter(StaffRoleFilter.OwnerOnly)
            .WithName("PayrollHolidayUpdate")
            .WithSummary("Update a holiday")
            .WithDescription("Owner only. Same body as create. 404 outside the shop scope. 409 on a duplicate date.");

        api.MapDelete("/holidays/{id:guid}", DeleteHoliday)
            .AddEndpointFilter(StaffRoleFilter.OwnerOnly)
            .WithName("PayrollHolidayDelete")
            .WithSummary("Delete a holiday")
            .WithDescription("Owner only. 404 outside the shop scope.");

        api.MapPost("/holidays/copy-year", CopyHolidayYear)
            .AddEndpointFilter(StaffRoleFilter.OwnerOnly)
            .WithName("PayrollHolidayCopyYear")
            .WithSummary("Copy one year's holidays onto another year")
            .WithDescription(
                "Owner only. Body: fromYear, toYear. Copies month, day, name, and kind. " +
                "Skips dates already present and skips 29 February. Returns created and skipped.");

        api.MapPut("/overrides", SaveOverride)
            .WithName("PayrollOverrideSave")
            .WithSummary("Set or clear a draft amount override")
            .WithDescription(
                "Owner and admin, same as deductions. Body: staffId, from, to, key, amount, reason. " +
                "Amount null clears the row. Reason is required when amount is set. " +
                "409 when the period is locked. Writes payroll.override on the HR trail. " +
                "Register keys in use: \"premium\", \"allowances\", and \"loan:{loanId}\".");

        api.MapPut("/day-overrides", SaveDayPayOverride)
            .AddEndpointFilter(StaffRoleFilter.OwnerOrAdmin)
            .WithName("PayrollDayPayOverrideSave")
            .WithSummary("Set or clear a per-day pay override")
            .WithDescription(
                "Owner and admin. Body: staffId, workDate, mode (full|half|none|custom), customAmount, reason. " +
                "Mode null clears the row. Reason is required when mode is set. Custom amount is required for custom " +
                "and must be between 0 and that day's normal pay. 409 when the period is locked. " +
                "Writes payroll.day_override on the HR trail. Punches are not changed.");

        api.MapGet("/allowances", ListAllowances)
            .WithName("PayrollAllowances")
            .WithSummary("Allowances for the shop, or one worker")
            .WithDescription(
                "Query: staffId (optional). Owner and admin, same as deductions. " +
                "Returns that worker's rows when staffId is set, otherwise every worker in scope. " +
                "suggestedNames is the distinct free-text names already used in this shop. " +
                "404 when staffId is outside the register scope.");

        api.MapPost("/allowances", CreateAllowance)
            .WithName("PayrollAllowanceCreate")
            .WithSummary("Add an allowance")
            .WithDescription(
                "Owner and admin. Body: staffId, name (80), amount (zero or more), per period|day, " +
                "taxable (default true), effectiveFrom, effectiveTo, note (500), active (default true). " +
                "Effective to must be on or after effective from. Writes payroll.allowance on the HR trail.");

        api.MapPut("/allowances/{id:guid}", UpdateAllowance)
            .WithName("PayrollAllowanceUpdate")
            .WithSummary("Update an allowance")
            .WithDescription(
                "Owner and admin. Same fields as create, without staffId. 404 outside the shop scope.");

        api.MapDelete("/allowances/{id:guid}", DeleteAllowance)
            .WithName("PayrollAllowanceDelete")
            .WithSummary("Delete an allowance")
            .WithDescription(
                "Owner and admin. Hard delete. Locked run lines keep the items copied at lock time. " +
                "404 outside the shop scope.");

        api.MapGet("/loans", ListLoans)
            .WithName("PayrollLoans")
            .WithSummary("Loans and cash advances")
            .WithDescription(
                "Query: staffId (optional), includeClosed (default false). Owner and admin. " +
                "Each row has principal, paid, balance, and status active|paid|cancelled. " +
                "Closed means paid or cancelled. suggestedKinds lists kinds already used plus " +
                "Cash advance, SSS loan, Pag-IBIG loan, and Company loan. " +
                "404 when staffId is outside the register scope.");

        api.MapGet("/loans/{id:guid}", GetLoan)
            .WithName("PayrollLoan")
            .WithSummary("One loan with its payments")
            .WithDescription(
                "Owner and admin. Payments include reversed rows. 404 outside the shop scope.");

        api.MapPost("/loans", CreateLoan)
            .WithName("PayrollLoanCreate")
            .WithSummary("Add a loan or cash advance")
            .WithDescription(
                "Owner and admin. Body: staffId, kind (required, 80), principal (greater than zero), " +
                "perPeriodAmount (greater than zero), startPeriod, note (500). " +
                "Instalments start in the first period whose end is on or after startPeriod. " +
                "Writes payroll.loan on the HR trail.");

        api.MapPut("/loans/{id:guid}", UpdateLoan)
            .WithName("PayrollLoanUpdate")
            .WithSummary("Update a loan")
            .WithDescription(
                "Owner and admin. Body: kind, perPeriodAmount, startPeriod, note, principal (optional). " +
                "Principal changes only while the loan has no unreversed payment; otherwise 409. " +
                "404 outside the shop scope.");

        api.MapPost("/loans/{id:guid}/cancel", CancelLoan)
            .WithName("PayrollLoanCancel")
            .WithSummary("Cancel a loan")
            .WithDescription(
                "Owner and admin. Body: reason (required, 500). Stops future instalments. " +
                "Payments already taken stay until the run is voided. Writes payroll.loan.");

        api.MapDelete("/loans/{id:guid}", DeleteLoan)
            .WithName("PayrollLoanDelete")
            .WithSummary("Delete a loan that has never been paid")
            .WithDescription(
                "Owner and admin. 409 when any payment exists, including a reversed one. " +
                "404 outside the shop scope.");

        // Worker self-service: any staff role, own row only. The main group is owner/admin.
        var self = app.MapGroup("/api/ops/payroll/me")
            .WithTags("Payroll")
            .AddEndpointFilter<StaffAuthorizationFilter>();
        self.MapGet("/", GetMyPayslip)
            .WithName("MyPayslip")
            .WithSummary("My payslip for a period")
            .WithDescription(
                "Query: from, to (YYYY-MM-DD, Asia/Manila; default the current week). " +
                "The caller's own row only. Locked periods return their frozen snapshot.");
        self.MapGet("/payslips", ListMyPayslips)
            .WithName("MyPayslips")
            .WithSummary("My payslip history")
            .WithDescription(
                "The caller's own lines from locked payroll runs only (draft and void excluded), " +
                "newest first by period end. Each row: runId, lineId, periodFrom, periodTo, " +
                "gross, deductions, net, lockedAtUtc. Owner logins with no home shop get an empty list.");
        self.MapGet("/payslips/{lineId:guid}", GetMyPayslipLine)
            .WithName("MyPayslipLine")
            .WithSummary("My saved payslip")
            .WithDescription(
                "One saved payslip line exactly as stored at lock time, with its run period and its " +
                "frozen line items. Not recalculated. 403 when the line belongs to another staff " +
                "member; 404 when the line is missing or its run is not locked.");

        return app;
    }

    private static Task<IResult> GetThirteenthMonth(
        int? year,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken)
    {
        if (!PayrollService.TryResolveThirteenthYear(year, out var resolved, out var error))
        {
            return Task.FromResult(Results.BadRequest(new { error }));
        }

        return OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var result = await payroll.GetThirteenthMonthAsync(
                    shops, resolved, IncludeUnscopedOwners(http), ct);
                return Results.Ok(result);
            },
            cancellationToken);
    }

    private static Task<IResult> ExportThirteenthMonth(
        int? year,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken)
    {
        if (!PayrollService.TryResolveThirteenthYear(year, out var resolved, out var error))
        {
            return Task.FromResult(Results.BadRequest(new { error }));
        }

        return OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var file = await payroll.BuildThirteenthCsvAsync(
                    shops, resolved, IncludeUnscopedOwners(http), ct);
                return Results.File(file.Bytes, "text/csv; charset=utf-8", file.FileName);
            },
            cancellationToken);
    }

    private static async Task<IResult> SaveThirteenthOverride(
        SaveThirteenthOverrideRequest body,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken)
    {
        if (!PayrollService.TryResolveThirteenthYear(body.Year, out var year, out var yearError))
        {
            return Results.BadRequest(new { error = yearError });
        }

        if (!PayrollService.TryNormalizeOverride(body.BasicEarnedOverride, out var amount, out var amountError))
        {
            return Results.BadRequest(new { error = amountError });
        }

        return await OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var saved = await payroll.SaveThirteenthOverrideAsync(
                    shops, body.StaffId, year, amount, http.GetStaff()?.Id, IncludeUnscopedOwners(http), ct);
                return saved is null
                    ? Results.NotFound(new { error = "That person is not on this 13th-month list." })
                    : Results.Ok(saved);
            },
            cancellationToken);
    }

    private static Task<IResult> ClearThirteenthOverride(
        Guid staffId,
        int year,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken)
    {
        if (!PayrollService.TryResolveThirteenthYear(year, out var resolved, out var error))
        {
            return Task.FromResult(Results.BadRequest(new { error }));
        }

        return OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var cleared = await payroll.ClearThirteenthOverrideAsync(
                    shops, staffId, resolved, IncludeUnscopedOwners(http), ct);
                return cleared is null
                    ? Results.NotFound(new { error = "That person is not on this 13th-month list." })
                    : Results.Ok(cleared);
            },
            cancellationToken);
    }

    private static Task<IResult> GetBir2316(
        Guid staffId,
        int? year,
        HttpContext http,
        BranchResolver branches,
        Bir2316Service bir,
        CancellationToken cancellationToken)
    {
        if (!PayrollService.TryResolveThirteenthYear(year, out var resolved, out var error))
        {
            return Task.FromResult(Results.BadRequest(new { error }));
        }

        return OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var sheet = await bir.GetAsync(shops, staffId, resolved, ct);
                return sheet is null
                    ? Results.NotFound(new { error = "That person is not on this shop scope." })
                    : Results.Ok(sheet);
            },
            cancellationToken);
    }

    private static Task<IResult> GetRegister(
        DateOnly? from,
        DateOnly? to,
        bool? includeInactive,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var result = await payroll.GetRegisterAsync(
                    shops, from, to, includeInactive ?? false, IncludeUnscopedOwners(http), ct);
                return Results.Ok(result);
            },
            cancellationToken);

    private static Task<IResult> ListRuns(
        DateOnly? from,
        DateOnly? to,
        HttpContext http,
        BranchResolver branches,
        PayrollRunService runs,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) => Results.Ok(await runs.ListAsync(shops, from, to, ct)),
            cancellationToken);

    private static Task<IResult> GetRun(
        Guid id,
        HttpContext http,
        BranchResolver branches,
        PayrollRunService runs,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var run = await runs.GetRunAsync(shops, id, ct);
                return run is null
                    ? Results.NotFound(new { error = "That payroll run is not on this shop scope." })
                    : Results.Ok(run);
            },
            cancellationToken);

    private static async Task<IResult> LockRun(
        LockPayrollRunRequest body,
        HttpContext http,
        BranchResolver branches,
        PayrollRunService runs,
        CancellationToken cancellationToken)
    {
        if (body.BranchId == Guid.Empty)
        {
            return Results.BadRequest(new { error = "Pick a shop." });
        }

        return await OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var (run, error) = await runs.LockAsync(
                    shops,
                    body.BranchId,
                    body.From,
                    body.To,
                    body.Note,
                    http.GetStaff()?.Id,
                    IncludeUnscopedOwners(http),
                    ct);
                if (error is not null)
                {
                    return Results.Json(new { error }, statusCode: StatusCodes.Status409Conflict);
                }

                return run is null
                    ? Results.NotFound(new { error = "That shop is not on your payroll scope." })
                    : Results.Ok(run);
            },
            cancellationToken);
    }

    private static Task<IResult> VoidRun(
        Guid id,
        HttpContext http,
        BranchResolver branches,
        PayrollRunService runs,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var run = await runs.VoidAsync(shops, id, http.GetStaff()?.Id, ct);
                return run is null
                    ? Results.NotFound(new { error = "That payroll run is not on this shop scope." })
                    : Results.Ok(run);
            },
            cancellationToken);

    private static Task<IResult> GetRates(
        Guid staffId,
        HttpContext http,
        BranchResolver branches,
        PayrollRateService rates,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var result = await rates.GetRatesAsync(shops, staffId, IncludeUnscopedOwners(http), ct);
                return result is null
                    ? Results.NotFound(new { error = "That person is not on this payroll register." })
                    : Results.Ok(result);
            },
            cancellationToken);

    private static Task<IResult> SetRate(
        Guid staffId,
        SetPayrollRateRequest body,
        HttpContext http,
        BranchResolver branches,
        PayrollRateService rates,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var (result, error) = await rates.SetRateAsync(
                    shops, staffId, body, http.GetStaff()?.Id, IncludeUnscopedOwners(http), ct);
                if (error is not null)
                {
                    return Results.Json(new { error }, statusCode: StatusCodes.Status409Conflict);
                }

                return result is null
                    ? Results.NotFound(new { error = "That person is not on this payroll register." })
                    : Results.Ok(result);
            },
            cancellationToken);

    private static Task<IResult> GetExport(
        DateOnly? from,
        DateOnly? to,
        bool? includeInactive,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var file = await payroll.BuildRegisterCsvAsync(
                    shops, from, to, includeInactive ?? false, IncludeUnscopedOwners(http), ct);
                return Results.File(file.Bytes, "text/csv; charset=utf-8", file.FileName);
            },
            cancellationToken);

    private static async Task<IResult> SaveDeductions(
        SavePayrollDeductionsRequest body,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken)
    {
        if (body.Sss < 0 || body.PhilHealth < 0 || body.PagIbig < 0 || body.Wht < 0 || body.Other < 0)
        {
            return Results.BadRequest(new { error = "Deduction amounts cannot be negative." });
        }

        return await OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var result = await payroll.SaveDeductionsAsync(
                    shops, body, IncludeUnscopedOwners(http), http.GetStaff()?.Id, ct);
                if (result.Error is not null)
                {
                    return Results.Json(
                        new { error = result.Error },
                        statusCode: StatusCodes.Status409Conflict);
                }

                return result.Saved is null
                    ? Results.NotFound(new { error = "That person is not on this payroll register." })
                    : Results.Ok(result.Saved);
            },
            cancellationToken);
    }

    private static Task<IResult> IssuePayslip(
        IssuePayslipRequest body,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var (payslip, error) = await payroll.IssuePayslipAsync(
                    shops, body, http.GetStaff()?.Id, IncludeUnscopedOwners(http), ct);
                if (error is not null)
                {
                    return Results.Json(
                        new { error }, statusCode: StatusCodes.Status400BadRequest);
                }

                return payslip is null
                    ? Results.NotFound(new { error = "That person is not on this payroll register." })
                    : Results.Ok(payslip);
            },
            cancellationToken);

    private static Task<IResult> GetStatutorySuggested(
        Guid staffId,
        string? month,
        DateOnly? from,
        DateOnly? to,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                int year;
                int monthNumber;
                if (from is not null || to is not null)
                {
                    if (from is null || to is null)
                    {
                        return Results.BadRequest(new { error = "Period from and to are both required." });
                    }

                    if (from > to)
                    {
                        return Results.BadRequest(new { error = "The period end must not be before the start." });
                    }

                    year = from.Value.Year;
                    monthNumber = from.Value.Month;
                }
                else if (!TryParseMonth(month, out year, out monthNumber))
                {
                    return Results.BadRequest(new { error = "Month must be YYYY-MM." });
                }

                var suggestion = await payroll.GetStatutorySuggestionAsync(
                    shops, staffId, year, monthNumber, from, to, IncludeUnscopedOwners(http), ct);
                return suggestion is null
                    ? Results.NotFound(new { error = "That person is not on this payroll register." })
                    : Results.Ok(suggestion);
            },
            cancellationToken);

    private static Task<IResult> GetFinalPay(
        Guid staffId,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var (result, error) = await payroll.GetFinalPayAsync(
                    shops, staffId, IncludeUnscopedOwners(http), ct);
                if (error is not null)
                {
                    return Results.BadRequest(new { error });
                }

                return result is null
                    ? Results.NotFound(new { error = "That person is not on this payroll register." })
                    : Results.Ok(result);
            },
            cancellationToken);

    private static async Task<IResult> GetMyPayslip(
        DateOnly? from,
        DateOnly? to,
        HttpContext http,
        PayrollService payroll,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        if (actor.BranchId is null)
        {
            return Results.BadRequest(
                new { error = "Owner logins have no home shop, so there is no own payslip." });
        }

        var payslip = await payroll.GetMyPayslipAsync(actor, from, to, cancellationToken);
        return payslip is null
            ? Results.NotFound(new { error = "No payslip for this period." })
            : Results.Ok(payslip);
    }

    private static async Task<IResult> ListMyPayslips(
        HttpContext http,
        PayrollRunService runs,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await runs.ListMyPayslipsAsync(actor.Id, cancellationToken));
    }

    private static async Task<IResult> GetMyPayslipLine(
        Guid lineId,
        HttpContext http,
        PayrollRunService runs,
        CancellationToken cancellationToken)
    {
        var actor = http.GetStaff();
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var result = await runs.GetMyPayslipAsync(actor.Id, lineId, cancellationToken);
        if (result.Status == StatusCodes.Status403Forbidden)
        {
            return Results.Json(
                new { error = "That payslip belongs to another staff member." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return result.Saved is null
            ? Results.NotFound(new { error = "No saved payslip found." })
            : Results.Ok(result.Saved);
    }

    private static bool TryParseMonth(string? month, out int year, out int monthNumber)
    {
        year = 0;
        monthNumber = 0;
        if (string.IsNullOrWhiteSpace(month))
        {
            return false;
        }

        var parts = month.Split('-');
        if (parts.Length != 2
            || !int.TryParse(parts[0], out year)
            || !int.TryParse(parts[1], out monthNumber))
        {
            return false;
        }

        return year is >= 2000 and <= 2100 && monthNumber is >= 1 and <= 12;
    }

    private static Task<IResult> GetPremiumSettings(
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, currentId, ct) =>
            {
                var settings = await payroll.GetPremiumSettingsAsync(shops, currentId, ct);
                return settings is null
                    ? Results.NotFound(new { error = "That shop is not on your payroll scope." })
                    : Results.Ok(settings);
            },
            cancellationToken);

    private static Task<IResult> SavePremiumSettings(
        UpdatePayrollPremiumSettingsRequest body,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, currentId, ct) =>
            {
                var (saved, error) = await payroll.SavePremiumSettingsAsync(shops, currentId, body, http.ActorStaffId(), ct);
                if (error is not null)
                {
                    return Results.BadRequest(new { error });
                }

                return saved is null
                    ? Results.NotFound(new { error = "That shop is not on your payroll scope." })
                    : Results.Ok(saved);
            },
            cancellationToken);

    private static Task<IResult> ListHolidays(
        int? year,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken)
    {
        var resolved = year ?? ManilaTime.Today().Year;
        if (resolved is < 2000 or > 2100)
        {
            return Task.FromResult<IResult>(Results.BadRequest(new { error = "Year must be between 2000 and 2100." }));
        }

        return OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) => Results.Ok(await payroll.ListHolidaysAsync(shops, resolved, ct)),
            cancellationToken);
    }

    private static Task<IResult> CreateHoliday(
        UpsertShopHolidayRequest body,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, currentId, ct) =>
            {
                var (saved, error, status) = await payroll.CreateHolidayAsync(shops, currentId, body, ct);
                if (saved is not null)
                {
                    return Results.Ok(saved);
                }

                return error is null
                    ? Results.NotFound()
                    : Results.Json(new { error }, statusCode: status);
            },
            cancellationToken);

    private static Task<IResult> UpdateHoliday(
        Guid id,
        UpsertShopHolidayRequest body,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var (saved, error, status) = await payroll.UpdateHolidayAsync(shops, id, body, ct);
                if (saved is not null)
                {
                    return Results.Ok(saved);
                }

                return error is null
                    ? Results.NotFound(new { error = "That holiday is not on this shop scope." })
                    : Results.Json(new { error }, statusCode: status);
            },
            cancellationToken);

    private static Task<IResult> DeleteHoliday(
        Guid id,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var deleted = await payroll.DeleteHolidayAsync(shops, id, ct);
                return deleted
                    ? Results.NoContent()
                    : Results.NotFound(new { error = "That holiday is not on this shop scope." });
            },
            cancellationToken);

    private static Task<IResult> CopyHolidayYear(
        CopyHolidayYearRequest body,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var (result, error) = await payroll.CopyHolidayYearAsync(shops, body.FromYear, body.ToYear, ct);
                return error is not null
                    ? Results.BadRequest(new { error })
                    : Results.Ok(result);
            },
            cancellationToken);

    private static Task<IResult> SaveOverride(
        SavePayrollOverrideRequest body,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var result = await payroll.SaveOverrideAsync(
                    shops, body, IncludeUnscopedOwners(http), http.GetStaff()?.Id, ct);
                if (result.Error is not null)
                {
                    return Results.Json(new { error = result.Error }, statusCode: result.ErrorStatus);
                }

                if (result.Saved is null && result.Cleared is null)
                {
                    return Results.NotFound(new { error = "That person is not on this payroll register." });
                }

                return Results.Ok((object?)result.Saved ?? result.Cleared);
            },
            cancellationToken);

    private static Task<IResult> SaveDayPayOverride(
        SavePayrollDayPayOverrideRequest body,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var result = await payroll.SaveDayPayOverrideAsync(
                    shops,
                    body,
                    IncludeUnscopedOwners(http),
                    http.GetStaff()?.Id,
                    http.GetStaff()?.Role,
                    ct);
                if (result.Error is not null)
                {
                    return Results.Json(new { error = result.Error }, statusCode: result.ErrorStatus);
                }

                if (result.Saved is null && result.Cleared is null)
                {
                    return Results.NotFound(new { error = "That person is not on this payroll register." });
                }

                return Results.Ok((object?)result.Saved ?? result.Cleared);
            },
            cancellationToken);

    private static Task<IResult> ListAllowances(
        Guid? staffId,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var result = await payroll.ListAllowancesAsync(
                    shops, staffId, IncludeUnscopedOwners(http), ct);
                return result is null
                    ? Results.NotFound(new { error = "That person is not on this payroll register." })
                    : Results.Ok(result);
            },
            cancellationToken);

    private static Task<IResult> CreateAllowance(
        SaveStaffAllowanceRequest body,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var result = await payroll.CreateAllowanceAsync(
                    shops, body, IncludeUnscopedOwners(http), http.GetStaff()?.Id, ct);
                if (result.Error is not null)
                {
                    return Results.Json(new { error = result.Error }, statusCode: result.Status);
                }

                return result.Saved is null
                    ? Results.NotFound(new { error = "That person is not on this payroll register." })
                    : Results.Ok(result.Saved);
            },
            cancellationToken);

    private static Task<IResult> UpdateAllowance(
        Guid id,
        UpdateStaffAllowanceRequest body,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var result = await payroll.UpdateAllowanceAsync(
                    shops, id, body, IncludeUnscopedOwners(http), http.GetStaff()?.Id, ct);
                if (result.Error is not null)
                {
                    return Results.Json(new { error = result.Error }, statusCode: result.Status);
                }

                return result.Saved is null
                    ? Results.NotFound(new { error = "Allowance not found." })
                    : Results.Ok(result.Saved);
            },
            cancellationToken);

    private static Task<IResult> DeleteAllowance(
        Guid id,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var deleted = await payroll.DeleteAllowanceAsync(
                    shops, id, IncludeUnscopedOwners(http), http.GetStaff()?.Id, ct);
                return deleted
                    ? Results.NoContent()
                    : Results.NotFound(new { error = "Allowance not found." });
            },
            cancellationToken);

    private static Task<IResult> ListLoans(
        Guid? staffId,
        bool? includeClosed,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var result = await payroll.ListLoansAsync(
                    shops, staffId, includeClosed ?? false, IncludeUnscopedOwners(http), ct);
                return result is null
                    ? Results.NotFound(new { error = "That person is not on this payroll register." })
                    : Results.Ok(result);
            },
            cancellationToken);

    private static Task<IResult> GetLoan(
        Guid id,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var result = await payroll.GetLoanAsync(shops, id, IncludeUnscopedOwners(http), ct);
                return result is null
                    ? Results.NotFound(new { error = "Loan not found." })
                    : Results.Ok(result);
            },
            cancellationToken);

    private static Task<IResult> CreateLoan(
        SaveStaffLoanRequest body,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var result = await payroll.CreateLoanAsync(
                    shops, body, IncludeUnscopedOwners(http), http.GetStaff()?.Id, ct);
                if (result.Error is not null)
                {
                    return Results.Json(new { error = result.Error }, statusCode: result.Status);
                }

                return result.Saved is null
                    ? Results.NotFound(new { error = "That person is not on this payroll register." })
                    : Results.Ok(result.Saved);
            },
            cancellationToken);

    private static Task<IResult> UpdateLoan(
        Guid id,
        UpdateStaffLoanRequest body,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var result = await payroll.UpdateLoanAsync(
                    shops, id, body, IncludeUnscopedOwners(http), http.GetStaff()?.Id, ct);
                if (result.Error is not null)
                {
                    return Results.Json(new { error = result.Error }, statusCode: result.Status);
                }

                return result.Saved is null
                    ? Results.NotFound(new { error = "Loan not found." })
                    : Results.Ok(result.Saved);
            },
            cancellationToken);

    private static Task<IResult> CancelLoan(
        Guid id,
        CancelStaffLoanRequest body,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var result = await payroll.CancelLoanAsync(
                    shops, id, body, IncludeUnscopedOwners(http), http.GetStaff()?.Id, ct);
                if (result.Error is not null)
                {
                    return Results.Json(new { error = result.Error }, statusCode: result.Status);
                }

                return result.Saved is null
                    ? Results.NotFound(new { error = "Loan not found." })
                    : Results.Ok(result.Saved);
            },
            cancellationToken);

    private static Task<IResult> DeleteLoan(
        Guid id,
        HttpContext http,
        BranchResolver branches,
        PayrollService payroll,
        CancellationToken cancellationToken) =>
        OpsReportScope.MapAsync(
            http,
            branches,
            async (shops, _, ct) =>
            {
                var result = await payroll.DeleteLoanAsync(
                    shops, id, IncludeUnscopedOwners(http), http.GetStaff()?.Id, ct);
                if (result.Error is not null)
                {
                    return Results.Json(new { error = result.Error }, statusCode: result.Status);
                }

                return result.Deleted
                    ? Results.NoContent()
                    : Results.NotFound(new { error = "Loan not found." });
            },
            cancellationToken);

    private static bool IncludeUnscopedOwners(HttpContext http) =>
        http.GetStaff().IsHqOwner()
        && OpsReportScope.Parse(http.Request.Query) == OpsReportScopeKind.All;
}
