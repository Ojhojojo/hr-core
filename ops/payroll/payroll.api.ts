import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@hr-host';
import {
  IssuePayslip,
  LockPayrollRun,
  MyPayslipDetail,
  MyPayslipSummary,
  Payslip,
  PayrollDeductionSaved,
  PayrollQuery,
  PayrollRatesResponse,
  PayrollRegisterResponse,
  PayrollRunSummary,
  SavePayrollDeductions,
  SaveThirteenthOverride,
  SetPayrollRate,
  StatutorySuggestion,
  PayrollFinalPay,
  ThirteenthMonthResponse,
  ThirteenthMonthRow,
  PayrollPremiumSettings,
  UpdatePayrollPremiumSettings,
  ShopHoliday,
  ShopHolidayList,
  UpsertShopHoliday,
  CopyHolidayYear,
  CopyHolidayYearResult,
  PayrollOverride,
  PayrollOverrideCleared,
  SavePayrollOverride,
  PayrollDayPayOverride,
  SavePayrollDayPayOverride,
  StaffAllowance,
  StaffAllowanceList,
  SaveStaffAllowance,
  UpdateStaffAllowance,
  StaffLoan,
  StaffLoanDetail,
  StaffLoanList,
  SaveStaffLoan,
  UpdateStaffLoan,
} from './payroll.models';

/** A punch with no clock-out, listed before a period is locked. */
export interface OpenPunchRow {
  punchId: string;
  staffId: string;
  staffName: string;
  inAtUtc: string;
  inAtManila: string;
}

/** Missing-value keys the BIR 2316 endpoint returns in `missing`. */
export type Bir2316MissingKey =
  | 'employee_tin'
  | 'employee_address'
  | 'employee_zip_code'
  | 'employee_birth_date'
  | 'employer_tin'
  | 'employer_address';

export interface Bir2316Sheet {
  year: number;
  staffId: string;
  employee: {
    name: string;
    tin: string | null;
    registeredAddress: string | null;
    zipCode: string | null;
    birthDate: string | null;
  };
  employer: {
    legalName: string;
    tin: string | null;
    address: string | null;
    shopName: string;
  };
  amounts: {
    gross: number;
    sss: number;
    philHealth: number;
    pagIbig: number;
    wht: number;
    lineCount: number;
  };
  runs: {
    runId: string;
    periodFrom: string;
    periodTo: string;
    lockedAtUtc: string | null;
    shopName: string;
  }[];
  /** Calendar months (1 to 12) the person was employed in with no locked run. */
  missingMonths: number[];
  missing: Bir2316MissingKey[];
}

@Injectable({ providedIn: 'root' })
export class PayrollApi {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  /** Open punches whose clock-in falls in the period (owner or admin). */
  openPunches(from: string, to: string): Observable<OpenPunchRow[]> {
    return this.http.get<OpenPunchRow[]>(`${this.base}/api/ops/attendance/open-punches`, {
      params: toParams({ from, to }),
    });
  }

  getPayroll(query: PayrollQuery = {}): Observable<PayrollRegisterResponse> {
    return this.http.get<PayrollRegisterResponse>(`${this.base}/api/ops/payroll`, {
      params: toParams(query),
    });
  }

  saveDeductions(
    body: SavePayrollDeductions,
    query: PayrollQuery = {},
  ): Observable<PayrollDeductionSaved> {
    return this.http.put<PayrollDeductionSaved>(`${this.base}/api/ops/payroll/deductions`, body, {
      params: toParams(query),
    });
  }

  exportCsv(query: PayrollQuery = {}): Observable<HttpResponse<Blob>> {
    return this.http.get(`${this.base}/api/ops/payroll/export`, {
      params: toParams(query),
      observe: 'response',
      responseType: 'blob',
    });
  }

  lockRun(body: LockPayrollRun): Observable<PayrollRunSummary> {
    return this.http.post<PayrollRunSummary>(`${this.base}/api/ops/payroll/runs`, body);
  }

  voidRun(runId: string): Observable<PayrollRunSummary> {
    return this.http.post<PayrollRunSummary>(
      `${this.base}/api/ops/payroll/runs/${runId}/void`,
      null,
    );
  }

