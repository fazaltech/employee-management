using System.Security.Claims;
using EmployeeApi.DTOs.Auth;
using EmployeeApi.Models;
using EmployeeApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EmployeeApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController(
    UserManager<ApplicationUser> userManager,
    IJwtTokenService tokenService, IConfiguration configuration) : ControllerBase
{
    [HttpGet("account-options"), Authorize]
    public async Task<IActionResult> AccountOptions()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();
        return Ok(new {
            IsPlatformAdmin = await userManager.IsInRoleAsync(user, "PlatformAdmin"),
            RequireConfirmedEmail = configuration.GetValue("Authentication:RequireConfirmedEmail", true),
            EmailEnabled = configuration.GetValue("Notifications:EmailEnabled", true)
        });
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = new ApplicationUser
        {
            FullName = request.FullName.Trim(),
            Email = email,
            UserName = email,
            CreatedAtUtc = DateTime.UtcNow
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = result.Errors
                .GroupBy(error => error.Code)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.Description).ToArray());
            return ValidationProblem(new ValidationProblemDetails(errors));
        }

        if (configuration.GetValue("Authentication:RequireConfirmedEmail", true) && !user.EmailConfirmed)
            return Accepted(value: new { requiresEmailConfirmation = true, message = "Account created. Use the password setup link to verify your email before signing in." });
        var response = await tokenService.CreateAsync(user);
        return CreatedAtAction(nameof(Me), response);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Invalid credentials",
                Detail = "The email address or password is incorrect."
            });
        }

        if (configuration.GetValue("Authentication:RequireConfirmedEmail", true) && !await userManager.IsEmailConfirmedAsync(user))
            return StatusCode(403, new ProblemDetails { Status = 403, Title = "Email confirmation required", Detail = "Use password setup to verify your email before signing in." });
        return Ok(await tokenService.CreateAsync(user));
    }

    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    [HttpPost("change-password"), Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();
        if (request.NewPassword == request.CurrentPassword) return BadRequest(new { detail = "Choose a different password." });
        AccountAdministrationService.EnsureSucceeded(await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword));
        user.MustChangePassword = false;
        AccountAdministrationService.EnsureSucceeded(await userManager.UpdateAsync(user));
        return Ok(await tokenService.CreateAsync(user));
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserResponse>> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = userId is null ? null : await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Unauthorized();
        }

        return Ok(new UserResponse(user.Id, user.FullName, user.Email ?? string.Empty));
    }
}
