import { HttpClient } from '@angular/common/http';
import { Component, computed, effect, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatCheckbox } from '@angular/material/checkbox';
import { MatDialog } from '@angular/material/dialog';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatMenu, MatMenuItem, MatMenuTrigger } from '@angular/material/menu';
import { MatPaginator, MatPaginatorModule } from '@angular/material/paginator';
import { MatTableDataSource, MatTableModule } from '@angular/material/table';
import { ActivatedRoute, Router } from '@angular/router';
import {
  environment,
  AuthService,
  canPickHomeShop,
  apiErrorMessage,
  BannerComponent,
  CardComponent,
  EmptyStateComponent,
  OpsConfirmService,
  OpsToastService,
  PageHeaderComponent,
  SkeletonComponent,
  StatusBadgeComponent,
  StatusBadgeTone,
} from '@hr-host';
import {
  DirectoryConfigIssue,
  DirectoryRole,
  DirectoryStatus,
  EmployeeCard,
  EmployeeDirectoryApi,
  HEALTH_LABELS,
} from './employee-directory.api';
import {
  DIRECTORY_ROLE_LABELS,
  DirectoryPersonDialog,
  DirectoryShopOption,
} from './employee-directory.dialogs';

const ROLE_LABELS = DIRECTORY_ROLE_LABELS;

const STATUS_LABELS: Record<DirectoryStatus, string> = {
  active: 'Active',
  on_leave_today: 'On leave today',
  inactive: 'Inactive',
};

