using System.ComponentModel.DataAnnotations;
using EmployeeApi.Infrastructure;
using EmployeeApi.Models;
using EmployeeApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EmployeeApi.Controllers;
public sealed record DepartmentRequest([Required, StringLength(100)] string Name);

[ApiController, Route("api/organizations/{organizationId}/departments"), Authorize(Policy = "OrganizationMember")]
public sealed class DepartmentsController(HrmStore store, TenantContext tenant, TenantWriteLock writeLock, IEmployeeService employees) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        await store.EnsureIndexes(ct);
        var departmentIds = (await employees.GetAllAsync(ct)).Select(x => x.DepartmentId).ToArray();
        return Ok((await store.Departments.Find(x => x.OrganizationId == tenant.OrganizationId && (tenant.IsHR || departmentIds.Contains(x.Id))).SortBy(x => x.Name).ToListAsync(ct))
            .Select(x => new { x.Id, x.Name, x.OrganizationId, x.CreatedAtUtc, x.UpdatedAtUtc }));
    }
    [HttpPost, Authorize(Policy = "OrganizationOwner")]
    public async Task<IActionResult> Create(DepartmentRequest request, CancellationToken ct)
    {
        await using var tx = await writeLock.Acquire(ct);
        await store.EnsureIndexes(ct);
        var d = new Department { OrganizationId = tenant.OrganizationId, Name = request.Name.Trim(), NormalizedName = request.Name.Trim().ToUpperInvariant() };
        await store.Departments.InsertOneAsync(d, cancellationToken: ct);
        await tx.CommitAsync(ct);
        return Created($"/api/organizations/{tenant.OrganizationId}/departments/{d.Id}", new { d.Id, d.Name });
    }
    [HttpPut("{id}"), Authorize(Policy = "OrganizationOwner")]
    public async Task<IActionResult> Update(string id, DepartmentRequest request, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _)) return NotFound();
        await using var tx = await writeLock.Acquire(ct);
        await store.EnsureIndexes(ct);
        var result = await store.Departments.UpdateOneAsync(x => x.OrganizationId == tenant.OrganizationId && x.Id == id,
            Builders<Department>.Update.Set(x => x.Name, request.Name.Trim()).Set(x => x.NormalizedName, request.Name.Trim().ToUpperInvariant()).Set(x => x.UpdatedAtUtc, DateTime.UtcNow), cancellationToken: ct);
        await tx.CommitAsync(ct);
        return result.MatchedCount == 0 ? NotFound() : NoContent();
    }
    [HttpDelete("{id}"), Authorize(Policy = "OrganizationOwner")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out var objectId)) return NotFound();
        id = objectId.ToString();
        await using var tx = await writeLock.Acquire(ct);
        if (await store.Employees.Find(x => x.OrganizationId == tenant.OrganizationId && x.DepartmentId == id).AnyAsync(ct))
            throw new ApiException(409, "Reassign employees before deleting this department.");
        var result = await store.Departments.DeleteOneAsync(x => x.OrganizationId == tenant.OrganizationId && x.Id == id, ct);
        await tx.CommitAsync(ct);
        return result.DeletedCount == 0 ? NotFound() : NoContent();
    }
}
