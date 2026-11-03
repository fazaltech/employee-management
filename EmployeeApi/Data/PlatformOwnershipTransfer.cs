using EmployeeApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EmployeeApi.Data;

/// <summary>Explicit operator action, never invoked during ordinary startup or account seeding.</summary>
public sealed class PlatformOwnershipTransfer(ApplicationDbContext db, UserManager<ApplicationUser> users,
    IConfiguration configuration)
{
    public async Task TransferAsync(CancellationToken ct)
    {
        var replacements = configuration.GetSection("PlatformAdmin:OwnershipTransfers").Get<Dictionary<string, string>>() ?? [];
        if (replacements.Count == 0)
            throw new DatabaseSetupException("Ownership transfer requires PlatformAdmin:OwnershipTransfers:<organization-id>=<different-existing-user-id> for every membership. No memberships were changed.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var platformUser = await users.FindByIdAsync(PlatformAdminSeeder.UserId);
        if (platformUser is null || users.NormalizeEmail(platformUser.Email ?? "") != users.NormalizeEmail(PlatformAdminSeeder.Email))
            throw new DatabaseSetupException("The platform account ID/email must match before ownership can be transferred. No memberships were changed.");
        var current = await db.OrganizationMemberships.Where(x => x.UserId == PlatformAdminSeeder.UserId).ToListAsync(ct);
        if (current.Count == 0) return; // Idempotent retry after the previous transfer committed.
        if (current.Any(m => !replacements.ContainsKey(m.OrganizationId)) || replacements.Keys.Any(k => current.All(m => m.OrganizationId != k)))
            throw new DatabaseSetupException("Provide exactly one replacement user ID for every organization membership held by the platform account. No memberships were changed.");
        foreach (var membership in current)
        {
            var replacementId = replacements[membership.OrganizationId];
            if (replacementId == PlatformAdminSeeder.UserId)
                throw new DatabaseSetupException("Ownership replacement must be a different account from the platform administrator. No memberships were changed.");
            var replacement = await users.FindByIdAsync(replacementId);
            if (replacement is null || await users.IsInRoleAsync(replacement, PlatformAdminSeeder.Role))
                throw new DatabaseSetupException("Every ownership replacement must be an existing, non-PlatformAdmin user ID. No memberships were changed.");
            if (configuration.GetValue("Authentication:RequireConfirmedEmail", true) && !replacement.EmailConfirmed)
                throw new DatabaseSetupException("A replacement owner must have confirmed email when email confirmation is required. No memberships were changed.");
            var target = await db.OrganizationMemberships.FindAsync([membership.OrganizationId, replacementId], ct);
            if (target is null)
            {
                target = new OrganizationMembership { OrganizationId = membership.OrganizationId, UserId = replacementId, RequiresVerifiedEmail = true };
                db.OrganizationMemberships.Add(target);
            }
            target.Role = OrganizationRoles.Owner;
            target.IsActive = true;
            db.OrganizationMemberships.Remove(membership);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
