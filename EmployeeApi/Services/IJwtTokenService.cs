using EmployeeApi.DTOs.Auth;
using EmployeeApi.Models;

namespace EmployeeApi.Services;

public interface IJwtTokenService
{
    Task<AuthResponse> CreateAsync(ApplicationUser user);
}
