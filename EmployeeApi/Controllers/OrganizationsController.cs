using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using EmployeeApi.Data;
using EmployeeApi.Infrastructure;
using EmployeeApi.Models;
using EmployeeApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmployeeApi.Controllers;

public sealed record OrganizationRequest([Required, StringLength(160)] string Name);
public sealed record MembershipRequest([Required] string Role, bool IsActive);
public sealed record OrganizationStatusRequest([Required] string Status);

[ApiController, Authorize, Route("api/organizations")]
public sealed class OrganizationsController(ApplicationDbContext db, TenantContext tenant, TenantWriteLock writeLock, IConfiguration configuration,
    AccountAdministrationService accounts, Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> users) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await (
        from m in db.OrganizationMemberships.AsNoTracking()
        join o in db.Organizations on m.OrganizationId equals o.Id
        where m.UserId == User.FindFirstValue(ClaimTypes.NameIdentifier) && m.IsActive && o.Status == "Active"
        where !configuration.GetValue<bool>("Authentication:RequireConfirmedEmail", true) || !m.RequiresVerifiedEmail || db.Users.Any(u => u.Id == m.UserId && u.EmailConfirmed)
        select new { o.Id, o.Name, o.Status, m.Role, o.CreatedAtUtc, o.UpdatedAtUtc }).ToListAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create(OrganizationRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (!await db.Users.AnyAsync(x => x.Id == userId, ct)) return Unauthorized();
        var org = new Organization { Name = request.Name.Trim() };
        db.Organizations.Add(org);
        db.OrganizationMemberships.Add(new OrganizationMembership { OrganizationId = org.Id, UserId = userId, Role = OrganizationRoles.Owner });
        await db.SaveChangesAsync(ct); // Both records commit atomically.
        return Created($"/api/organizations/{org.Id}", new { org.Id, org.Name, org.Status, Role = OrganizationRoles.Owner });
    }

    [HttpPut("{organizationId}"), Authorize(Policy = "OrganizationOwner")]
    public async Task<IActionResult> Rename(string organizationId, OrganizationRequest request, CancellationToken ct)
    {
        await using var tx = await writeLock.Acquire(ct);
        await db.Organizations.Where(x => x.Id == tenant.OrganizationId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, request.Name.Trim()).SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow), ct);
        await tx.CommitAsync(ct);
        return NoContent();
    }

    [HttpGet("{organizationId}/memberships"), Authorize(Policy = "OrganizationOwner")]
    public async Task<IActionResult> Members(string organizationId, CancellationToken ct) => Ok(await (
        from m in db.OrganizationMemberships join u in db.Users on m.UserId equals u.Id
        where m.OrganizationId == tenant.OrganizationId
        select new { m.UserId, u.FullName, u.Email, m.Role, m.IsActive, EmailVerificationPending = configuration.GetValue<bool>("Authentication:RequireConfirmedEmail", true) && m.RequiresVerifiedEmail && !u.EmailConfirmed }).ToListAsync(ct));

    [HttpPut("{organizationId}/memberships/{userId}"), Authorize(Policy = "OrganizationOwner")]
    public async Task<IActionResult> UpdateMember(string organizationId, string userId, MembershipRequest request, CancellationToken ct)
    {
        if (!OrganizationRoles.All.Contains(request.Role)) throw new ApiException(400, "Invalid organization role.");
        await using var tx = await writeLock.Acquire(ct);
        var member = await db.OrganizationMemberships.FindAsync([tenant.OrganizationId, userId], ct);
        if (member is null) return NotFound();
        var target = await users.FindByIdAsync(userId);
        if (target is null) return NotFound();
        await accounts.EnsureTargetAllowed(target, tenant.OrganizationId, tenant.Role, false, false, ct);
        if (AccountAdministrationService.Rank(request.Role) > AccountAdministrationService.Rank(tenant.Role))
            throw new ApiException(403, "The requested role exceeds your authority.");
        if (configuration.GetValue("Authentication:RequireConfirmedEmail", true) && request.Role == OrganizationRoles.Owner && request.IsActive && member.RequiresVerifiedEmail
            && !await db.Users.AnyAsync(x => x.Id == userId && x.EmailConfirmed, ct))
            throw new ApiException(409, "The new owner must complete email verification first.");
        if (member.Role == OrganizationRoles.Owner && member.IsActive && (request.Role != OrganizationRoles.Owner || !request.IsActive)
            && await db.OrganizationMemberships.CountAsync(x => x.OrganizationId == tenant.OrganizationId && x.Role == OrganizationRoles.Owner && x.IsActive, ct) <= 1)
            throw new ApiException(409, "The organization must retain an active owner.");
        member.Role = request.Role;
        member.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return NoContent();
    }

    [HttpPut("{organizationId}/status"), Authorize(Policy = "PlatformAdmin")]
    public async Task<IActionResult> SetStatus(string organizationId, OrganizationStatusRequest request, CancellationToken ct)
    {
        if (request.Status is not ("Active" or "Suspended")) throw new ApiException(400, "Invalid organization status.");
        var changed = await db.Organizations.Where(x => x.Id == organizationId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, request.Status).SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow), ct);
        return changed == 0 ? NotFound() : NoContent();
    }
}
