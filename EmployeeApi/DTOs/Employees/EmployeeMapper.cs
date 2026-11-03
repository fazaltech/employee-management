using EmployeeApi.Models;
using MongoDB.Bson;

namespace EmployeeApi.DTOs.Employees;

public static class EmployeeMapper
{
    public static Employee ToEmployee(this EmployeeRequest request)
    {
        var now = DateTime.UtcNow;
        var employee = new Employee
        {
            Id = ObjectId.GenerateNewId().ToString(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        request.ApplyTo(employee);
        return employee;
    }

    public static void ApplyTo(this EmployeeRequest request, Employee employee)
    {
        employee.EmployeeNumber = request.EmployeeNumber.Trim().ToUpperInvariant();
        employee.FirstName = request.FirstName.Trim();
        employee.LastName = request.LastName.Trim();
        employee.Email = request.Email.Trim().ToLowerInvariant();
        employee.DepartmentId = ObjectId.TryParse(request.DepartmentId.Trim(), out var departmentId) ? departmentId.ToString() : request.DepartmentId.Trim();
        employee.ManagerEmployeeId = string.IsNullOrWhiteSpace(request.ManagerEmployeeId) ? null
            : ObjectId.TryParse(request.ManagerEmployeeId.Trim(), out var managerId) ? managerId.ToString() : request.ManagerEmployeeId.Trim();
        employee.JobTitle = request.JobTitle.Trim();
        employee.Salary = request.Salary;
        employee.HireDate = DateTime.SpecifyKind(request.HireDate.Date, DateTimeKind.Utc);
        employee.IsActive = request.IsActive;
        employee.UpdatedAtUtc = DateTime.UtcNow;
    }
}
