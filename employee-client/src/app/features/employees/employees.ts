import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { HrmService, Department } from '../../core/services/hrm.service';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { Icon } from '../../shared/icon';
import { readApiError } from '../../core/api-error';
import { Employee, EmployeePayload } from '../../core/models/employee.model';
import { EmployeeService } from '../../core/services/employee.service';

@Component({
  selector: 'app-employees',
  imports: [DatePipe, ReactiveFormsModule, RouterLink, Icon],
  templateUrl: './employees.html',
  styleUrl: './employees.scss',
})
export class Employees {
  private readonly employeeService = inject(EmployeeService);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly employees = signal<Employee[]>([]);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly deletingId = signal<string | null>(null);
  protected readonly error = signal('');
  protected readonly formError = signal('');
  protected readonly showForm = signal(false);
  protected readonly editingId = signal<string | null>(null);
  protected readonly activeCount = computed(
    () => this.employees().filter((employee) => employee.isActive).length,
  );
  protected readonly departmentCount = computed(
    () => new Set(this.employees().map((employee) => employee.departmentId)).size,
  );

  protected readonly form = this.formBuilder.nonNullable.group({
    employeeNumber: ['', Validators.required],
    firstName: ['', Validators.required],
    lastName: ['', Validators.required],
    email: ['', [Validators.required, Validators.email]],
    departmentId: [''],
    jobTitle: ['', Validators.required],
    salary: [0, [Validators.required, Validators.min(0)]],
    hireDate: [this.today(), Validators.required],
    isActive: [true],
    managerEmployeeId: [''],
    provisionAppAccess: [false],
    password: [''],
  });

  protected readonly hrm = inject(HrmService);
  protected readonly departments = signal<Department[]>([]);
  protected readonly isHR = computed(() => ['OrganizationOwner', 'HRAdmin'].includes(this.hrm.organization()?.role ?? ''));
  private creationKey = crypto.randomUUID();
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly params = toSignal(this.route.queryParamMap);
  protected readonly searchTerm = computed(() => this.params()?.get('q')?.trim() ?? '');
  protected readonly filteredEmployees = computed(() => {
    const term = this.searchTerm().toLowerCase();
    return this.employees().filter(e => !term || `${e.firstName} ${e.lastName} ${e.email} ${e.employeeNumber}`.toLowerCase().includes(term));
  });
  constructor() {
    effect(() => {
      const org = this.hrm.organization();
      untracked(() => {
        this.showForm.set(false); this.form.controls.password.reset(); this.employees.set([]); this.departments.set([]);
        if (org) { this.load(); this.loadDepartments(); } else this.loading.set(false);
      });
    });
    effect(() => {
      const action = this.params()?.get('action'); const org = this.hrm.organization();
      if (action === 'add' && org && this.isHR()) untracked(() => {
        this.openCreate(); void this.router.navigate([], {relativeTo: this.route, queryParams: {action: null}, queryParamsHandling: 'merge', replaceUrl: true});
      });
    });
  }
  protected loadDepartments() {
    const org = this.hrm.organization()?.id;
    this.hrm.departments().subscribe({next: d => { if (org === this.hrm.organization()?.id) this.departments.set(d); }, error: e => this.error.set(readApiError(e, 'Unable to load departments.'))});
  }
  protected departmentLabel(id: string) { return this.departments().find(d => d.id === id)?.name ?? 'Unassigned'; }
  protected sendSetup(employee: Employee) {
    this.hrm.setupEmail(employee.email).subscribe({next: result => this.error.set(result.message), error: e => this.error.set(readApiError(e, 'Unable to send password setup email.'))});
  }

  protected load(): void {
    if (!this.hrm.organization()) return;
    const org = this.hrm.organization()?.id;
    this.loading.set(true);
    this.error.set('');
    this.employeeService
      .getAll()
      .pipe(finalize(() => { if (org === this.hrm.organization()?.id) this.loading.set(false); }))
      .subscribe({
        next: (employees) => { if (org === this.hrm.organization()?.id) this.employees.set(employees); },
        error: (error) =>
          { if (org === this.hrm.organization()?.id) this.error.set(readApiError(error, 'Unable to load employees.')); },
      });
  }

  protected openCreate(): void {
    this.editingId.set(null);
    this.creationKey = crypto.randomUUID();
    this.formError.set('');
    this.form.reset({
      employeeNumber: '',
      firstName: '',
      lastName: '',
      email: '',
      departmentId: '',
      jobTitle: '',
      salary: 0,
      hireDate: this.today(),
      isActive: true,
      managerEmployeeId: '',
      provisionAppAccess: false,
      password: '',
    });
    this.showForm.set(true);
  }

  protected openEdit(employee: Employee): void {
    this.editingId.set(employee.id);
    this.formError.set('');
    this.form.setValue({
      employeeNumber: employee.employeeNumber,
      firstName: employee.firstName,
      lastName: employee.lastName,
      email: employee.email,
      departmentId: employee.departmentId,
      jobTitle: employee.jobTitle,
      salary: employee.salary ?? 0,
      hireDate: employee.hireDate.slice(0, 10),
      isActive: employee.isActive,
      managerEmployeeId: employee.managerEmployeeId ?? '',
      provisionAppAccess: false,
      password: '',
    });
    this.showForm.set(true);
  }

  protected closeForm(): void {
    if (!this.saving()) {
      this.form.controls.password.reset();
      this.showForm.set(false);
    }
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const values = this.form.getRawValue();
    const payload: EmployeePayload = {
      ...values,
      password: values.password || undefined,
      hireDate: new Date(`${values.hireDate}T00:00:00.000Z`).toISOString(),
    };
    const id = this.editingId();
    const request = id
      ? this.employeeService.update(id, payload)
      : this.employeeService.create(payload, this.creationKey);

    this.saving.set(true);
    this.formError.set('');
    request.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: () => {
        this.form.controls.password.reset();
        this.showForm.set(false);
        this.load();
      },
      error: (error) =>
        this.formError.set(readApiError(error, 'Unable to save the employee.')),
    });
  }

  protected deleteEmployee(employee: Employee): void {
    const fullName = `${employee.firstName} ${employee.lastName}`;
    if (!window.confirm(`Delete ${fullName}? This action cannot be undone.`)) {
      return;
    }

    this.deletingId.set(employee.id);
    this.error.set('');
    this.employeeService
      .delete(employee.id)
      .pipe(finalize(() => this.deletingId.set(null)))
      .subscribe({
        next: () =>
          this.employees.update((employees) =>
            employees.filter((item) => item.id !== employee.id),
          ),
        error: (error) =>
          this.error.set(readApiError(error, 'Unable to delete the employee.')),
      });
  }

  protected initials(employee: Employee): string {
    return `${employee.firstName.charAt(0)}${employee.lastName.charAt(0)}`.toUpperCase();
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }
}
