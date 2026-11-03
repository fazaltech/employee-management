using EmployeeApi.Data;
using EmployeeApi.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EmployeeApi.Services;

// Serializes Mongo hierarchy changes across API instances using the shared SQLite write lock.
public sealed class TenantWriteLock(ApplicationDbContext db, TenantContext tenant, IConfiguration configuration)
{
    public async Task<IDbContextTransaction> Acquire(CancellationToken ct)
    {
        var requireEmail = configuration.GetValue("Authentication:RequireConfirmedEmail", true);
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await db.Organizations.Where(x => x.Id == tenant.OrganizationId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.WriteVersion, x => x.WriteVersion + 1), ct);
            if (!await db.OrganizationMemberships.AnyAsync(x => x.OrganizationId == tenant.OrganizationId && x.UserId == tenant.UserId && x.IsActive && x.Role == tenant.Role
                    && (!requireEmail || !x.RequiresVerifiedEmail || db.Users.Any(u => u.Id == x.UserId && u.EmailConfirmed)), ct)
                || !await db.Organizations.AnyAsync(x => x.Id == tenant.OrganizationId && x.Status == "Active", ct))
                throw new ApiException(403, "Organization access has changed.");
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }
}
