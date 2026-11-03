using EmployeeApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EmployeeApi.Data;

/// <summary>Creates platform credentials only; never creates organizations or memberships.</summary>
public sealed class PlatformAdminSeeder(ApplicationDbContext db, UserManager<ApplicationUser> users,
    RoleManager<IdentityRole> roles, IConfiguration configuration, IHostEnvironment environment)
{
    public const string UserId = "0bef329f-14d4-43c3-94cf-b8b7878fde81";
    public const string Email = "platform@peopleops.com";
    public const string Role = "PlatformAdmin";

    public async Task ValidateAsync(CancellationToken ct)
    {
        if (!environment.IsDevelopment()) return;
        var existing = await InspectAsync(ct);
        if (existing is not null) return; // Existing credentials are never read, validated, or reset.
        var candidate = NewUser();
        var password = InitialPassword();
        var errors = new List<IdentityError>();
        foreach (var validator in users.PasswordValidators)
            errors.AddRange((await validator.ValidateAsync(users, candidate, password)).Errors);
        if (errors.Count > 0)
            throw new DatabaseSetupException("PLATFORM_ADMIN_INITIAL_PASSWORD does not meet the configured Identity password policy. Validation codes: "
                + string.Join(", ", errors.Select(x => x.Code)) + ". Configure a compliant secret and retry.");
    }

    public async Task SeedAsync(CancellationToken ct)
    {
        if (!environment.IsDevelopment()) return;
        // The SQLite provider begins an immediate transaction, serializing concurrent seeders.
        // Roles, new users, and role assignment commit together or are rolled back together.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.ChangeTracker.Clear(); // Do not reuse entities tracked by the earlier preflight.
        var user = await InspectAsync(ct); // Recheck conflicts within the transaction.
        var password = user is null ? InitialPassword() : null;
        if (!await roles.RoleExistsAsync(Role))
            EnsureSucceeded(await roles.CreateAsync(new IdentityRole(Role)), "creating PlatformAdmin role");
        if (user is null)
        {
            user = NewUser();
            EnsureSucceeded(await users.CreateAsync(user, password!), "creating platform administrator");
        }
        else if (user.Email != Email || user.UserName != Email ||
            user.NormalizedEmail != users.NormalizeEmail(Email) || user.NormalizedUserName != users.NormalizeName(Email))
        {
            // Only canonicalize the matching account's identity fields through UserManager.
            // No password/security-stamp reset, EmailConfirmed change, or profile overwrite.
            user.Email = Email;
            user.UserName = Email;
            EnsureSucceeded(await users.UpdateAsync(user), "normalizing platform administrator identity");
        }
        if (!await users.IsInRoleAsync(user, Role))
            EnsureSucceeded(await users.AddToRoleAsync(user, Role), "assigning PlatformAdmin role");
        await transaction.CommitAsync(ct);
    }

    private async Task<ApplicationUser?> InspectAsync(CancellationToken ct)
    {
        var byId = await users.FindByIdAsync(UserId);
        var byEmail = await users.FindByEmailAsync(Email);
        var byName = await users.FindByNameAsync(Email);
        var normalizedEmail = users.NormalizeEmail(Email);
        var normalizedName = users.NormalizeName(Email);
        // Also detect legacy rows whose normalization fields were not populated correctly.
        if (await db.Users.AsNoTracking().AnyAsync(x => x.Id != UserId &&
            (x.NormalizedEmail == normalizedEmail || (x.Email != null && x.Email.ToUpper() == normalizedEmail)), ct))
            throw new DatabaseSetupException("Platform administrator seed conflict: the configured email belongs to a different user ID. No account IDs or passwords were changed.");
        if (await db.Users.AsNoTracking().AnyAsync(x => x.Id != UserId &&
            (x.NormalizedUserName == normalizedName || (x.UserName != null && x.UserName.ToUpper() == normalizedName)), ct))
            throw new DatabaseSetupException("Platform administrator seed conflict: the required username belongs to another account. Resolve it explicitly before retrying.");
        if (byEmail is not null && byEmail.Id != UserId)
            throw new DatabaseSetupException("Platform administrator seed conflict: the configured email belongs to a different user ID. No account IDs or passwords were changed.");
        if (byId is not null && users.NormalizeEmail(byId.Email ?? "") != users.NormalizeEmail(Email))
            throw new DatabaseSetupException("Platform administrator seed conflict: the specified user ID belongs to a different email. No account IDs or passwords were changed.");
        if (byName is not null && byName.Id != UserId)
            throw new DatabaseSetupException("Platform administrator seed conflict: the required username belongs to another account. Resolve it explicitly before retrying.");
        // Do not silently remove existing customer access or orphan an organization.
        if (await db.OrganizationMemberships.AnyAsync(x => x.UserId == UserId, ct))
            throw new DatabaseSetupException("Platform administrator seed blocked: this account has existing organization memberships. Transfer ownership to a different existing account, then explicitly remove this account's memberships before retrying. The seeder never creates or deletes customer memberships.");
        return byId;
    }

    private string InitialPassword()
    {
        var password = Environment.GetEnvironmentVariable("PLATFORM_ADMIN_INITIAL_PASSWORD")
            ?? configuration["PlatformAdmin:InitialPassword"];
        if (string.IsNullOrEmpty(password))
            throw new DatabaseSetupException("First-time platform administrator setup requires PLATFORM_ADMIN_INITIAL_PASSWORD or the PlatformAdmin:InitialPassword user secret. No default password is provided. Existing matching accounts do not require this secret.");
        return password;
    }

    private static ApplicationUser NewUser() => new()
    {
        Id = UserId, Email = Email, UserName = Email, FullName = "PlatformAdmin", MustChangePassword = true, CreatedAtUtc = DateTime.UtcNow
        // EmailConfirmed remains false; the sample-data configuration controls confirmation requirements.
    };

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
            throw new DatabaseSetupException($"Identity setup failed while {operation}. Validation codes: {string.Join(", ", result.Errors.Select(x => x.Code))}. No partial seed transaction was committed.");
    }
}

public sealed class DatabaseSetupException(string message) : Exception(message);
