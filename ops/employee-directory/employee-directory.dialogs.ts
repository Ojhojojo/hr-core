import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatCheckbox } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatDivider } from '@angular/material/divider';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import {
  type ScorecardPeriod,
  apiErrorMessage,
  TIN_FORMAT_HINT,
  BannerComponent,
  DialogComponent,
} from '@hr-host';
import { MatOption, MatSelect } from '@angular/material/select';
import { WEEKDAY_LABELS } from '../payroll/payroll-premiums.format';
import {
  DirectoryRole,
  EmployeeCard,
  EmployeeDirectoryApi,
  EmploymentDocType,
  PayBasis,
} from './employee-directory.api';

export interface DirectoryShopOption {
  id: string;
  name: string;
  kind: string;
  /** KPI review period for the shop. Only the Owner's branch list carries it. */
  scorecardPeriod?: ScorecardPeriod;
}

export interface DirectoryPersonDialogData {
  mode: 'add' | 'edit';
  person?: EmployeeCard;
  isOwner: boolean;
  shops: DirectoryShopOption[];
  presetRole?: DirectoryRole;
  presetShopId?: string | null;
}

export const DIRECTORY_ROLE_LABELS: Record<DirectoryRole, string> = {
  owner: 'Owner',
  admin: 'Admin',
  technician: 'Technician',
  sales: 'Sales',
};

const ROLE_HINTS: Record<DirectoryRole, string> = {
  owner: 'Full access, including staff and settings.',
  admin: 'Runs the shop day to day. Cannot change roles.',
  technician: 'Works jobs on the floor.',
  sales: 'Front desk: customers and estimates.',
};

