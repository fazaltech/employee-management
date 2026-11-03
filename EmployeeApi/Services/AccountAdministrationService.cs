using EmployeeApi.Data;
using EmployeeApi.Infrastructure;
using EmployeeApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EmployeeApi.Services;

public sealed class AccountAdministrationService(ApplicationDbContext db, UserManager<ApplicationUser> users,
    IConfiguration configuration)
{
    public static int Rank(string role) => role switch
    {
        OrganizationRoles.Owner => 4, OrganizationRoles.HR => 3,
        OrganizationRoles.Manager => 2, OrganizationRoles.Employee => 1, _ => 0
    };

    public static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new ApiException(400, string.Join(" ", result.Errors.Select(x => x.Description)));
    }

    public async Task ValidatePassword(ApplicationUser user, string? password, bool creating)
    {
        if (password is null)
        {
            if (creating && !configuration.GetValue("Notifications:EmailEnabled", true))
                throw new ApiException(400, "An initial password is required while email delivery is disabled.");
            return;
        }
        if (password.Length == 0) throw new ApiException(400, "Supply a new password or omit the password field to keep the current password.");
        var errors = new List<IdentityError>();
        foreach (var validator in users.PasswordValidators)
            errors.AddRange((await validator.ValidateAsync(users, user, password)).Errors);
        if (errors.Count > 0) EnsureSucceeded(IdentityResult.Failed(errors.ToArray()));
    }

    public async Task EnsureTargetAllowed(ApplicationUser user, string organizationId, string actorRole,
        bool changeCredentials, bool platform, CancellationToken ct)
    {
        if (await users.IsInRoleAsync(user, "PlatformAdmin"))
            throw new ApiException(403, "Platform administrator accounts cannot be managed through organization account flows.");
        var membership = await db.OrganizationMemberships.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.UserId == user.Id, ct);
        if (membership is null) throw new ApiException(404, "Account not found in this organization.");
        if (platform)
        {
            if (membership.Role != OrganizationRoles.Owner) throw new ApiException(403, "This platform operation manages organization owners only.");
        }
        else
        {
            if (Rank(membership.Role) == 0 || Rank(membership.Role) > Rank(actorRole))
                throw new ApiException(403, "The target account is above your authority.");
            if (changeCredentials && await db.OrganizationMemberships.AnyAsync(x => x.UserId == user.Id && x.OrganizationId != organizationId, ct))
                throw new ApiException(403, "This account is shared with another organization. Its credentials and profile cannot be changed by an organization administrator.");
        }
    }

    public async Task SetPassword(ApplicationUser user, string? password)
    {
        if (password is null) return;
        await ValidatePassword(user, password, false);
        var token = await users.GeneratePasswordResetTokenAsync(user);
        EnsureSucceeded(await users.ResetPasswordAsync(user, token, password));
    }
}
