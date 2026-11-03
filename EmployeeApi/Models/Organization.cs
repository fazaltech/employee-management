namespace EmployeeApi.Models;

public static class OrganizationRoles
{
    public const string Owner = "OrganizationOwner";
    public const string HR = "HRAdmin";
    public const string Manager = "Manager";
    public const string Employee = "Employee";
    public static readonly string[] All = [Owner, HR, Manager, Employee];
}

public sealed class Organization
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Status { get; set; } = "Active";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public long WriteVersion { get; set; }
}

public sealed class OrganizationMembership
{
    public string UserId { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string Role { get; set; } = OrganizationRoles.Employee;
    public bool IsActive { get; set; } = true;
    // Provisioned accounts must prove mailbox ownership before tenant access is granted.
    public bool RequiresVerifiedEmail { get; set; }
}
