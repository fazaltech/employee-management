using EmployeeApi.Configuration;
using EmployeeApi.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EmployeeApi.Services;

public sealed class HrmStore(IMongoClient client, IOptions<MongoDbSettings> options)
{
    private readonly IMongoDatabase database = client.GetDatabase(options.Value.DatabaseName);
    public IMongoCollection<Employee> Employees => database.GetCollection<Employee>(options.Value.EmployeesCollectionName);
    public IMongoCollection<Department> Departments => database.GetCollection<Department>(options.Value.DepartmentsCollectionName);
    public IMongoCollection<Attendance> Attendance => database.GetCollection<Attendance>(options.Value.AttendanceCollectionName);
    public IMongoCollection<OrganizationProfile> Organizations => database.GetCollection<OrganizationProfile>("Organizations");
    public IMongoCollection<Shift> Shifts => database.GetCollection<Shift>("Shifts");
    public IMongoCollection<ShiftAssignment> ShiftAssignments => database.GetCollection<ShiftAssignment>("ShiftAssignments");
    public IMongoCollection<LeaveRequest> Leaves => database.GetCollection<LeaveRequest>("Leaves");
    public IMongoCollection<SalaryRecord> Salaries => database.GetCollection<SalaryRecord>("Salaries");
    public IMongoCollection<Payroll> Payroll => database.GetCollection<Payroll>("Payroll");
    public IMongoCollection<Payslip> Payslips => database.GetCollection<Payslip>("Payslips");
    public string[] CollectionNames => [options.Value.EmployeesCollectionName, options.Value.DepartmentsCollectionName, options.Value.AttendanceCollectionName, "Organizations", "Shifts", "ShiftAssignments", "Leaves", "Salaries", "Payroll", "Payslips"];
    public async Task ClearDevelopmentCollections(CancellationToken ct)
    {
        foreach (var name in CollectionNames) await database.DropCollectionAsync(name, ct);
    }
    private readonly SemaphoreSlim gate = new(1);
    private bool initialized;
    public async Task EnsureIndexes(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (initialized) return;
            await EnsureCollection(options.Value.EmployeesCollectionName, ct);
            await EnsureCollection(options.Value.DepartmentsCollectionName, ct);
            await EnsureCollection(options.Value.AttendanceCollectionName, ct);
            foreach (var name in CollectionNames.Skip(3)) {
                await EnsureCollection(name, ct);
                if (name != "Organizations") await database.GetCollection<BsonDocument>(name).Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("OrganizationId")), cancellationToken: ct);
            }
            await Organizations.Indexes.CreateOneAsync(new CreateIndexModel<OrganizationProfile>(Builders<OrganizationProfile>.IndexKeys.Ascending(x => x.OrganizationId), new CreateIndexOptions { Unique = true }), cancellationToken: ct);
            await Salaries.Indexes.CreateOneAsync(new CreateIndexModel<SalaryRecord>(Builders<SalaryRecord>.IndexKeys.Ascending(x => x.OrganizationId).Ascending(x => x.EmployeeId).Ascending(x => x.EffectiveFrom), new CreateIndexOptions { Unique = true }), cancellationToken: ct);
            await Payroll.Indexes.CreateOneAsync(new CreateIndexModel<Payroll>(Builders<Payroll>.IndexKeys.Ascending(x => x.OrganizationId).Ascending(x => x.Period), new CreateIndexOptions { Unique = true }), cancellationToken: ct);
            await Payslips.Indexes.CreateOneAsync(new CreateIndexModel<Payslip>(Builders<Payslip>.IndexKeys.Ascending(x => x.OrganizationId).Ascending(x => x.PayrollId).Ascending(x => x.EmployeeId), new CreateIndexOptions { Unique = true }), cancellationToken: ct);
            await Leaves.Indexes.CreateOneAsync(new CreateIndexModel<LeaveRequest>(Builders<LeaveRequest>.IndexKeys.Ascending(x => x.OrganizationId).Ascending(x => x.EmployeeId).Ascending(x => x.Status)), cancellationToken: ct);
            await ShiftAssignments.Indexes.CreateOneAsync(new CreateIndexModel<ShiftAssignment>(Builders<ShiftAssignment>.IndexKeys.Ascending(x => x.OrganizationId).Ascending(x => x.EmployeeId).Ascending(x => x.EffectiveFrom)), cancellationToken: ct);
            var e = Builders<Employee>.IndexKeys;
            var assigned = Builders<Employee>.Filter.Gt(x => x.OrganizationId, "");
            await Employees.Indexes.CreateManyAsync([
                new(e.Ascending(x => x.OrganizationId).Ascending(x => x.EmployeeNumber), new CreateIndexOptions<Employee> { Unique = true, PartialFilterExpression = assigned, Name = "tenant_employee_number" }),
                new(e.Ascending(x => x.OrganizationId).Ascending(x => x.Email), new CreateIndexOptions<Employee> { Unique = true, PartialFilterExpression = assigned, Name = "tenant_employee_email" }),
                new(e.Ascending(x => x.OrganizationId).Ascending(x => x.CreationKey), new CreateIndexOptions<Employee> { Unique = true, PartialFilterExpression = Builders<Employee>.Filter.Gt(x => x.CreationKey, ""), Name = "tenant_creation_key" }),
                new(e.Ascending(x => x.OrganizationId).Ascending(x => x.ApplicationUserId), new CreateIndexOptions<Employee> { Unique = true, PartialFilterExpression = Builders<Employee>.Filter.Gt(x => x.ApplicationUserId, ""), Name = "tenant_user" }),
                new(e.Ascending(x => x.OrganizationId).Ascending(x => x.ManagerEmployeeId)),
                new(e.Ascending(x => x.OrganizationId).Ascending(x => x.DepartmentId))
            ], ct);
            await Departments.Indexes.CreateOneAsync(new CreateIndexModel<Department>(Builders<Department>.IndexKeys.Ascending(x => x.OrganizationId).Ascending(x => x.NormalizedName), new CreateIndexOptions { Unique = true }), cancellationToken: ct);
            await Attendance.Indexes.CreateOneAsync(new CreateIndexModel<Attendance>(Builders<Attendance>.IndexKeys.Ascending(x => x.OrganizationId).Ascending(x => x.EmployeeId).Ascending(x => x.WorkDate), new CreateIndexOptions { Unique = true }), cancellationToken: ct);
            await Attendance.Indexes.CreateOneAsync(new CreateIndexModel<Attendance>(Builders<Attendance>.IndexKeys.Ascending(x => x.OrganizationId).Ascending(x => x.WorkDate)), cancellationToken: ct);
            await Attendance.Indexes.CreateOneAsync(new CreateIndexModel<Attendance>(Builders<Attendance>.IndexKeys.Ascending(x => x.OrganizationId).Ascending(x => x.EmployeeId).Ascending(x => x.CheckOutUtc)), cancellationToken: ct);
            initialized = true;
        }
        finally { gate.Release(); }
    }

    private async Task EnsureCollection(string name, CancellationToken ct)
    {
        var tenantValidator = new BsonDocument("$jsonSchema", new BsonDocument
        {
            { "bsonType", "object" },
            { "required", new BsonArray { "OrganizationId" } },
            { "properties", new BsonDocument("OrganizationId", new BsonDocument { { "bsonType", "string" }, { "minLength", 1 } }) }
        });
        using var cursor = await database.ListCollectionsAsync(new ListCollectionsOptions { Filter = new BsonDocument("name", name) }, ct);
        var existing = await cursor.FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            try
            {
                await database.CreateCollectionAsync(name, new CreateCollectionOptions<BsonDocument>
                {
                    Validator = tenantValidator,
                    ValidationLevel = DocumentValidationLevel.Strict,
                    ValidationAction = DocumentValidationAction.Error
                }, ct);
                return;
            }
            catch (MongoCommandException exception) when (exception.Code == 48)
            {
                // A concurrent initializer created it. Inspect its current options below.
                using var retry = await database.ListCollectionsAsync(new ListCollectionsOptions { Filter = new BsonDocument("name", name) }, ct);
                existing = await retry.FirstOrDefaultAsync(ct);
            }
        }
        var collectionOptions = existing?.GetValue("options", new BsonDocument()).AsBsonDocument ?? new BsonDocument();
        var validator = collectionOptions.GetValue("validator", new BsonDocument()).AsBsonDocument;
        var includesTenantRule = validator.Equals(tenantValidator) ||
            (validator.TryGetValue("$and", out var rules) && rules.IsBsonArray && rules.AsBsonArray.Any(x => x.Equals(tenantValidator)));
        if (!includesTenantRule)
            validator = validator.ElementCount == 0 ? tenantValidator : new BsonDocument("$and", new BsonArray { validator, tenantValidator });
        if (includesTenantRule && collectionOptions.GetValue("validationLevel", "strict") == "strict"
            && collectionOptions.GetValue("validationAction", "error") == "error") return;
        // Preserve any existing custom validation rules. Old unassigned records are not rewritten;
        // future inserts/updates must provide an OrganizationId (see the explicit legacy migration).
        await database.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "collMod", name }, { "validator", validator }, { "validationLevel", "strict" }, { "validationAction", "error" }
        }, cancellationToken: ct);
    }
}
