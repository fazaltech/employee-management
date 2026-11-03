using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EmployeeApi.DTOs.Employees;
using EmployeeApi.Infrastructure;
using EmployeeApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace EmployeeApi.Controllers;

[ApiController, Authorize(Policy = "OrganizationMember")]
[Route("api/employees")]
[Route("api/organizations/{organizationId}/employees")]
public sealed class EmployeesController(IEmployeeService employees, HrmStore store, TenantContext tenant,
    TenantWriteLock writeLock, EmployeeProvisioningService provisioning, AccountAdministrationService accounts) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok((await employees.GetAllAsync(ct)).Select(x => EmployeeResponse.From(x, tenant.IsHR)));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var employee = await employees.GetByIdAsync(id, ct);
        return employee is null ? NotFound() : Ok(EmployeeResponse.From(employee, tenant.IsHR));
    }

    [HttpPost, Authorize(Policy = "HRAdmin")]
    public async Task<IActionResult> Create(EmployeeRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct)
    {
        if (!Guid.TryParse(key, out var parsed)) throw new ApiException(400, "Supply a UUID Idempotency-Key header; reuse it when retrying this creation.");
        key = parsed.ToString();
        await accounts.ValidatePassword(new EmployeeApi.Models.ApplicationUser(), request.Password, false);
        var fingerprintPayload = JsonSerializer.SerializeToNode(request)!.AsObject();
        fingerprintPayload.Remove(nameof(request.Password)); // Never persist a password or a password-derived fingerprint.
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintPayload.ToJsonString())));
        EmployeeApi.Models.Employee employee;
        await using (var tx = await writeLock.Acquire(ct))
        {
            await store.EnsureIndexes(ct);
            employee = await store.Employees.Find(x => x.OrganizationId == tenant.OrganizationId && x.CreationKey == key).FirstOrDefaultAsync(ct);
            if (employee != null)
            {
                if (employee.CreationFingerprint != fingerprint) throw new ApiException(409, "This creation key was already used with different employee details.");
            }
            else
            {
                employee = request.ToEmployee();
                employee.Salary = 0; // Compensation is approved through salary records.
                employee.CreationKey = key;
                employee.CreationFingerprint = fingerprint;
                if (request.ProvisionAppAccess) employee.ProvisioningUserId = Guid.NewGuid().ToString();
                await employees.CreateAsync(employee, ct);
            }
            await tx.CommitAsync(ct);
        }
        if (request.ProvisionAppAccess) employee = await provisioning.Provision(employee.Id, ct, request.Password);
        return Created($"/api/organizations/{tenant.OrganizationId}/employees/{employee.Id}", EmployeeResponse.From(employee, true));
    }

    [HttpPut("{id}"), Authorize(Policy = "HRAdmin")]
    public async Task<IActionResult> Update(string id, EmployeeRequest request, CancellationToken ct)
    {
        await accounts.ValidatePassword(new EmployeeApi.Models.ApplicationUser(), request.Password, false);
        EmployeeApi.Models.Employee? employee;
        await using (var tx = await writeLock.Acquire(ct))
        {
            employee = await employees.GetByIdAsync(id, ct);
            if (employee is null) return NotFound();
            if ((employee.ApplicationUserId != null || employee.ProvisioningUserId != null) && employee.Email != request.Email.Trim().ToLowerInvariant())
                throw new ApiException(409, "Email cannot be changed after app provisioning has started.");
            var legacySalary = employee.Salary;
            request.ApplyTo(employee);
            employee.Salary = legacySalary;
            if (request.ProvisionAppAccess) employee.ProvisioningUserId ??= Guid.NewGuid().ToString();
            await employees.UpdateAsync(employee, ct);
            await tx.CommitAsync(ct);
        }
        if (request.ProvisionAppAccess) employee = await provisioning.Provision(id, ct, request.Password, updatePassword: true);
        return Ok(EmployeeResponse.From(employee, true));
    }

    [HttpPost("{id}/app-access"), Authorize(Policy = "HRAdmin")]
    public async Task<IActionResult> Provision(string id, CancellationToken ct)
    {
        if (await employees.GetByIdAsync(id, ct) is null) return NotFound();
        return Ok(EmployeeResponse.From(await provisioning.Provision(id, ct), true));
    }

    [HttpDelete("{id}"), Authorize(Policy = "HRAdmin")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await using var tx = await writeLock.Acquire(ct);
        var deleted = await employees.DeleteAsync(id, ct);
        await tx.CommitAsync(ct);
        return deleted ? NoContent() : NotFound();
    }
}
