using EmployeeApi.Services;
using EmployeeApi.Models;
using MongoDB.Driver;
using Microsoft.EntityFrameworkCore;

namespace EmployeeApi.Data;

public sealed class DatabaseInitializer(ApplicationDbContext db, PlatformAdminSeeder seeder, HrmStore store,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken ct)
    {
        await db.Database.MigrateAsync(ct);
        await seeder.ValidateAsync(ct);

        await store.EnsureIndexes(ct);
        foreach (var org in await db.Organizations.AsNoTracking().ToListAsync(ct))
        {
            if (await store.Employees.Find(x => x.OrganizationId == org.Id && (x.ApplicationUserId == org.OwnerUserId || x.ProvisioningUserId == org.OwnerUserId)).AnyAsync(ct))
                throw new DatabaseSetupException("An existing organization owner is linked to an employee. Reconcile that legacy employee link before initialization; owner accounts cannot have departments.");
            await store.Organizations.UpdateOneAsync(x => x.OrganizationId == org.Id,
                Builders<OrganizationProfile>.Update.Set(x => x.Name, org.Name).Set(x => x.OwnerUserId, org.OwnerUserId).Set(x => x.Status, org.Status).SetOnInsert(x => x.OrganizationId, org.Id).SetOnInsert(x => x.Id, org.Id).SetOnInsert(x => x.TimeZone, "UTC"), new UpdateOptions { IsUpsert = true }, ct);
        }
        await seeder.SeedAsync(ct);
        logger.LogInformation("Database initialization completed: Identity migrations, HR collections/indexes, and platform administrator role assignment.");
    }
}
