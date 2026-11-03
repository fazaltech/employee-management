using System.ComponentModel.DataAnnotations;
using EmployeeApi.Models;
using EmployeeApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EmployeeApi.Controllers;
public sealed record PasswordSetupEmailRequest([Required, EmailAddress, StringLength(150)] string Email);
public sealed record PasswordSetupRequest([Required] string UserId, [Required] string Token, [Required, StringLength(128, MinimumLength = 8)] string Password);

[ApiController, AllowAnonymous, Route("api/auth/password-setup"), EnableRateLimiting("password-setup")]
public sealed class PasswordSetupController(PasswordSetupService setup, UserManager<ApplicationUser> users, IConfiguration configuration) : ControllerBase
{
    [HttpPost("request")]
    public async Task<IActionResult> RequestEmail(PasswordSetupEmailRequest request)
    {
        await setup.Send(request.Email);
        return Accepted(value: new { message = configuration.GetValue("Notifications:EmailEnabled", true) ? "If an account exists, a password setup email has been sent." : "Email delivery is disabled. Contact your authorized account administrator to set a password." });
    }
    [HttpPost]
    public async Task<IActionResult> Complete(PasswordSetupRequest request)
    {
        var user = await users.FindByIdAsync(request.UserId);
        if (user is null) return BadRequest(new ProblemDetails { Title = "Invalid or expired password setup link." });
        var result = await users.ResetPasswordAsync(user, request.Token, request.Password);
        if (!result.Succeeded) return ValidationProblem(new ValidationProblemDetails(result.Errors.GroupBy(e => e.Code).ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray())));
        user.EmailConfirmed = true;
        if (!(await users.UpdateAsync(user)).Succeeded)
            return Conflict(new ProblemDetails { Title = "The account changed. Request a new setup link to finish email verification." });
        return NoContent();
    }
}
