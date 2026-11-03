using EmployeeApi.Configuration;
using EmployeeApi.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace EmployeeApi.Data;

public static class DevelopmentDatabaseReset
{
    public static async Task Run(IHostEnvironment environment, IConfiguration configuration, IServiceProvider services, string[] args, CancellationToken ct)
    {
        if (!environment.IsDevelopment() || !args.Contains("--reset-development-databases") || !args.Contains("--confirm-permanent-delete"))
            throw new DatabaseSetupException("PERMANENT DATA DELETION requires Development, --reset-development-databases and --confirm-permanent-delete. Back up both databases first.");
        var mongo = services.GetRequiredService<IOptions<MongoDbSettings>>().Value;
        var url = new MongoUrl(mongo.ConnectionString);
        var db = services.GetRequiredService<ApplicationDbContext>();
        var sqlite = new SqliteConnectionStringBuilder(db.Database.GetConnectionString());
        var sqlitePath = Path.GetFullPath(sqlite.DataSource);
        var allowed = new[] { "identity.db", "identity.development.db" }.Select(n => Path.GetFullPath(n, environment.ContentRootPath)).ToArray();
        if (!(mongo.DatabaseName == "TestDB" || mongo.DatabaseName.EndsWith("_development", StringComparison.Ordinal)) || url.Servers.Any(s => s.Host is not ("localhost" or "127.0.0.1" or "::1")) || !allowed.Contains(sqlitePath, StringComparer.OrdinalIgnoreCase) || !args.Contains("--confirm-database=" + mongo.DatabaseName))
            throw new DatabaseSetupException("Reset only permits local TestDB or *_development databases and identity.db/identity.development.db in the API directory. Supply --confirm-database=<exact MongoDB name>. Production/shared targets are refused.");
        var store = services.GetRequiredService<HrmStore>();
        var allowedCollections = new[] { "Employees", "Departments", "Attendance", "Organizations", "Shifts", "ShiftAssignments", "Leaves", "Salaries", "Payroll", "Payslips" };
        if (store.CollectionNames.Any(n => !allowedCollections.Contains(n, StringComparer.Ordinal)))
            throw new DatabaseSetupException("Reset refuses custom collection names to protect unrelated collections.");
        Console.WriteLine("PERMANENTLY deleting the configured local development Identity database and application MongoDB collections.");
        await services.GetRequiredService<HrmStore>().ClearDevelopmentCollections(ct);
        await db.Database.EnsureDeletedAsync(ct);
        Console.WriteLine("Development reset complete. Restart with --initialize-databases to recreate migrations, indexes and seed data.");
    }
}
