using System.ComponentModel.DataAnnotations;
using EmployeeApi.Data;
using EmployeeApi.Infrastructure;
using EmployeeApi.Models;
using EmployeeApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmployeeApi.Controllers;

public sealed class CreateAccountRequest
{
    [Required, StringLength(160)] public string FullName { get; init; } = "";
    [Required, EmailAddress, StringLength(150)] public string Email { get; init; } = "";
    [StringLength(128)] public string? Password { get; init; }
    [Required] public string Role { get; init; } = OrganizationRoles.Employee;
}
public sealed class UpdateAccountRequest
{
    [Required, StringLength(160)] public string FullName { get; init; } = "";
    [StringLength(128)] public string? Password { get; init; }
}

[ApiController]
public sealed class AccountsController(ApplicationDbContext db, UserManager<ApplicationUser> users,
    AccountAdministrationService accounts, TenantContext tenant, TenantWriteLock writeLock) : ControllerBase
{
    [HttpPost("api/organizations/{organizationId}/accounts"), Authorize(Policy = "OrganizationOwner")]
    public Task<IActionResult> Create(string organizationId, CreateAccountRequest request, CancellationToken ct) => CreateCore(organizationId, request, false, ct);

    [HttpPost("api/platform/organizations/{organizationId}/owners"), Authorize(Policy = "PlatformAdmin")]
    public Task<IActionResult> CreateOwner(string organizationId, CreateAccountRequest request, CancellationToken ct) => CreateCore(organizationId, request, true, ct);

    private async Task<IActionResult> CreateCore(string organizationId, CreateAccountRequest request, bool platform, CancellationToken ct)
    {
        if (!OrganizationRoles.All.Contains(request.Role)) throw new ApiException(403, "Only organization roles may be assigned through this endpoint.");
        var role = platform ? OrganizationRoles.Owner : request.Role;
        if (AccountAdministrationService.Rank(role) == 0 || (!platform && AccountAdministrationService.Rank(role) > AccountAdministrationService.Rank(tenant.Role)))
            throw new ApiException(403, "The requested role is above your authority or is not an organization role.");
        await using var tx = platform ? await db.Database.BeginTransactionAsync(ct) : await writeLock.Acquire(ct);
        await db.Organizations.Where(x => x.Id == organizationId).ExecuteUpdateAsync(s => s.SetProperty(x => x.WriteVersion, x => x.WriteVersion + 1), ct);
        if (!await db.Organizations.AnyAsync(x => x.Id == organizationId, ct)) return NotFound();
        var email = request.Email.Trim().ToLowerInvariant();
        if (await users.FindByEmailAsync(email) != null)
            throw new ApiException(409, "An account with this email already exists. Creation never overwrites an existing account or password.");
        var user = new ApplicationUser { FullName = request.FullName.Trim(), Email = email, UserName = email };
        await accounts.ValidatePassword(user, request.Password, true);
        AccountAdministrationService.EnsureSucceeded(request.Password is null
            ? await users.CreateAsync(user) : await users.CreateAsync(user, request.Password));
        db.OrganizationMemberships.Add(new OrganizationMembership { UserId = user.Id, OrganizationId = organizationId, Role = role, RequiresVerifiedEmail = true });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return StatusCode(201, new { user.Id, user.FullName, user.Email, OrganizationId = organizationId, Role = role });
    }

    [HttpPut("api/organizations/{organizationId}/accounts/{userId}"), Authorize(Policy = "OrganizationOwner")]
    public Task<IActionResult> Update(string organizationId, string userId, UpdateAccountRequest request, CancellationToken ct) => UpdateCore(organizationId, userId, request, false, ct);

    [HttpPut("api/platform/organizations/{organizationId}/owners/{userId}"), Authorize(Policy = "PlatformAdmin")]
    public Task<IActionResult> UpdateOwner(string organizationId, string userId, UpdateAccountRequest request, CancellationToken ct) => UpdateCore(organizationId, userId, request, true, ct);

    private async Task<IActionResult> UpdateCore(string organizationId, string userId, UpdateAccountRequest request, bool platform, CancellationToken ct)
    {
        await using var tx = platform ? await db.Database.BeginTransactionAsync(ct) : await writeLock.Acquire(ct);
        // Acquire the SQLite write lock before checking target membership or credentials.
        await db.Organizations.Where(x => x.Id == organizationId).ExecuteUpdateAsync(s => s.SetProperty(x => x.WriteVersion, x => x.WriteVersion + 1), ct);
        var user = await users.FindByIdAsync(userId);
        if (user is null) return NotFound();
        await accounts.EnsureTargetAllowed(user, organizationId, tenant.Role, true, platform, ct);
        await accounts.SetPassword(user, request.Password);
        user.FullName = request.FullName.Trim();
        AccountAdministrationService.EnsureSucceeded(await users.UpdateAsync(user));
        await tx.CommitAsync(ct);
        return Ok(new { user.Id, user.FullName, user.Email });
    }

    [HttpGet("api/platform/owners"), Authorize(Policy = "PlatformAdmin")]
    public async Task<IActionResult> Owners(CancellationToken ct) => Ok(await (
        from m in db.OrganizationMemberships join u in db.Users on m.UserId equals u.Id
        join o in db.Organizations on m.OrganizationId equals o.Id
        where m.Role == OrganizationRoles.Owner
        select new { UserId = u.Id, u.FullName, u.Email, OrganizationId = o.Id, OrganizationName = o.Name, m.IsActive }).ToListAsync(ct));

    [HttpGet("api/platform/organizations"), Authorize(Policy = "PlatformAdmin")]
    public async Task<IActionResult> Organizations(CancellationToken ct) => Ok(await db.Organizations.Select(o => new { o.Id, o.Name, o.Status }).ToListAsync(ct));
}
