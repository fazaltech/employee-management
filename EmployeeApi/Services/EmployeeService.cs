using EmployeeApi.Infrastructure;
using EmployeeApi.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EmployeeApi.Services;

public sealed class EmployeeService(HrmStore store, TenantContext tenant) : IEmployeeService
{
    public FilterDefinition<Employee> TenantFilter => Builders<Employee>.Filter.Eq(x => x.OrganizationId, tenant.OrganizationId);
    public async Task<FilterDefinition<Employee>> VisibleFilter(CancellationToken ct)
    {
        if (tenant.IsHR) return TenantFilter;
        var own = await store.Employees.Find(TenantFilter & Builders<Employee>.Filter.Eq(x => x.ApplicationUserId, tenant.UserId) & Builders<Employee>.Filter.Eq(x => x.IsActive, true)).FirstOrDefaultAsync(ct);
        if (own is null) return TenantFilter & Builders<Employee>.Filter.Eq(x => x.Id, ObjectId.Empty.ToString());
        var visible = Builders<Employee>.Filter.Eq(x => x.Id, own.Id);
        if (tenant.Role == OrganizationRoles.Manager) visible |= Builders<Employee>.Filter.Eq(x => x.ManagerEmployeeId, own.Id);
        return TenantFilter & visible;
    }
    public async Task<IReadOnlyList<Employee>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await store.EnsureIndexes(cancellationToken);
        return await store.Employees.Find(await VisibleFilter(cancellationToken)).SortBy(x => x.LastName).ThenBy(x => x.FirstName).ToListAsync(cancellationToken);
    }
    public async Task<Employee?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!ObjectId.TryParse(id, out _)) return null;
        await store.EnsureIndexes(cancellationToken);
        return await store.Employees.Find(await VisibleFilter(cancellationToken) & Builders<Employee>.Filter.Eq(x => x.Id, id)).FirstOrDefaultAsync(cancellationToken);
    }
    public async Task Validate(Employee employee, CancellationToken ct)
    {
        if (!ObjectId.TryParse(employee.DepartmentId, out _) || !await store.Departments.Find(x => x.OrganizationId == tenant.OrganizationId && x.Id == employee.DepartmentId).AnyAsync(ct))
            throw new ApiException(400, "Select a department in this organization.");
        var visited = new HashSet<string> { employee.Id };
        var managerId = employee.ManagerEmployeeId;
        while (managerId != null)
        {
            if (!ObjectId.TryParse(managerId, out _) || !visited.Add(managerId)) throw new ApiException(400, "Invalid manager or reporting cycle.");
            var manager = await store.Employees.Find(x => x.OrganizationId == tenant.OrganizationId && x.Id == managerId).FirstOrDefaultAsync(ct);
            if (manager is null || !manager.IsActive) throw new ApiException(400, "Select an active manager in this organization.");
            managerId = manager.ManagerEmployeeId;
        }
    }
    public async Task CreateAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        employee.OrganizationId = tenant.OrganizationId;
        await store.EnsureIndexes(cancellationToken);
        await Validate(employee, cancellationToken);
        await store.Employees.InsertOneAsync(employee, cancellationToken: cancellationToken);
    }
    public async Task<bool> UpdateAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        await Validate(employee, cancellationToken);
        return (await store.Employees.ReplaceOneAsync(x => x.OrganizationId == tenant.OrganizationId && x.Id == employee.Id, employee, cancellationToken: cancellationToken)).MatchedCount == 1;
    }
    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!ObjectId.TryParse(id, out var objectId)) return false;
        id = objectId.ToString();
        if (await store.Employees.Find(x => x.OrganizationId == tenant.OrganizationId && x.ManagerEmployeeId == id).AnyAsync(cancellationToken)
            || await store.Attendance.Find(x => x.OrganizationId == tenant.OrganizationId && x.EmployeeId == id).AnyAsync(cancellationToken))
            throw new ApiException(409, "Reassign direct reports first. Employees with attendance history must be deactivated instead of deleted.");
        var employee = await GetByIdAsync(id, cancellationToken);
        if (employee?.ApplicationUserId != null || employee?.ProvisioningUserId != null)
            throw new ApiException(409, "Employees with app access must be deactivated instead of deleted.");
        return (await store.Employees.DeleteOneAsync(x => x.OrganizationId == tenant.OrganizationId && x.Id == id, cancellationToken)).DeletedCount == 1;
    }
}
