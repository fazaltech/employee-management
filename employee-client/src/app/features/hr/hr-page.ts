import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute } from '@angular/router';
import { forkJoin, Observable, of } from 'rxjs';
import { WorkspaceService } from '../../core/services/workspace.service';
import { AuthService } from '../../core/services/auth.service';
import { EmployeeService } from '../../core/services/employee.service';
import { Employee } from '../../core/models/employee.model';
import { readApiError } from '../../core/api-error';
interface Decision {stage: string; approverUserId: string; approved: boolean; decidedAtUtc: string; comment: string;}
interface Leave {id: string; employeeId: string; from: string; to: string; reason: string; status: string; decisions: Decision[];}
interface Shift {id: string; name: string; startTime: string; endTime: string; breakMinutes: number; workingDays: number[]; timeZone: string;}
interface Assignment {id: string; employeeId: string; shiftId: string; effectiveFrom: string; effectiveTo: string | null;}
interface Salary {id: string; employeeId: string; amount: number; currency: string; effectiveFrom: string; status: string;}
interface Payroll {id: string; period: string; status: string; lines: {employeeId: string; amount: number; currency: string}[];}
interface Slip {id: string; employeeId: string; period: string; amount: number; currency: string;}
@Component({selector: 'app-hr-page', imports: [FormsModule, DatePipe], templateUrl: './hr-page.html', styleUrl: './hr-page.scss'})
export class HrPage {
  readonly workspace = inject(WorkspaceService); readonly auth = inject(AuthService);
  private readonly http = inject(HttpClient); private readonly employeesApi = inject(EmployeeService);
  readonly view = inject(ActivatedRoute).snapshot.data['view'] as string;
  readonly title = this.view === 'leaves' ? 'Leave requests' : this.view === 'shifts' ? 'Shifts & working hours' : 'Salaries & payroll';
  readonly loading = signal(false); readonly busy = signal(false); readonly error = signal(''); readonly message = signal(''); readonly refresh = signal(0);
  readonly employees = signal<Employee[]>([]); readonly leaves = signal<Leave[]>([]); readonly shifts = signal<Shift[]>([]); readonly assignments = signal<Assignment[]>([]);
  readonly salaries = signal<Salary[]>([]); readonly payroll = signal<Payroll[]>([]); readonly slips = signal<Slip[]>([]);
  from = ''; to = ''; reason = ''; comment = ''; employeeId = ''; shiftId = ''; effectiveFrom = ''; effectiveTo = '';
  shiftName = ''; startTime = '09:00'; endTime = '17:00'; breakMinutes = 30; workingDays = '1,2,3,4,5'; editingShift = ''; editingAssignment = ''; editingSalary = '';
  amount = 0; currency = 'PKR'; period = ''; timeZone = 'UTC';
  constructor() {
    effect(cleanup => {
      const org = this.workspace.hrm.organization(); this.refresh();
      this.employees.set([]); this.leaves.set([]); this.shifts.set([]); this.assignments.set([]); this.salaries.set([]); this.payroll.set([]); this.slips.set([]); this.error.set('');
      this.employeeId = ''; this.shiftId = ''; this.editingShift = ''; this.editingAssignment = ''; this.editingSalary = '';
      if (!org) { this.loading.set(false); return; }
      this.loading.set(true); const base = this.workspace.hrm.url;
      const request = forkJoin({
        employees: this.employeesApi.getAll(),
        leaves: this.view === 'leaves' ? this.http.get<Leave[]>(base + '/leaves') : of([] as Leave[]),
        shifts: this.view === 'shifts' ? this.http.get<Shift[]>(base + '/shifts') : of([] as Shift[]),
        assignments: this.view === 'shifts' ? this.http.get<Assignment[]>(base + '/shift-assignments') : of([] as Assignment[]),
        settings: this.view === 'shifts' ? this.http.get<{timeZone: string}>(base + '/hr-settings') : of({timeZone: 'UTC'}),
        salaries: this.view === 'payroll' ? this.http.get<Salary[]>(base + '/salaries') : of([] as Salary[]),
        payroll: this.view === 'payroll' && this.workspace.isHR() ? this.http.get<Payroll[]>(base + '/payroll') : of([] as Payroll[]),
        slips: this.view === 'payroll' ? this.http.get<Slip[]>(base + '/payslips') : of([] as Slip[]),
      }).subscribe({next: r => {this.employees.set(r.employees); this.leaves.set(r.leaves); this.shifts.set(r.shifts); this.assignments.set(r.assignments); this.salaries.set(r.salaries); this.payroll.set(r.payroll); this.slips.set(r.slips); this.timeZone = r.settings.timeZone; this.loading.set(false);}, error: e => {this.error.set(readApiError(e, 'Unable to load HR records.')); this.loading.set(false);}});
      cleanup(() => request.unsubscribe());
    });
  }
  reload() { this.refresh.update(x => x + 1); }
  name(id: string) {const e = this.employees().find(x => x.id === id); return e ? e.firstName + ' ' + e.lastName : id;}
  shiftLabel(id: string) {return this.shifts().find(x => x.id === id)?.name ?? id;}
  canSubmit() {return this.employees().some(x => x.applicationUserId === this.auth.user()?.id);}
  canDecide(row: Leave) {
    const role = this.workspace.hrm.organization()?.role;
    const self = this.employees().find(x => x.applicationUserId === this.auth.user()?.id);
    if (row.employeeId === self?.id || row.decisions.some(x => x.approverUserId === this.auth.user()?.id)) return false;
    return row.status === 'PendingOwner' && role === 'OrganizationOwner' || row.status === 'PendingHR' && role === 'HRAdmin' || row.status === 'PendingManager' && role === 'Manager' && this.employees().find(x => x.id === row.employeeId)?.managerEmployeeId === self?.id;
  }
  run(request: Observable<unknown>) {
    if (this.busy()) return; const org = this.workspace.hrm.organization()?.id;
    this.busy.set(true); this.error.set(''); this.message.set('');
    request.subscribe({next: () => {this.busy.set(false); if (org === this.workspace.hrm.organization()?.id) {this.message.set('Changes saved.'); this.reload();}}, error: e => {this.busy.set(false); if (org === this.workspace.hrm.organization()?.id) this.error.set(readApiError(e, 'Unable to save changes.'));}});
  }
  submitLeave() {this.run(this.http.post(this.workspace.hrm.url + '/leaves', {from: this.from, to: this.to, reason: this.reason}));}
  decide(row: Leave, approved: boolean) {this.run(this.http.post(this.workspace.hrm.url + '/leaves/' + row.id + '/decision', {approved, comment: this.comment}));}
  editShift(row: Shift) {this.editingShift = row.id; this.shiftName = row.name; this.startTime = row.startTime; this.endTime = row.endTime; this.breakMinutes = row.breakMinutes; this.workingDays = row.workingDays.join(',');}
  saveShift() {const body = {name: this.shiftName, startTime: this.startTime, endTime: this.endTime, breakMinutes: this.breakMinutes, workingDays: this.workingDays.split(',').map(Number)}; this.run(this.editingShift ? this.http.put(this.workspace.hrm.url + '/shifts/' + this.editingShift, body) : this.http.post(this.workspace.hrm.url + '/shifts', body));}
  saveSettings() {this.run(this.http.put(this.workspace.hrm.url + '/hr-settings', {timeZone: this.timeZone}));}
  editAssignment(a: Assignment) {this.editingAssignment = a.id; this.employeeId = a.employeeId; this.shiftId = a.shiftId; this.effectiveFrom = a.effectiveFrom; this.effectiveTo = a.effectiveTo ?? '';}
  assign() {const body = {employeeId: this.employeeId, shiftId: this.shiftId, effectiveFrom: this.effectiveFrom, effectiveTo: this.effectiveTo || null}; this.run(this.editingAssignment ? this.http.put(this.workspace.hrm.url + '/shift-assignments/' + this.editingAssignment, body) : this.http.post(this.workspace.hrm.url + '/shift-assignments', body));}
  editSalary(s: Salary) {this.editingSalary = s.id; this.employeeId = s.employeeId; this.amount = s.amount; this.currency = s.currency; this.effectiveFrom = s.effectiveFrom;}
  salary() {const body = {employeeId: this.employeeId, amount: this.amount, currency: this.currency, effectiveFrom: this.effectiveFrom}; this.run(this.editingSalary ? this.http.put(this.workspace.hrm.url + '/salaries/' + this.editingSalary, body) : this.http.post(this.workspace.hrm.url + '/salaries', body));}
  deleteDraft(id: string) {this.run(this.http.delete(this.workspace.hrm.url + '/payroll/' + id));}
  approveSalary(id: string) {this.run(this.http.post(this.workspace.hrm.url + '/salaries/' + id + '/approve', {}));}
  draft() {this.run(this.http.post(this.workspace.hrm.url + '/payroll', {period: this.period}));}
  approvePayroll(id: string) {this.run(this.http.post(this.workspace.hrm.url + '/payroll/' + id + '/approve', {}));}
}