@Component({
  selector: 'app-employee-directory',
  imports: [
    MatButton,
    MatIconButton,
    MatFormField,
    MatLabel,
    MatInput,
    MatCheckbox,
    MatTableModule,
    MatPaginatorModule,
    MatMenu,
    MatMenuItem,
    MatMenuTrigger,
    FormsModule,
    PageHeaderComponent,
    CardComponent,
    BannerComponent,
    EmptyStateComponent,
    SkeletonComponent,
    StatusBadgeComponent,
  ],
  templateUrl: './employee-directory.html',
  styles: `
    tr.ops-row-link:focus-visible td {
      outline: 2px solid var(--mat-sys-primary, currentColor);
      outline-offset: -2px;
    }

    .dir-health {
      margin-bottom: var(--space-4);
    }

    .dir-health-list {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: var(--space-2);
    }

    .dir-health-row {
      display: flex;
      justify-content: space-between;
      align-items: center;
      flex-wrap: wrap;
      gap: var(--space-2);
      padding: var(--space-1) 0;
      border-bottom: 1px solid var(--ops-border, #e4e6ee);
      font-size: 0.9rem;
    }

    .dir-health-row:last-child {
      border-bottom: none;
    }

    .dir-health-more {
      margin-top: var(--space-2);
    }
  `,
})
export class EmployeeDirectoryPage {
  private readonly api = inject(EmployeeDirectoryApi);
  private readonly auth = inject(AuthService);
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);
  private readonly confirm = inject(OpsConfirmService);
  private readonly toast = inject(OpsToastService);
  private readonly route = inject(ActivatedRoute, { optional: true });
  private readonly base = environment.apiBaseUrl;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly people = signal<EmployeeCard[]>([]);
  protected readonly configHealth = signal<DirectoryConfigIssue[]>([]);
  protected readonly headcount = signal(0);
  protected readonly includeInactive = signal(false);
  protected readonly search = signal('');
  protected readonly menuPerson = signal<EmployeeCard | null>(null);
  protected readonly expandedHealth = signal(false);

  protected readonly isOwner = computed(() => this.auth.me()?.staffRole === 'owner');
  protected readonly canEdit = computed(() => {
    const role = this.auth.me()?.staffRole;
    return role === 'owner' || role === 'admin';
  });

  protected canEditPerson(person?: EmployeeCard | null): boolean {
    const role = this.auth.me()?.staffRole;
    if (role === 'owner') {
      return true;
    }
    if (role === 'admin') {
      return person?.role !== 'owner';
    }
    return false;
  }

  protected readonly showHealth = computed(() => this.canEdit() && this.healthChips().length > 0);

  protected readonly healthChips = computed(() => {
    const chips: { staffId: string; displayName: string; code: string; label: string }[] = [];
    for (const issue of this.configHealth()) {
      for (const code of issue.issues ?? []) {
        chips.push({
          staffId: issue.staffId,
          displayName: issue.displayName,
          code,
          label: HEALTH_LABELS[code] ?? code,
        });
      }
    }
    return chips;
  });

  protected readonly displayedHealth = computed(() => {
    const chips = this.healthChips();
    return this.expandedHealth() ? chips : chips.slice(0, 5);
  });

  protected readonly remainingHealthCount = computed(() =>
    Math.max(0, this.healthChips().length - 5),
  );

  protected readonly showRowMenu = computed(() => this.canEdit() || this.isOwner());
  protected readonly headcountLabel = computed(() => `${this.headcount()} active`);

  protected readonly visible = computed(() => {
    const term = this.search().trim().toLowerCase();
    return this.people().filter((person) => {
      if (!this.includeInactive() && !person.active) {
        return false;
      }
      if (!term) {
        return true;
      }
      return (
        person.displayName.toLowerCase().includes(term) ||
        (person.mobile ?? '').toLowerCase().includes(term)
      );
    });
  });

  protected readonly columns = [
    'name',
    'position',
    'role',
    'status',
    'mobile',
    'hireDate',
    'actions',
  ];
  protected readonly table = new MatTableDataSource<EmployeeCard>([]);
  private readonly pager = viewChild(MatPaginator);
  private presetOwnerShop: string | null = null;

  constructor() {
    this.presetOwnerShop = this.route?.snapshot.queryParamMap.get('addOwnerShop') ?? null;
    effect(() => {
      this.table.data = this.visible();
      const pager = this.pager();
      if (pager) {
        this.table.paginator = pager;
      }
    });
    this.reload();
    if (this.presetOwnerShop && this.isOwner()) {
      this.openAdd();
    }
  }

  protected roleLabel(role: DirectoryRole): string {
    return ROLE_LABELS[role] ?? role;
  }

  protected statusKey(person: EmployeeCard): DirectoryStatus {
    if (person.status) {
      return person.status;
    }
    return person.active ? 'active' : 'inactive';
  }

  protected statusLabel(person: EmployeeCard): string {
    return STATUS_LABELS[this.statusKey(person)];
  }

  protected statusTone(person: EmployeeCard): StatusBadgeTone {
    if (person.status === 'on_leave_today') {
      return 'warn';
    }
    return person.active ? 'ok' : 'neutral';
  }

  protected openDetail(person: EmployeeCard): void {
    void this.router.navigate(['/hr/employee-directory', person.id]);
  }

  protected onIncludeInactive(checked: boolean): void {
    this.includeInactive.set(checked);
    this.reload();
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.api.list(this.includeInactive(), this.search()).subscribe({
      next: (body) => {
        this.people.set(body.items);
        this.configHealth.set(body.configHealth ?? []);
        this.headcount.set(body.headcount);
        this.loading.set(false);
        const editId = this.route?.snapshot.queryParamMap.get('edit');
        if (editId) {
          const match = body.items.find((p) => p.id === editId);
          if (match && this.canEditPerson(match)) {
            this.beginEdit(match);
          }
        }
      },
      error: (err: unknown) => {
        this.error.set(apiErrorMessage(err));
        this.loading.set(false);
      },
    });
  }

  protected openHealth(staffId: string): void {
    const person = this.people().find((row) => row.id === staffId);
    if (!person || !this.canEditPerson(person)) {
      return;
    }
    this.beginEdit(person);
  }

  protected openAdd(): void {
    this.error.set('');
    if (this.isOwner() && canPickHomeShop(this.auth.me())) {
      this.http.get<DirectoryShopOption[]>(`${this.base}/api/settings/branches`).subscribe({
        next: (shops) => this.openPersonDialog('add', undefined, shops),
        error: () => this.openPersonDialog('add', undefined, []),
      });
      return;
    }
    this.openPersonDialog('add');
  }

  protected beginEdit(person: EmployeeCard): void {
    if (!this.canEditPerson(person)) {
      return;
    }
    this.error.set('');
    if (this.isOwner() && canPickHomeShop(this.auth.me())) {
      this.http.get<DirectoryShopOption[]>(`${this.base}/api/settings/branches`).subscribe({
        next: (shops) => this.openPersonDialog('edit', person, shops),
        error: () => this.openPersonDialog('edit', person, []),
      });
      return;
    }
    this.openPersonDialog('edit', person);
  }

  protected setActive(person: EmployeeCard, active: boolean): void {
    const verb = active ? 'Reactivate' : 'Deactivate';
    this.confirm
      .confirm({
        title: `${verb} ${person.displayName}?`,
        message: active
          ? `They will appear as active and can be scheduled or clocked in.`
          : `They will be hidden from the active roster. Rate history and documents are preserved.`,
        confirmLabel: verb,
        tone: !active ? 'danger' : 'default',
      })
      .subscribe((res) => {
        if (!res.confirmed) {
          return;
        }
        this.saving.set(true);
        this.api.update(person.id, { active }).subscribe({
          next: () => {
            this.saving.set(false);
            this.toast.success(active ? 'Person reactivated.' : 'Person deactivated.');
            this.reload();
          },
          error: (err: unknown) => {
            const message = apiErrorMessage(err);
            this.error.set(message);
            this.toast.error(message);
            this.saving.set(false);
          },
        });
      });
  }

  private openPersonDialog(
    mode: 'add' | 'edit',
    person?: EmployeeCard,
    shops: DirectoryShopOption[] = [],
  ): void {
    const ref = this.dialog.open(DirectoryPersonDialog, {
      data: {
        mode,
        person,
        isOwner: this.isOwner(),
        shops,
        presetShopId: this.presetOwnerShop,
      },
      width: 'min(48rem, 78vw)',
      maxWidth: 'calc(100vw - 32px)',
    });
    ref.afterClosed().subscribe((saved) => {
      if (saved) {
        this.toast.success('Person saved.');
        this.reload();
      }
    });
  }
}
