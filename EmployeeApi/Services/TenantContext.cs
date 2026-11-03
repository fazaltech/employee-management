using System.Security.Claims;
using EmployeeApi.Data;
using EmployeeApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace EmployeeApi.Services;

public sealed class TenantContext
{
    public string OrganizationId { get; internal set; } = "";
    public string UserId { get; internal set; } = "";
    public string Role { get; internal set; } = "";
    public bool IsHR => Role is OrganizationRoles.Owner or OrganizationRoles.HR;
}

public sealed record TenantRequirement(string[] Roles) : IAuthorizationRequirement;

public sealed class TenantAuthorizationHandler(ApplicationDbContext db, TenantContext tenant,
    IHttpContextAccessor accessor, IConfiguration configuration) : AuthorizationHandler<TenantRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, TenantRequirement requirement)
    {
        var http = accessor.HttpContext;
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var organizationId = http?.Request.RouteValues["organizationId"]?.ToString()
            ?? http?.Request.Headers["X-Organization-Id"].ToString();
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(organizationId)) return;
        if (await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId == userId && r.Name == "PlatformAdmin" select ur).AnyAsync()) return;
        var membership = await db.OrganizationMemberships.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.UserId == userId && x.IsActive);
        if (membership is null || !requirement.Roles.Contains(membership.Role) ||
            !await db.Organizations.AnyAsync(x => x.Id == organizationId && x.Status == "Active")) return;
        if (configuration.GetValue("Authentication:RequireConfirmedEmail", true) && membership.RequiresVerifiedEmail && !await db.Users.AnyAsync(x => x.Id == userId && x.EmailConfirmed)) return;
        tenant.OrganizationId = organizationId;
        tenant.UserId = userId;
        tenant.Role = membership.Role;
        context.Succeed(requirement);
    }
}
