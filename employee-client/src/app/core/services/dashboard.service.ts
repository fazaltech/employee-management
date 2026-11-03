import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface DashboardData {
  generatedAtUtc: string; scope: string; totalEmployees: number; activeEmployees: number; todayAttendance: number; pendingActions: number;
  weeklyAttendance: {date: string; present: number; absent: number; leave: number}[];
  departments: {id: string; name: string; employees: number}[];
  recentAttendance: {employeeId: string; employeeName: string; department: string; workDate: string; checkInUtc: string | null; checkOutUtc: string | null; status: string}[];
  actions: {label: string; count: number; destination: string}[];
}
@Injectable({providedIn: 'root'})
export class DashboardService {
  private readonly http = inject(HttpClient);
  load(organizationId: string) { return this.http.get<DashboardData>(`${environment.apiUrl}/organizations/${organizationId}/dashboard`); }
}
