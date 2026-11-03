import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { HrmService } from './hrm.service';
import { Employee, EmployeePayload } from '../models/employee.model';

@Injectable({ providedIn: 'root' })
export class EmployeeService {
  private readonly http = inject(HttpClient);
  private readonly hrm = inject(HrmService);
  private get url() { return `${this.hrm.url}/employees`; }

  getAll(): Observable<Employee[]> {
    return this.http.get<Employee[]>(this.url);
  }

  getById(id: string): Observable<Employee> {
    return this.http.get<Employee>(`${this.url}/${id}`);
  }

  create(employee: EmployeePayload, creationKey: string): Observable<Employee> {
    return this.http.post<Employee>(this.url, employee, { headers: { 'Idempotency-Key': creationKey } });
  }

  update(id: string, employee: EmployeePayload): Observable<Employee> {
    return this.http.put<Employee>(`${this.url}/${id}`, employee);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.url}/${id}`);
  }
}
