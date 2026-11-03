using System.ComponentModel.DataAnnotations;

namespace EmployeeApi.DTOs.Auth;

public sealed class RegisterRequest
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string FullName { get; init; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 8)]
    public string Password { get; init; } = string.Empty;

    [Required, Compare(nameof(Password))]
    public string ConfirmPassword { get; init; } = string.Empty;
}
