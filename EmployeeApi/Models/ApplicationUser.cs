using Microsoft.AspNetCore.Identity;

namespace EmployeeApi.Models;

public sealed class ApplicationUser : IdentityUser
{
    public bool MustChangePassword { get; set; }

    public string FullName { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
