using EmployeeApi.Data;
using EmployeeApi.Infrastructure;
using EmployeeApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;

namespace EmployeeApi.Services;

public sealed class EmployeeProvisioningService(HrmStore store, TenantContext tenant,
    TenantWriteLock writeLock, ApplicationDbContext db, UserManager<ApplicationUser> users,
    AccountAdministrationService accounts)
{
    public async Task<Employee> Provision(string employeeId, CancellationToken ct, string? password = null, bool updatePassword = false)
    {
        // Persist the intent before creating the account. A retry always uses the same employee.
        await using var transaction = await writeLock.Acquire(ct);
        var employee = await store.Employees.Find(x => x.OrganizationId == tenant.OrganizationId && x.Id == employeeId).FirstOrDefaultAsync(ct)
            ?? throw new ApiException(404, "Employee not found.");
        if (!employee.IsActive) throw new ApiException(409, "Activate the employee before provisioning access.");
        if (employee.ApplicationUserId != null)
        {
            if (password != null && updatePassword)
            {
                var linked = await users.FindByIdAsync(employee.ApplicationUserId) ?? throw new ApiException(409, "The linked account is unavailable.");
                await accounts.EnsureTargetAllowed(linked, tenant.OrganizationId, tenant.Role, true, false, ct);
                await accounts.SetPassword(linked, password);
                await transaction.CommitAsync(ct);
            }
            return employee;
        }
        var user = await users.FindByEmailAsync(employee.Email);
        if (user != null)
        {
            if (await users.IsInRoleAsync(user, "PlatformAdmin")) throw new ApiException(403, "Platform administrator accounts cannot be provisioned through organization employee flows.");
            if (password != null && (updatePassword || user.Id != employee.ProvisioningUserId))
            {
                if (!updatePassword) throw new ApiException(409, "This account already exists. Omit the initial password to link it without changing its credentials.");
                await accounts.EnsureTargetAllowed(user, tenant.OrganizationId, tenant.Role, true, false, ct);
                await accounts.SetPassword(user, password);
            }
            if (!await users.HasPasswordAsync(user)) await accounts.ValidatePassword(user, null, true);
        }
        employee.ProvisioningUserId ??= user?.Id ?? Guid.NewGuid().ToString();
        await store.Employees.UpdateOneAsync(x => x.OrganizationId == tenant.OrganizationId && x.Id == employee.Id,
            Builders<Employee>.Update.Set(x => x.ProvisioningUserId, employee.ProvisioningUserId), cancellationToken: ct);
        if (user is null)
        {
            user = new ApplicationUser { Id = employee.ProvisioningUserId, Email = employee.Email, UserName = employee.Email, FullName = $"{employee.FirstName} {employee.LastName}" };
            await accounts.ValidatePassword(user, password, true);
            AccountAdministrationService.EnsureSucceeded(password is null ? await users.CreateAsync(user) : await users.CreateAsync(user, password));
        }
        if (await store.Employees.Find(x => x.OrganizationId == tenant.OrganizationId && x.Id != employee.Id && x.ApplicationUserId == user.Id).AnyAsync(ct))
            throw new ApiException(409, "This account is already linked to an employee in this organization.");
        var membership = await db.OrganizationMemberships.FindAsync([tenant.OrganizationId, user.Id], ct);
        if (membership is null)
        {
            db.OrganizationMemberships.Add(new OrganizationMembership { OrganizationId = tenant.OrganizationId, UserId = user.Id, RequiresVerifiedEmail = true });
            await db.SaveChangesAsync(ct);
        }
        // Existing memberships (including revoked ones) are never elevated or reactivated here.
        await transaction.CommitAsync(ct);
        await transaction.DisposeAsync();
        // If this fails, the durable intent and email uniqueness allow a retry to finish the link.
        await using var linkTransaction = await writeLock.Acquire(ct);
        await store.Employees.UpdateOneAsync(x => x.OrganizationId == tenant.OrganizationId && x.Id == employee.Id,
            Builders<Employee>.Update.Set(x => x.ApplicationUserId, user.Id).Set(x => x.UpdatedAtUtc, DateTime.UtcNow), cancellationToken: ct);
        await linkTransaction.CommitAsync(ct);
        employee.ApplicationUserId = user.Id;
        return employee;
    }
}
