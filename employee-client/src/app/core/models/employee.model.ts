export interface Employee {
  id: string;
  employeeNumber: string;
  firstName: string;
  lastName: string;
  email: string;
  departmentId: string;
  managerEmployeeId: string | null;
  organizationId: string;
  applicationUserId: string | null;
  appAccessStatus: string;
  jobTitle: string;
  salary: number | null;
  hireDate: string;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export type EmployeePayload = Omit<Employee, 'id' | 'createdAtUtc' | 'updatedAtUtc' | 'organizationId' | 'applicationUserId' | 'appAccessStatus'> & { provisionAppAccess: boolean; password?: string };