@Component({
  selector: 'app-directory-person-dialog',
  imports: [
    FormsModule,
    MatButton,
    MatCheckbox,
    MatDivider,
    MatFormField,
    MatLabel,
    MatHint,
    MatError,
    MatInput,
    MatSelect,
    MatOption,
    DialogComponent,
    BannerComponent,
  ],
  template: `
    <ops-dialog
      [title]="
        data.mode === 'add' ? 'Add person' : 'Edit ' + (data.person?.displayName ?? 'person')
      "
    >
      <form id="directory-person" (ngSubmit)="save()">
        <div class="dir-form">
          @if (error()) {
            <ops-banner tone="danger" role="alert" [message]="error()" />
          }

          <section class="dir-form-section" data-section="identity">
            <h3 class="dir-form-title">Identity</h3>
            <div class="dir-form-grid">
              <mat-form-field appearance="outline" class="dir-span-2">
                <mat-label>Full name</mat-label>
                <input
                  matInput
                  name="displayName"
                  [(ngModel)]="displayName"
                  required
                  maxlength="200"
                  autocomplete="off"
                />
                <mat-error>Name is required.</mat-error>
              </mat-form-field>
              @if (data.mode === 'add') {
                <mat-form-field appearance="outline">
                  <mat-label>Email</mat-label>
                  <input
                    matInput
                    name="email"
                    type="email"
                    [(ngModel)]="email"
                    required
                    email
                    autocomplete="off"
                  />
                  <mat-error>Enter a valid email.</mat-error>
                </mat-form-field>
              } @else {
                <mat-form-field appearance="outline">
                  <mat-label>Email</mat-label>
                  <input
                    matInput
                    name="emailReadonly"
                    [value]="data.person?.email ?? ''"
                    disabled
                  />
                  <mat-hint>Sign-in email can't be changed here.</mat-hint>
                </mat-form-field>
              }
              <mat-form-field appearance="outline">
                <mat-label>Employee number</mat-label>
                <input matInput name="employeeNo" [(ngModel)]="employeeNo" maxlength="100" />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>TIN</mat-label>
                <input matInput name="tin" [(ngModel)]="tin" maxlength="40" autocomplete="off" />
                <mat-hint>{{ tinFormatHint }}</mat-hint>
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>SSS</mat-label>
                <input matInput name="sss" [(ngModel)]="sss" maxlength="40" autocomplete="off" />
                <mat-hint>00-0000000-0</mat-hint>
              </mat-form-field>
            </div>
          </section>

          <mat-divider />

          @if (data.mode !== 'add') {
            <section class="dir-form-section" data-section="tax-address">
              <h3 class="dir-form-title">Tax address and birth date</h3>
              <div class="dir-form-grid">
                <mat-form-field appearance="outline" class="dir-span-2">
                  <mat-label>Registered address</mat-label>
                  <input
                    matInput
                    name="registeredAddress"
                    [(ngModel)]="registeredAddress"
                    maxlength="300"
                    autocomplete="off"
                  />
                  <mat-hint>As it should appear on BIR Form 2316.</mat-hint>
                </mat-form-field>
                <mat-form-field appearance="outline">
                  <mat-label>ZIP code</mat-label>
                  <input
                    matInput
                    name="zipCode"
                    [(ngModel)]="zipCode"
                    maxlength="10"
                    autocomplete="off"
                  />
                </mat-form-field>
                <mat-form-field appearance="outline">
                  <mat-label>Birth date</mat-label>
                  <input matInput name="birthDate" type="date" [(ngModel)]="birthDate" />
                </mat-form-field>
              </div>
            </section>
          }

          <section class="dir-form-section" data-section="role">
            <h3 class="dir-form-title">Role &amp; access</h3>
            <div class="dir-form-grid">
              @if (data.mode === 'add' || data.isOwner) {
                <mat-form-field appearance="outline">
                  <mat-label>Role</mat-label>
                  <mat-select name="role" [(ngModel)]="role" (ngModelChange)="onRoleChange($event)">
                    <mat-option value="admin">Admin</mat-option>
                    <mat-option value="technician">Technician</mat-option>
                    <mat-option value="sales">Sales</mat-option>
                    <mat-option value="owner">Owner</mat-option>
                  </mat-select>
                  <mat-hint>{{ roleHint(role) }}</mat-hint>
                </mat-form-field>
              } @else {
                <mat-form-field appearance="outline">
                  <mat-label>Role</mat-label>
                  <input matInput name="roleReadonly" [value]="roleLabel(role)" disabled />
                  <mat-hint>Only the owner can change roles.</mat-hint>
                </mat-form-field>
              }
              @if ((data.mode === 'add' || data.isOwner) && data.shops.length > 0) {
                <mat-form-field appearance="outline">
                  <mat-label>Home shop</mat-label>
                  <mat-select name="branchId" [(ngModel)]="branchId" [disabled]="role === 'owner'">
                    @if (role === 'owner') {
                      <mat-option value="">All shops</mat-option>
                    }
                    @for (shop of data.shops; track shop.id) {
                      <mat-option [value]="shop.id">{{ shop.name }}</mat-option>
                    }
                  </mat-select>
                </mat-form-field>
              }
              @if (data.isOwner) {
                <div class="dir-span-2 dir-perms-block">
                  <mat-checkbox
                    name="canCanvassAndPaySuppliers"
                    [(ngModel)]="canCanvassAndPaySuppliers"
                  >
                    Can canvass &amp; pay suppliers
                  </mat-checkbox>
                  <div class="dir-perm-hinted">
                    <mat-checkbox name="canOpenLoaPdf" [(ngModel)]="canOpenLoaPdf">
                      Can open LOA PDF
                    </mat-checkbox>
                    <small class="dir-perm-hint">
                      PDF on does not turn pesos on. If the PDF prints the amount, viewing it will
                      reveal it.
                    </small>
                  </div>
                  <mat-checkbox name="canSeeLoaPesos" [(ngModel)]="canSeeLoaPesos">
                    Can see LOA pesos / job margin
                  </mat-checkbox>
                  <div class="dir-perm-hinted">
                    <mat-checkbox
                      name="mustClock"
                      [(ngModel)]="mustClock"
                      [disabled]="role === 'owner'"
                    >
                      Must clock
                    </mat-checkbox>
                    <small class="dir-perm-hint">
                      On for technician and front desk. Off for sales. Owner does not clock in.
                    </small>
                  </div>
                </div>
              }
            </div>
          </section>

          <mat-divider />

          <section class="dir-form-section" data-section="employment">
            <h3 class="dir-form-title">Employment</h3>
            <div class="dir-form-grid">
              <mat-form-field appearance="outline">
                <mat-label>Position</mat-label>
                <input matInput name="position" [(ngModel)]="position" maxlength="100" />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>Department</mat-label>
                <input matInput name="department" [(ngModel)]="department" maxlength="100" />
              </mat-form-field>
              @if (data.mode === 'edit') {
                <mat-form-field appearance="outline" class="dir-span-2">
                  <mat-label>Rest day</mat-label>
                  <mat-select name="restDays" multiple [(ngModel)]="restDays">
                    @for (day of restDayOptions; track day.value) {
                      <mat-option [value]="day.value">{{ day.label }}</mat-option>
                    }
                  </mat-select>
                </mat-form-field>
              }
              <mat-form-field appearance="outline">
                <mat-label>Hire date</mat-label>
                <input
                  matInput
                  name="hireDate"
                  type="date"
                  [(ngModel)]="hireDate"
                  [required]="data.mode === 'add'"
                />
                <mat-error>Hire date is required.</mat-error>
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>PhilHealth</mat-label>
                <input
                  matInput
                  name="philHealth"
                  [(ngModel)]="philHealth"
                  maxlength="40"
                  autocomplete="off"
                />
                <mat-hint>00-000000000-0</mat-hint>
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>Pag-IBIG</mat-label>
                <input
                  matInput
                  name="pagIbig"
                  [(ngModel)]="pagIbig"
                  maxlength="40"
                  autocomplete="off"
                />
                <mat-hint>0000-0000-0000</mat-hint>
              </mat-form-field>
              @if (data.isOwner) {
                <mat-form-field appearance="outline">
                  <mat-label>Separation date</mat-label>
                  <input matInput name="separationDate" type="date" [(ngModel)]="separationDate" />
                  <mat-hint>When the worker left. Drives the final pay estimate.</mat-hint>
                </mat-form-field>
                <mat-form-field appearance="outline">
                  <mat-label>Separation reason</mat-label>
                  <input
                    matInput
                    name="separationReason"
                    [(ngModel)]="separationReason"
                    maxlength="200"
                  />
                </mat-form-field>
              }
            </div>
          </section>

          <mat-divider />

          <section class="dir-form-section" data-section="contact">
            <h3 class="dir-form-title">Contact</h3>
            <div class="dir-form-grid">
              <mat-form-field appearance="outline">
                <mat-label>Mobile</mat-label>
                <input
                  matInput
                  name="mobile"
                  type="tel"
                  inputmode="tel"
                  [(ngModel)]="mobile"
                  maxlength="40"
                />
              </mat-form-field>
            </div>
            <h4 class="dir-form-subtitle">Emergency contact</h4>
            <div class="dir-form-grid">
              <mat-form-field appearance="outline">
                <mat-label>Contact name</mat-label>
                <input
                  matInput
                  name="emergencyContactName"
                  [(ngModel)]="emergencyName"
                  maxlength="200"
                />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>Contact phone</mat-label>
                <input
                  matInput
                  name="emergencyContactPhone"
                  type="tel"
                  inputmode="tel"
                  [(ngModel)]="emergencyPhone"
                  maxlength="40"
                />
              </mat-form-field>
            </div>
          </section>

          @if (data.isOwner) {
            <mat-divider />

            <section class="dir-form-section" data-section="pay">
              <h3 class="dir-form-title">Pay rate</h3>
              <div class="dir-form-grid">
                <mat-form-field appearance="outline">
                  <mat-label>Pay basis</mat-label>
                  <mat-select
                    name="payBasis"
                    [ngModel]="payBasis"
                    (ngModelChange)="onPayBasisChange($event)"
                  >
                    <mat-option [value]="null">None</mat-option>
                    <mat-option value="hourly">Hourly</mat-option>
                    <mat-option value="daily">Daily</mat-option>
                    <mat-option value="monthly">Monthly</mat-option>
                  </mat-select>
                </mat-form-field>
                <mat-form-field appearance="outline">
                  <mat-label>{{
                    payBasis === 'monthly' ? 'Monthly rate (PHP)' : 'Rate (PHP)'
                  }}</mat-label>
                  <input
                    matInput
                    name="ratePhp"
                    type="number"
                    step="0.01"
                    min="0"
                    [(ngModel)]="ratePhp"
                    placeholder="Rate PHP"
                  />
                </mat-form-field>
                @if (payBasis === 'hourly') {
                  <mat-form-field appearance="outline">
                    <mat-label>OT after hours</mat-label>
                    <input
                      matInput
                      name="otAfterHoursPerDay"
                      type="number"
                      step="0.5"
                      min="0"
                      [(ngModel)]="otAfterHoursPerDay"
                    />
                  </mat-form-field>
                  <mat-form-field appearance="outline">
                    <mat-label>OT multiplier</mat-label>
                    <input
                      matInput
                      name="otMultiplier"
                      type="number"
                      step="0.05"
                      min="1"
                      [(ngModel)]="otMultiplier"
                    />
                  </mat-form-field>
                }
              </div>
            </section>
          }
        </div>
      </form>

      <ng-container dialogActions>
        <button matButton="text" type="button" (click)="close()">Cancel</button>
        <button matButton="filled" type="submit" form="directory-person" [disabled]="saving()">
          {{ data.mode === 'add' ? 'Add person' : 'Save changes' }}
        </button>
      </ng-container>
    </ops-dialog>
  `,
  styles: `
    .dir-form {
      display: grid;
      gap: var(--space-4);
      padding-top: var(--space-2);
    }

    .dir-form-section {
      display: grid;
      gap: var(--space-2);
    }

    .dir-form-title {
      margin: 0;
      font-size: 0.8rem;
      font-weight: 700;
      letter-spacing: 0.06em;
      text-transform: uppercase;
      color: var(--ops-muted, #5b6178);
    }

    .dir-form-subtitle {
      margin: var(--space-1) 0 0;
      font-size: 0.85rem;
      font-weight: 600;
    }

    .dir-form-grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      column-gap: var(--space-3);
      row-gap: var(--space-1);
    }

    .dir-form-grid mat-form-field {
      width: 100%;
    }

    .dir-span-2 {
      grid-column: 1 / -1;
    }

    .dir-perms-block {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      margin-top: var(--space-1);
    }

    .dir-perm-hinted {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
    }

    .dir-perm-hint {
      margin: 0 0 0 var(--space-6);
      font-size: 0.75rem;
      color: var(--ops-muted, #5b6178);
      font-style: italic;
    }

    @media (max-width: 600px) {
      .dir-form-grid {
        grid-template-columns: 1fr;
      }
    }
  `,
})
export class DirectoryPersonDialog {
  protected readonly tinFormatHint = TIN_FORMAT_HINT;
  protected readonly data = inject<DirectoryPersonDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<DirectoryPersonDialog, boolean>);
  private readonly api = inject(EmployeeDirectoryApi);

  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected displayName = this.data.person?.displayName ?? '';
  protected email = '';
  protected role: DirectoryRole = this.data.person?.role ?? this.data.presetRole ?? 'technician';
  protected hireDate = this.data.person?.hireDate ?? '';
  protected separationDate = this.data.person?.separationDate ?? '';
  protected separationReason = this.data.person?.separationReason ?? '';
  protected mobile = this.data.person?.mobile ?? '';
  protected emergencyName = this.data.person?.emergencyContactName ?? '';
  protected emergencyPhone = this.data.person?.emergencyContactPhone ?? '';
  protected position = this.data.person?.position ?? '';
  protected department = this.data.person?.department ?? '';
  protected employeeNo = this.data.person?.employeeNo ?? '';
  protected tin = this.data.person?.tin ?? '';
  protected sss = this.data.person?.sss ?? '';
  protected philHealth = this.data.person?.philHealth ?? '';
  protected pagIbig = this.data.person?.pagIbig ?? '';
  protected registeredAddress = this.data.person?.registeredAddress ?? '';
  protected zipCode = this.data.person?.zipCode ?? '';
  protected birthDate = this.data.person?.birthDate ?? '';
  protected branchId =
    this.data.person?.branchId ??
    (this.data.person?.role === 'owner'
      ? ''
      : (this.data.presetShopId ?? this.data.shops[0]?.id ?? ''));
  protected canCanvassAndPaySuppliers = this.data.person?.canCanvassAndPaySuppliers ?? true;
  protected canOpenLoaPdf = this.data.person?.canOpenLoaPdf ?? true;
  protected canSeeLoaPesos = this.data.person?.canSeeLoaPesos ?? true;
  protected mustClock = this.data.person?.mustClock ?? (this.role === 'owner' ? false : true);
  protected payBasis: PayBasis | null = this.data.person?.payBasis ?? null;
  protected ratePhp: number | null = this.data.person?.ratePhp ?? null;
  protected otAfterHoursPerDay: number | null =
    this.data.person?.otAfterHoursPerDay ?? (this.payBasis === 'hourly' ? 8 : null);
  protected otMultiplier: number | null =
    this.data.person?.otMultiplier ?? (this.payBasis === 'hourly' ? 1.0 : null);
  protected restDays: number[] = [...(this.data.person?.restDays ?? [])];
  protected readonly restDayOptions = WEEKDAY_LABELS.map((label, value) => ({ value, label }));

  private readonly initialSnapshot = {
    displayName: this.displayName,
    email: this.email,
    role: this.role,
    hireDate: this.hireDate,
    separationDate: this.separationDate,
    separationReason: this.separationReason,
    mobile: this.mobile,
    emergencyName: this.emergencyName,
    emergencyPhone: this.emergencyPhone,
    position: this.position,
    department: this.department,
    employeeNo: this.employeeNo,
    tin: this.tin,
    sss: this.sss,
    philHealth: this.philHealth,
    pagIbig: this.pagIbig,
    registeredAddress: this.registeredAddress,
    zipCode: this.zipCode,
    birthDate: this.birthDate,
    branchId: this.branchId,
    canCanvassAndPaySuppliers: this.canCanvassAndPaySuppliers,
    canOpenLoaPdf: this.canOpenLoaPdf,
    canSeeLoaPesos: this.canSeeLoaPesos,
    mustClock: this.mustClock,
    payBasis: this.payBasis,
    ratePhp: this.ratePhp,
    otAfterHoursPerDay: this.otAfterHoursPerDay,
    otMultiplier: this.otMultiplier,
    restDays: [...this.restDays],
  };

  protected isDirty(): boolean {
    return (
      this.displayName !== this.initialSnapshot.displayName ||
      this.email !== this.initialSnapshot.email ||
      this.role !== this.initialSnapshot.role ||
      this.hireDate !== this.initialSnapshot.hireDate ||
      this.separationDate !== this.initialSnapshot.separationDate ||
      this.separationReason !== this.initialSnapshot.separationReason ||
      this.mobile !== this.initialSnapshot.mobile ||
      this.emergencyName !== this.initialSnapshot.emergencyName ||
      this.emergencyPhone !== this.initialSnapshot.emergencyPhone ||
      this.position !== this.initialSnapshot.position ||
      this.department !== this.initialSnapshot.department ||
      this.employeeNo !== this.initialSnapshot.employeeNo ||
      this.tin !== this.initialSnapshot.tin ||
      this.sss !== this.initialSnapshot.sss ||
      this.philHealth !== this.initialSnapshot.philHealth ||
      this.pagIbig !== this.initialSnapshot.pagIbig ||
      this.registeredAddress !== this.initialSnapshot.registeredAddress ||
      this.zipCode !== this.initialSnapshot.zipCode ||
      this.birthDate !== this.initialSnapshot.birthDate ||
      this.branchId !== this.initialSnapshot.branchId ||
      this.canCanvassAndPaySuppliers !== this.initialSnapshot.canCanvassAndPaySuppliers ||
      this.canOpenLoaPdf !== this.initialSnapshot.canOpenLoaPdf ||
      this.canSeeLoaPesos !== this.initialSnapshot.canSeeLoaPesos ||
      this.mustClock !== this.initialSnapshot.mustClock ||
      this.payBasis !== this.initialSnapshot.payBasis ||
      this.ratePhp !== this.initialSnapshot.ratePhp ||
      this.otAfterHoursPerDay !== this.initialSnapshot.otAfterHoursPerDay ||
      this.otMultiplier !== this.initialSnapshot.otMultiplier ||
      !sameDays(this.restDays, this.initialSnapshot.restDays)
    );
  }

  protected roleLabel(role: DirectoryRole): string {
    return DIRECTORY_ROLE_LABELS[role] ?? role;
  }

  protected roleHint(role: DirectoryRole): string {
    return ROLE_HINTS[role] ?? '';
  }

  protected onRoleChange(role: DirectoryRole): void {
    this.role = role;
    if (role === 'owner') {
      this.mustClock = false;
      this.branchId = '';
    }
  }

  protected onPayBasisChange(basis: PayBasis | null): void {
    this.payBasis = basis;
    if (basis === 'hourly') {
      if (this.otAfterHoursPerDay == null) {
        this.otAfterHoursPerDay = 8;
      }
      if (this.otMultiplier == null) {
        this.otMultiplier = 1.0;
      }
    }
  }

  protected close(): void {
    this.ref.close(false);
  }

  protected save(): void {
    const displayName = this.displayName.trim();
    if (!displayName) {
      this.error.set('Name is required.');
      return;
    }

    this.saving.set(true);
    this.error.set('');
    const rate = parseOptionalNumber(this.ratePhp);
    const otHours =
      this.payBasis === 'hourly' ? parseOptionalNumber(this.otAfterHoursPerDay) : null;
    const otMult = this.payBasis === 'hourly' ? parseOptionalNumber(this.otMultiplier) : null;

    if (this.data.mode === 'add') {
      const email = this.email.trim();
      if (!email || !this.hireDate) {
        this.error.set('Name, email, and hire date are required.');
        this.saving.set(false);
        return;
      }
      const allShops = this.role === 'owner' && this.branchId === '' && this.data.shops.length > 0;
      this.api
        .create({
          email,
          displayName,
          role: this.role,
          hireDate: this.hireDate,
          mobile: this.mobile.trim() || null,
          emergencyContactName: this.emergencyName.trim() || null,
          emergencyContactPhone: this.emergencyPhone.trim() || null,
          position: this.position.trim() || null,
          department: this.department.trim() || null,
          employeeNo: this.employeeNo.trim() || null,
          tin: this.tin.trim() || null,
          sss: this.sss.trim() || null,
          philHealth: this.philHealth.trim() || null,
          pagIbig: this.pagIbig.trim() || null,
          ...(allShops ? { allShops: true } : {}),
          ...(this.branchId && !allShops ? { branchId: this.branchId } : {}),
          ...(this.data.isOwner
            ? {
                canCanvassAndPaySuppliers: this.canCanvassAndPaySuppliers,
                canOpenLoaPdf: this.canOpenLoaPdf,
                canSeeLoaPesos: this.canSeeLoaPesos,
                mustClock: this.role === 'owner' ? false : this.mustClock,
                ...(this.payBasis
                  ? {
                      payBasis: this.payBasis,
                      ratePhp: rate,
                      otAfterHoursPerDay: otHours,
                      otMultiplier: otMult,
                    }
                  : {}),
              }
            : {}),
        })
        .subscribe({
          next: () => {
            this.saving.set(false);
            this.ref.close(true);
          },
          error: (err: unknown) => {
            this.error.set(apiErrorMessage(err));
            this.saving.set(false);
          },
        });
      return;
    }

    const person = this.data.person;
    if (!person) {
      this.saving.set(false);
      return;
    }

    if (!this.data.isOwner && person.role === 'owner') {
      this.error.set("You cannot edit an owner's record.");
      this.saving.set(false);
      return;
    }

    const payDirty =
      this.payBasis !== this.initialSnapshot.payBasis ||
      !sameOptionalNumber(rate, this.initialSnapshot.ratePhp) ||
      !sameOptionalNumber(otHours, this.initialSnapshot.otAfterHoursPerDay) ||
      !sameOptionalNumber(otMult, this.initialSnapshot.otMultiplier);
    const accessDirty =
      this.branchId !== this.initialSnapshot.branchId ||
      this.canCanvassAndPaySuppliers !== this.initialSnapshot.canCanvassAndPaySuppliers ||
      this.canOpenLoaPdf !== this.initialSnapshot.canOpenLoaPdf ||
      this.canSeeLoaPesos !== this.initialSnapshot.canSeeLoaPesos ||
      this.mustClock !== this.initialSnapshot.mustClock;

    this.api
      .update(person.id, {
        displayName,
        mobile: this.mobile.trim(),
        hireDate: this.hireDate || null,
        emergencyContactName: this.emergencyName.trim(),
        emergencyContactPhone: this.emergencyPhone.trim(),
        position: this.position.trim(),
        department: this.department.trim(),
        employeeNo: this.employeeNo.trim(),
        tin: this.tin.trim(),
        sss: this.sss.trim(),
        philHealth: this.philHealth.trim(),
        pagIbig: this.pagIbig.trim(),
        registeredAddress: this.registeredAddress.trim(),
        zipCode: this.zipCode.trim(),
        birthDate: this.birthDate || null,
        clearBirthDate: !this.birthDate,
        ...(this.data.isOwner
          ? {
              role: this.role,
              separationDate: this.separationDate || null,
              separationReason: this.separationReason.trim(),
              ...(accessDirty
                ? {
                    branchId:
                      this.role === 'owner' && !this.branchId ? null : this.branchId || null,
                    clearHomeBranch: this.role === 'owner' && !this.branchId,
                    canCanvassAndPaySuppliers: this.canCanvassAndPaySuppliers,
                    canOpenLoaPdf: this.canOpenLoaPdf,
                    canSeeLoaPesos: this.canSeeLoaPesos,
                    mustClock: this.role === 'owner' ? false : this.mustClock,
                  }
                : {}),
              ...(payDirty
                ? {
                    payBasis: this.payBasis,
                    ratePhp: rate,
                    otAfterHoursPerDay: otHours,
                    otMultiplier: otMult,
                    clearPayBasis: !this.payBasis,
                    clearRate: rate == null,
                  }
                : {}),
            }
          : {}),
        restDays: [...this.restDays].sort((left, right) => left - right),
        ...(this.restDays.length === 0 ? { clearRestDays: true } : {}),
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.ref.close(true);
        },
        error: (err: unknown) => {
          this.error.set(apiErrorMessage(err));
          this.saving.set(false);
        },
      });
  }
}

