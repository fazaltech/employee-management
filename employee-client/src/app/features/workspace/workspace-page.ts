import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { forkJoin, Observable } from 'rxjs';
import { WorkspaceService } from '../../core/services/workspace.service';
import { AuthService } from '../../core/services/auth.service';
import { EmployeeService } from '../../core/services/employee.service';
import { Employee } from '../../core/models/employee.model';
import { Department } from '../../core/services/hrm.service';
import { readApiError } from '../../core/api-error';
import { AttendancePanel } from '../employees/attendance-panel';
import { MembershipPanel } from '../employees/membership-panel';
import { PlatformOwnersPanel } from '../employees/platform-owners-panel';
import { Icon } from '../../shared/icon';

@Component({selector: 'app-workspace-page', imports: [FormsModule, RouterLink, AttendancePanel, MembershipPanel, PlatformOwnersPanel, Icon], templateUrl: './workspace-page.html', styleUrl: './workspace-page.scss'})
export class WorkspacePage {
  protected readonly workspace = inject(WorkspaceService);
  protected readonly auth = inject(AuthService);
  private readonly employees = inject(EmployeeService);
  protected readonly view: string = inject(ActivatedRoute).snapshot.data['view'];
  protected readonly title = ({departments: 'Departments', hierarchy: 'Organization hierarchy', attendance: 'Attendance', settings: 'Workspace settings'} as Record<string,string>)[this.view];
  protected readonly description = ({departments: 'A home for every team in your organization.', hierarchy: 'Understand reporting relationships in your authorized scope.', attendance: 'Record work periods and keep your team’s attendance up to date.', settings: 'Manage your workspace, people, and account access.'} as Record<string,string>)[this.view];
  protected readonly items = signal<Employee[]>([]); protected readonly departments = signal<Department[]>([]);
  protected readonly loading = signal(false); protected readonly busy = signal(false); protected readonly error = signal('');
  protected readonly refresh = signal(0);
  protected readonly hierarchyAllowed = computed(() => this.workspace.hrm.organization()?.role !== 'Employee');
  protected departmentName = ''; protected editingDepartment: string | null = null; protected organizationName = '';
  constructor() {
    effect(cleanup => {
      const org = this.workspace.hrm.organization(); this.refresh();
      this.items.set([]); this.departments.set([]); this.error.set(''); this.loading.set(false);
      this.departmentName = ''; this.editingDepartment = null;
      if (!org || this.view === 'settings' || (this.view === 'hierarchy' && !this.hierarchyAllowed())) return;
      this.loading.set(true);
      const request = forkJoin({employees: this.employees.getAll(), departments: this.workspace.hrm.departments()}).subscribe({
        next: result => {this.items.set(result.employees); this.departments.set(result.departments); this.loading.set(false);},
        error: e => {this.error.set(readApiError(e, 'Unable to load workspace data.')); this.loading.set(false);},
      }); cleanup(() => request.unsubscribe());
    });
  }
  protected reload() {this.refresh.update(v => v + 1);}
  protected count(id: string) {return this.items().filter(e => e.departmentId === id).length;}
  protected department(id: string) {return this.departments().find(d => d.id === id)?.name ?? 'Unassigned';}
  protected manager(id: string | null) {
    if (!id) return 'No reporting manager';
    const employee = this.items().find(e => e.id === id);
    return employee ? employee.firstName + ' ' + employee.lastName : 'Manager outside your view';
  }
  protected edit(department: Department) {this.departmentName = department.name; this.editingDepartment = department.id; document.getElementById('department-name')?.focus();}
  protected saveDepartment() {
    this.busy.set(true); this.error.set('');
    const request: Observable<unknown> = this.editingDepartment ? this.workspace.hrm.renameDepartment(this.editingDepartment, this.departmentName) : this.workspace.hrm.createDepartment(this.departmentName);
    request.subscribe({next: () => {this.busy.set(false); this.reload();}, error: e => {this.busy.set(false); this.error.set(readApiError(e, 'Unable to save department.'));}});
  }
  protected deleteDepartment(department: Department) {
    if (!window.confirm(`Delete ${department.name}? Departments with employees cannot be deleted.`)) return;
    this.busy.set(true);
    this.workspace.hrm.deleteDepartment(department.id).subscribe({next: () => {this.busy.set(false); this.reload();}, error: e => {this.busy.set(false); this.error.set(readApiError(e, 'Unable to delete department.'));}});
  }
  protected createOrganization() {
    this.busy.set(true); this.error.set('');
    this.workspace.hrm.createOrganization(this.organizationName).subscribe({next: organization => {this.busy.set(false); this.organizationName = ''; this.workspace.hrm.organization.set(organization); this.workspace.refresh();}, error: e => {this.busy.set(false); this.error.set(readApiError(e, 'Unable to create organization.'));}});
  }
  protected renameOrganization() {
    const name = window.prompt('Organization name', this.workspace.hrm.organization()?.name);
    if (!name?.trim()) return;
    this.workspace.hrm.renameOrganization(name).subscribe({next: () => this.workspace.refresh(), error: e => this.error.set(readApiError(e, 'Unable to rename organization.'))});
  }
}
