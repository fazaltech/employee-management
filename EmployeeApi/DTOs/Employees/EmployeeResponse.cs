using EmployeeApi.Models;
namespace EmployeeApi.DTOs.Employees;

public sealed record EmployeeResponse(string Id, string OrganizationId, string EmployeeNumber,
    string FirstName, string LastName, string Email, string DepartmentId, string? ManagerEmployeeId,
    string JobTitle, decimal? Salary, DateTime HireDate, bool IsActive, string? ApplicationUserId,
    string AppAccessStatus, DateTime CreatedAtUtc, DateTime UpdatedAtUtc)
{
    public static EmployeeResponse From(Employee e, bool isHR) => new(e.Id, e.OrganizationId,
        e.EmployeeNumber, e.FirstName, e.LastName, e.Email, e.DepartmentId, e.ManagerEmployeeId,
        e.JobTitle, isHR ? e.Salary : null, e.HireDate, e.IsActive, e.ApplicationUserId,
        e.ApplicationUserId != null ? "Linked" : e.ProvisioningUserId != null ? "Pending" : "None", e.CreatedAtUtc, e.UpdatedAtUtc);
}