function sameDays(left: readonly number[], right: readonly number[]): boolean {
  if (left.length !== right.length) {
    return false;
  }
  const a = [...left].sort((x, y) => x - y);
  const b = [...right].sort((x, y) => x - y);
  return a.every((day, index) => day === b[index]);
}

function parseOptionalNumber(value: number | null | '' | undefined): number | null {
  if (value == null || value === ('' as unknown)) {
    return null;
  }
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}

function sameOptionalNumber(left: number | null, right: number | null): boolean {
  if (left == null && right == null) {
    return true;
  }
  if (left == null || right == null) {
    return false;
  }
  return left === right;
}

export interface EmployeeDocumentUploadDialogData {
  person: EmployeeCard;
}

@Component({
  selector: 'app-employee-document-upload-dialog',
  imports: [
    FormsModule,
    MatButton,
    MatFormField,
    MatLabel,
    MatInput,
    MatSelect,
    MatOption,
    DialogComponent,
    BannerComponent,
  ],
  styles: `
    .emp-doc-upload-form {
      display: flex;
      flex-direction: column;
      gap: var(--space-6);
      padding: var(--space-2) 0;
    }
    .emp-file-picker {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-3);
      padding: var(--space-1) 0;
    }
    .emp-file-name {
      min-width: 0;
      font-size: 0.875rem;
      color: var(--ops-muted, #5b6178);
      overflow-wrap: anywhere;
      max-width: 100%;
    }
  `,
  template: `
    <ops-dialog title="Add document" [subtitle]="data.person.displayName">
      <form id="upload-doc-form" (ngSubmit)="upload()">
        <div class="emp-doc-upload-form">
          @if (error()) {
            <ops-banner tone="danger" role="alert" [message]="error()" />
          }

          <mat-form-field appearance="outline">
            <mat-label>Document type</mat-label>
            <mat-select name="docType" [(ngModel)]="docType">
              <mat-option value="contract">Contract</mat-option>
              <mat-option value="offer">Job offer</mat-option>
              <mat-option value="id">Government / Valid ID</mat-option>
              <mat-option value="coe_request_note">COE request note</mat-option>
              <mat-option value="other">Other document</mat-option>
            </mat-select>
          </mat-form-field>

          <div class="emp-file-picker">
            <button matButton="outlined" type="button" (click)="fileInput.click()">
              Choose file
            </button>
            <input
              #fileInput
              type="file"
              hidden
              accept=".pdf,.jpg,.jpeg,.png,.webp,application/pdf,image/jpeg,image/png,image/webp"
              (change)="onFileSelected($event)"
            />
            <span class="emp-file-name">
              {{ fileName() || 'No file selected' }}
            </span>
          </div>

          <mat-form-field appearance="outline">
            <mat-label>Note (optional)</mat-label>
            <input
              matInput
              name="note"
              [(ngModel)]="note"
              placeholder="e.g. Signed 2026 employment agreement"
              maxlength="300"
            />
          </mat-form-field>
        </div>
      </form>

      <div dialogActions>
        <button matButton="text" type="button" (click)="close()">Cancel</button>
        <button
          matButton="filled"
          type="submit"
          form="upload-doc-form"
          [disabled]="saving() || !file"
        >
          {{ saving() ? 'Uploading…' : 'Upload' }}
        </button>
      </div>
    </ops-dialog>
  `,
})
export class EmployeeDocumentUploadDialog {
  protected readonly data = inject<EmployeeDocumentUploadDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<EmployeeDocumentUploadDialog, boolean>);
  private readonly api = inject(EmployeeDirectoryApi);

  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly fileName = signal('');
  protected docType: EmploymentDocType = 'contract';
  protected note = '';
  protected file: File | null = null;

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.file = input.files?.[0] ?? null;
    this.fileName.set(this.file?.name ?? '');
  }

  protected close(): void {
    this.ref.close(false);
  }

  protected upload(): void {
    if (!this.file) {
      this.error.set('Please select a file to upload.');
      return;
    }
    this.saving.set(true);
    this.error.set('');
    this.api
      .upload(this.data.person.id, this.file, this.docType, this.note.trim() || null)
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.ref.close(true);
        },
        error: (err: unknown) => {
          this.error.set(apiErrorMessage(err));
          this.saving.set(false);
        },
      });
  }
}