  getRates(staffId: string): Observable<PayrollRatesResponse> {
    return this.http.get<PayrollRatesResponse>(`${this.base}/api/ops/payroll/rates/${staffId}`);
  }

  setRate(staffId: string, body: SetPayrollRate): Observable<PayrollRatesResponse> {
    return this.http.put<PayrollRatesResponse>(
      `${this.base}/api/ops/payroll/rates/${staffId}`,
      body,
    );
  }

  /** BIR 2316 data sheet for one employee and calendar year. Owner and admin. */
  getBir2316(staffId: string, year: number): Observable<Bir2316Sheet> {
    const params = new HttpParams().set('staffId', staffId).set('year', String(year));
    return this.http.get<Bir2316Sheet>(`${this.base}/api/ops/payroll/bir-2316`, { params });
  }

  getThirteenth(year: number): Observable<ThirteenthMonthResponse> {
    return this.http.get<ThirteenthMonthResponse>(`${this.base}/api/ops/payroll/thirteenth-month`, {
      params: new HttpParams().set('year', String(year)),
    });
  }

  issuePayslip(body: IssuePayslip): Observable<Payslip> {
    return this.http.post<Payslip>(`${this.base}/api/ops/payroll/payslips/issue`, body);
  }

  getStatutorySuggested(
    staffId: string,
    month: string,
    period?: { from?: string; to?: string },
  ): Observable<StatutorySuggestion> {
    let params = new HttpParams().set('staffId', staffId).set('month', month);
    if (period?.from) {
      params = params.set('from', period.from);
    }
    if (period?.to) {
      params = params.set('to', period.to);
    }
    return this.http.get<StatutorySuggestion>(`${this.base}/api/ops/payroll/statutory-suggested`, {
      params,
    });
  }

  getFinalPay(staffId: string): Observable<PayrollFinalPay> {
    const params = new HttpParams().set('staffId', staffId);
    return this.http.get<PayrollFinalPay>(`${this.base}/api/ops/payroll/final-pay`, { params });
  }

  getMyPayslip(query: { from?: string; to?: string } = {}): Observable<Payslip> {
    let params = new HttpParams();
    if (query.from) {
      params = params.set('from', query.from);
    }
    if (query.to) {
      params = params.set('to', query.to);
    }
    return this.http.get<Payslip>(`${this.base}/api/ops/payroll/me`, { params });
  }

  listMyPayslips(): Observable<MyPayslipSummary[]> {
    return this.http.get<MyPayslipSummary[]>(`${this.base}/api/ops/payroll/me/payslips`);
  }

  getMyPayslipLine(lineId: string): Observable<MyPayslipDetail> {
    return this.http.get<MyPayslipDetail>(`${this.base}/api/ops/payroll/me/payslips/${lineId}`);
  }

  saveThirteenthOverride(body: SaveThirteenthOverride): Observable<ThirteenthMonthRow> {
    return this.http.put<ThirteenthMonthRow>(
      `${this.base}/api/ops/payroll/thirteenth-month/override`,
      body,
    );
  }

  clearThirteenthOverride(staffId: string, year: number): Observable<ThirteenthMonthRow> {
    const params = new HttpParams().set('staffId', staffId).set('year', String(year));
    return this.http.delete<ThirteenthMonthRow>(
      `${this.base}/api/ops/payroll/thirteenth-month/override`,
      {
        params,
      },
    );
  }

  exportThirteenth(year: number): Observable<HttpResponse<Blob>> {
    return this.http.get(`${this.base}/api/ops/payroll/thirteenth-month/export`, {
      params: new HttpParams().set('year', String(year)),
      observe: 'response',
      responseType: 'blob',
    });
  }

  getSettings(): Observable<PayrollPremiumSettings> {
    return this.http.get<PayrollPremiumSettings>(`${this.base}/api/ops/payroll/settings`);
  }

  saveSettings(body: UpdatePayrollPremiumSettings): Observable<PayrollPremiumSettings> {
    return this.http.put<PayrollPremiumSettings>(`${this.base}/api/ops/payroll/settings`, body);
  }

