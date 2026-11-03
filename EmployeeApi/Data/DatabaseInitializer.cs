using EmployeeApi.Services;
using Microsoft.EntityFrameworkCore;

namespace EmployeeApi.Data;

public sealed class DatabaseInitializer(ApplicationDbContext db, PlatformAdminSeeder seeder, HrmStore store,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken ct)
    {
        await db.Database.MigrateAsync(ct);
        await seeder.ValidateAsync(ct);
        // Organizations and memberships live in SQLite. Do not create a duplicate Mongo organization store.
        await store.EnsureIndexes(ct);
        await seeder.SeedAsync(ct);
        logger.LogInformation("Database initialization completed: Identity migrations, HR collections/indexes, and platform administrator role assignment.");
    }
}
