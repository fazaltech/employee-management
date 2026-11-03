using EmployeeApi.DTOs.Employees;

namespace EmployeeApi.Tests;

public sealed class EmployeeMapperTests
{
    [Fact]
    public void ToEmployee_NormalizesFieldsAndInitializesDocumentMetadata()
    {
        var request = new EmployeeRequest
        {
            EmployeeNumber = " emp-001 ",
            FirstName = " Alex ",
            LastName = " Morgan ",
            Email = " ALEX@EXAMPLE.COM ",
            DepartmentId = " 507f1f77bcf86cd799439011 ",
            JobTitle = " Developer ",
            Salary = 85000m,
            HireDate = new DateTime(2026, 1, 15),
            IsActive = true
        };

        var employee = request.ToEmployee();

        Assert.Equal("EMP-001", employee.EmployeeNumber);
        Assert.Equal("Alex", employee.FirstName);
        Assert.Equal("Morgan", employee.LastName);
        Assert.Equal("alex@example.com", employee.Email);
        Assert.Equal("507f1f77bcf86cd799439011", employee.DepartmentId);
        Assert.Equal(DateTimeKind.Utc, employee.HireDate.Kind);
        Assert.NotEmpty(employee.Id);
        Assert.NotEqual(default, employee.CreatedAtUtc);
        Assert.True(employee.UpdatedAtUtc >= employee.CreatedAtUtc);
    }
}
