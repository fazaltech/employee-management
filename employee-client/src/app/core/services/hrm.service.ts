import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface Organization { id: string; name: string; role: string; status: string; }
export interface Department { id: string; name: string; }
export interface Membership { userId: string; fullName: string; email: string; role: string; isActive: boolean; emailVerificationPending: boolean; }
export interface Attendance { id: string; employeeId: string; workDate: string; status: string; checkInUtc: string | null; checkOutUtc: string | null; notes: string; }
export interface OwnerAccount { userId: string; fullName: string; email: string; organizationId: string; organizationName: string; }

@Injectable({ providedIn: 'root' })
export class HrmService {
  private readonly http = inject(HttpClient);
  readonly organization = signal<Organization | null>(null);
  get url() { return `${environment.apiUrl}/organizations/${this.organization()?.id}`; }
  organizations() { return this.http.get<Organization[]>(`${environment.apiUrl}/organizations`); }
  createOrganization(name: string) { return this.http.post<Organization>(`${environment.apiUrl}/organizations`, { name }); }
  renameOrganization(name: string) { return this.http.put<void>(this.url, { name }); }
  departments() { return this.http.get<Department[]>(`${this.url}/departments`); }
  createDepartment(name: string) { return this.http.post<Department>(`${this.url}/departments`, { name }); }
  renameDepartment(id: string, name: string) { return this.http.put<void>(`${this.url}/departments/${id}`, { name }); }
  deleteDepartment(id: string) { return this.http.delete<void>(`${this.url}/departments/${id}`); }
  memberships() { return this.http.get<Membership[]>(`${this.url}/memberships`); }
  updateMembership(m: Membership) { return this.http.put<void>(`${this.url}/memberships/${m.userId}`, { role: m.role, isActive: m.isActive }); }
  createAccount(fullName: string, email: string, role: string, password?: string) {
    return this.http.post(`${this.url}/accounts`, { fullName, email, role, ...(password ? { password } : {}) });
  }
  updateAccount(userId: string, fullName: string, password?: string) {
    return this.http.put(`${this.url}/accounts/${userId}`, { fullName, ...(password ? { password } : {}) });
  }
  accountOptions() { return this.http.get<{isPlatformAdmin: boolean; emailEnabled: boolean; requireConfirmedEmail: boolean}>(`${environment.apiUrl}/auth/account-options`); }
  platformOrganizations() { return this.http.get<Organization[]>(`${environment.apiUrl}/platform/organizations`); }
  owners() { return this.http.get<OwnerAccount[]>(`${environment.apiUrl}/platform/owners`); }
  createOwner(organizationId: string, fullName: string, email: string, password?: string) {
    return this.http.post(`${environment.apiUrl}/platform/organizations/${organizationId}/owners`, { fullName, email, role: 'OrganizationOwner', ...(password ? { password } : {}) });
  }
  updateOwner(owner: OwnerAccount, password?: string) {
    return this.http.put(`${environment.apiUrl}/platform/organizations/${owner.organizationId}/owners/${owner.userId}`, { fullName: owner.fullName, ...(password ? { password } : {}) });
  }
  attendance(employeeId: string, from: string, to: string) { return this.http.get<Attendance[]>(`${this.url}/attendance`, { params: { employeeId, from, to } }); }
  checkIn(employeeId: string) { return this.http.post<Attendance>(`${this.url}/attendance/check-in`, { employeeId }); }
  attendanceState(employeeId: string) { return this.http.get<{openPeriod: Attendance | null; todayRecord: Attendance | null}>(`${this.url}/attendance/${employeeId}/state`); }
  checkOut(employeeId: string, date: string) { return this.http.post<Attendance>(`${this.url}/attendance/${employeeId}/${date}/check-out`, {}); }
  correct(employeeId: string, date: string, value: { status: string; checkInUtc: string | null; checkOutUtc: string | null; notes: string }) {
    return this.http.put<Attendance>(`${this.url}/attendance/${employeeId}/${date}`, value);
  }
  setupEmail(email: string) { return this.http.post<{message: string}>(`${environment.apiUrl}/auth/password-setup/request`, { email }); }
  setupPassword(userId: string, token: string, password: string) { return this.http.post<void>(`${environment.apiUrl}/auth/password-setup`, { userId, token, password }); }
}