  listHolidays(year: number): Observable<ShopHolidayList> {
    return this.http.get<ShopHolidayList>(`${this.base}/api/ops/payroll/holidays`, {
      params: new HttpParams().set('year', String(year)),
    });
  }

  createHoliday(body: UpsertShopHoliday): Observable<ShopHoliday> {
    return this.http.post<ShopHoliday>(`${this.base}/api/ops/payroll/holidays`, body);
  }

  updateHoliday(id: string, body: UpsertShopHoliday): Observable<ShopHoliday> {
    return this.http.put<ShopHoliday>(`${this.base}/api/ops/payroll/holidays/${id}`, body);
  }

  deleteHoliday(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/api/ops/payroll/holidays/${id}`);
  }

  copyHolidayYear(body: CopyHolidayYear): Observable<CopyHolidayYearResult> {
    return this.http.post<CopyHolidayYearResult>(
      `${this.base}/api/ops/payroll/holidays/copy-year`,
      body,
    );
  }

  saveOverride(body: SavePayrollOverride): Observable<PayrollOverride | PayrollOverrideCleared> {
    return this.http.put<PayrollOverride | PayrollOverrideCleared>(
      `${this.base}/api/ops/payroll/overrides`,
      body,
    );
  }

  saveDayPayOverride(
    body: SavePayrollDayPayOverride,
  ): Observable<PayrollDayPayOverride | { staffId: string; workDate: string; cleared: boolean }> {
    return this.http.put<
      PayrollDayPayOverride | { staffId: string; workDate: string; cleared: boolean }
    >(`${this.base}/api/ops/payroll/day-overrides`, body);
  }

  listAllowances(staffId?: string): Observable<StaffAllowanceList> {
    let params = new HttpParams();
    if (staffId) {
      params = params.set('staffId', staffId);
    }
    return this.http.get<StaffAllowanceList>(`${this.base}/api/ops/payroll/allowances`, { params });
  }

  createAllowance(body: SaveStaffAllowance): Observable<StaffAllowance> {
    return this.http.post<StaffAllowance>(`${this.base}/api/ops/payroll/allowances`, body);
  }

  updateAllowance(id: string, body: UpdateStaffAllowance): Observable<StaffAllowance> {
    return this.http.put<StaffAllowance>(`${this.base}/api/ops/payroll/allowances/${id}`, body);
  }

  deleteAllowance(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/api/ops/payroll/allowances/${id}`);
  }

  listLoans(staffId?: string, includeClosed?: boolean): Observable<StaffLoanList> {
    let params = new HttpParams();
    if (staffId) {
      params = params.set('staffId', staffId);
    }
    if (includeClosed) {
      params = params.set('includeClosed', 'true');
    }
    return this.http.get<StaffLoanList>(`${this.base}/api/ops/payroll/loans`, { params });
  }

  getLoan(id: string): Observable<StaffLoanDetail> {
    return this.http.get<StaffLoanDetail>(`${this.base}/api/ops/payroll/loans/${id}`);
  }

  createLoan(body: SaveStaffLoan): Observable<StaffLoan> {
    return this.http.post<StaffLoan>(`${this.base}/api/ops/payroll/loans`, body);
  }

  updateLoan(id: string, body: UpdateStaffLoan): Observable<StaffLoan> {
    return this.http.put<StaffLoan>(`${this.base}/api/ops/payroll/loans/${id}`, body);
  }

  cancelLoan(id: string, reason: string): Observable<StaffLoan> {
    return this.http.post<StaffLoan>(`${this.base}/api/ops/payroll/loans/${id}/cancel`, { reason });
  }

  deleteLoan(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/api/ops/payroll/loans/${id}`);
  }
}

function toParams(query: PayrollQuery): HttpParams {
  let params = new HttpParams();
  if (query.from) {
    params = params.set('from', query.from);
  }
  if (query.to) {
    params = params.set('to', query.to);
  }
  if (query.scope && query.scope !== 'shop') {
    params = params.set('scope', query.scope);
  }
  if (query.includeInactive) {
    params = params.set('includeInactive', 'true');
  }
  return params;
}
