using System.ComponentModel.DataAnnotations;

namespace EmployeeApi.DTOs.Employees;

public sealed class EmployeeRequest : IValidatableObject
{
    [Required, StringLength(30)]
    public string EmployeeNumber { get; init; } = string.Empty;

    [Required, StringLength(80)]
    public string FirstName { get; init; } = string.Empty;

    [Required, StringLength(80)]
    public string LastName { get; init; } = string.Empty;

    [Required, EmailAddress, StringLength(150)]
    public string Email { get; init; } = string.Empty;

    [StringLength(100)]
    public string DepartmentId { get; init; } = string.Empty;

    [Required, StringLength(100)]
    public string JobTitle { get; init; } = string.Empty;

    [StringLength(24)]
    public string? ManagerEmployeeId { get; init; }
    public bool ProvisionAppAccess { get; init; }
    [StringLength(128)]
    public string? Password { get; init; }

    [Range(0, 1_000_000_000)]
    public decimal Salary { get; init; }

    public DateTime HireDate { get; init; }

    public bool IsActive { get; init; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (HireDate.Year is < 1900 or > 2100)
            yield return new ValidationResult("Supply a hire date between 1900 and 2100.", [nameof(HireDate)]);
        if (ProvisionAppAccess && !IsActive)
            yield return new ValidationResult("App access can only be provisioned for an active employee.", [nameof(ProvisionAppAccess)]);
        if (Password != null && !ProvisionAppAccess)
            yield return new ValidationResult("Select app access when supplying a password.", [nameof(Password)]);
    }
}
