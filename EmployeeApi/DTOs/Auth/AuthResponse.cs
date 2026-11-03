namespace EmployeeApi.DTOs.Auth;

public sealed record AuthResponse(
    string Token,
    DateTime ExpiresAtUtc,
    UserResponse User);

public sealed record UserResponse(
    string Id,
    string FullName,
    string Email,
    bool MustChangePassword = false);
