using System.ComponentModel.DataAnnotations;
using System.Globalization;
using EmployeeApi.Infrastructure;
using EmployeeApi.Models;
using EmployeeApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace EmployeeApi.Controllers;

public sealed record LeaveInput(string From, string To, [Required, StringLength(2000)] string Reason);
public sealed record DecisionInput(bool Approved, [StringLength(2000)] string Comment = "");
public sealed record SalaryInput(string EmployeeId, [Range(0.01, 1000000000)] decimal Amount, [RegularExpression("^[A-Z]{3}$")] string Currency, string EffectiveFrom);
public sealed record ShiftInput([Required, StringLength(100)] string Name, string StartTime, string EndTime, int BreakMinutes, int[] WorkingDays);
public sealed record AssignmentInput(string EmployeeId, string ShiftId, string EffectiveFrom, string? EffectiveTo);
public sealed record SettingsInput([Required] string TimeZone);
public sealed record PayrollInput(string Period);

[ApiController, Authorize(Policy = "OrganizationMember"), Route("api/organizations/{organizationId}")]
public sealed class HrWorkflowsController(HrmStore store, TenantContext tenant, TenantWriteLock writeLock, IEmployeeService employees) : ControllerBase
{
    public static DateOnly Date(string value) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
        ? d : throw new ApiException(400, "Use a date in yyyy-MM-dd format.");
    private async Task<Employee> Self(CancellationToken ct) => await store.Employees.Find(x => x.OrganizationId == tenant.OrganizationId && x.ApplicationUserId == tenant.UserId && x.IsActive).FirstOrDefaultAsync(ct)
        ?? throw new ApiException(403, "An active employee account is required.");
    private async Task<Employee> Employee(string id, CancellationToken ct) => await employees.GetByIdAsync(id, ct) ?? throw new ApiException(404, "Employee not found.");
    private void Stamp(TenantDocument row) { row.OrganizationId = tenant.OrganizationId; row.CreatedByUserId = tenant.UserId; }

    [HttpGet("hr-settings")]
    public async Task<IActionResult> Settings(CancellationToken ct) => Ok(await store.Organizations.Find(x => x.OrganizationId == tenant.OrganizationId).FirstOrDefaultAsync(ct)
        ?? new OrganizationProfile { Id = tenant.OrganizationId, OrganizationId = tenant.OrganizationId });
    [HttpPut("hr-settings"), Authorize(Policy = "OrganizationOwner")]
    public async Task<IActionResult> Settings(SettingsInput input, CancellationToken ct)
    {
        try { TimeZoneInfo.FindSystemTimeZoneById(input.TimeZone); } catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { throw new ApiException(400, "Unknown timezone."); }
        await using var tx = await writeLock.Acquire(ct);
        await store.Organizations.UpdateOneAsync(x => x.OrganizationId == tenant.OrganizationId,
            Builders<OrganizationProfile>.Update.Set(x => x.TimeZone, input.TimeZone).Set(x => x.OrganizationId, tenant.OrganizationId).Set(x => x.UpdatedAtUtc, DateTime.UtcNow), new UpdateOptions { IsUpsert = true }, ct);
        await tx.CommitAsync(ct); return NoContent();
    }
    [HttpGet("leaves")]
    public async Task<IActionResult> Leaves(CancellationToken ct)
    {
        var ids = (await employees.GetAllAsync(ct)).Select(x => x.Id).ToArray();
        return Ok(await store.Leaves.Find(x => x.OrganizationId == tenant.OrganizationId && ids.Contains(x.EmployeeId)).SortByDescending(x => x.CreatedAtUtc).ToListAsync(ct));
    }
    [HttpPost("leaves")]
    public async Task<IActionResult> Submit(LeaveInput input, CancellationToken ct)
    {
        var start = Date(input.From); var end = Date(input.To);
        if (end < start || end.DayNumber - start.DayNumber > 366) throw new ApiException(400, "Invalid leave date range (maximum 367 days).");
        await using var tx = await writeLock.Acquire(ct);
        var self = await Self(ct);
        if (self.ManagerEmployeeId is null) throw new ApiException(409, "Assign a direct manager before submitting leave; approval stages cannot be skipped.");
        var f = Builders<LeaveRequest>.Filter;
        if (await store.Leaves.Find(f.Eq(x => x.OrganizationId, tenant.OrganizationId) & f.Eq(x => x.EmployeeId, self.Id) & f.Ne(x => x.Status, "Rejected") & f.Lte(x => x.From, input.To) & f.Gte(x => x.To, input.From)).AnyAsync(ct))
            throw new ApiException(409, "An overlapping leave request exists.");
        var row = new LeaveRequest { EmployeeId = self.Id, From = input.From, To = input.To, Reason = input.Reason.Trim() }; Stamp(row);
        await store.Leaves.InsertOneAsync(row, cancellationToken: ct); await tx.CommitAsync(ct); return Ok(row);
    }
    public static string NextStage(string status, string role, bool approved)
    {
        var required = status switch { "PendingManager" => OrganizationRoles.Manager, "PendingHR" => OrganizationRoles.HR, "PendingOwner" => OrganizationRoles.Owner, _ => "" };
        if (required.Length == 0) throw new ApiException(409, "This request has already been decided.");
        if (role != required) throw new ApiException(403, "Only the approver for the current stage can decide this request.");
        return !approved ? "Rejected" : status switch { "PendingManager" => "PendingHR", "PendingHR" => "PendingOwner", _ => "Approved" };
    }
    [HttpPost("leaves/{id}/decision")]
    public async Task<IActionResult> Decide(string id, DecisionInput input, CancellationToken ct)
    {
        await using var tx = await writeLock.Acquire(ct);
        var row = await store.Leaves.Find(x => x.OrganizationId == tenant.OrganizationId && x.Id == id).FirstOrDefaultAsync(ct) ?? throw new ApiException(404, "Leave not found.");
        var next = NextStage(row.Status, tenant.Role, input.Approved);
        var employee = await Employee(row.EmployeeId, ct);
        if (employee.ApplicationUserId == tenant.UserId || row.Decisions.Any(x => x.ApproverUserId == tenant.UserId)) throw new ApiException(403, "A requester cannot approve their own leave, and stages require distinct approvers.");
        if (tenant.Role == OrganizationRoles.Manager && employee.ManagerEmployeeId != (await Self(ct)).Id) throw new ApiException(403, "Only the direct manager can approve this request.");
        var previous = row.Status; row.Decisions.Add(new(previous, tenant.UserId, input.Approved, DateTime.UtcNow, input.Comment.Trim())); row.Status = next; row.UpdatedAtUtc = DateTime.UtcNow;
        var result = await store.Leaves.ReplaceOneAsync(x => x.OrganizationId == tenant.OrganizationId && x.Id == id && x.Status == previous, row, cancellationToken: ct);
        if (result.ModifiedCount != 1) throw new ApiException(409, "The request changed. Refresh before deciding.");
        await tx.CommitAsync(ct); return Ok(row);
    }
    [HttpGet("shifts")]
    public async Task<IActionResult> Shifts(CancellationToken ct) => Ok(await store.Shifts.Find(x => x.OrganizationId == tenant.OrganizationId).ToListAsync(ct));
    [HttpPost("shifts"), Authorize(Policy = "OrganizationOwner")]
    public Task<IActionResult> CreateShift(ShiftInput input, CancellationToken ct) => SaveShift(null, input, ct);
    [HttpPut("shifts/{id}"), Authorize(Policy = "OrganizationOwner")]
    public Task<IActionResult> UpdateShift(string id, ShiftInput input, CancellationToken ct) => SaveShift(id, input, ct);
    private async Task<IActionResult> SaveShift(string? id, ShiftInput input, CancellationToken ct)
    {
        if (!TimeOnly.TryParseExact(input.StartTime, "HH:mm", out var start) || !TimeOnly.TryParseExact(input.EndTime, "HH:mm", out var end) || start == end || input.BreakMinutes < 0 || input.BreakMinutes >= (end - start).TotalMinutes || input.WorkingDays is null || input.WorkingDays.Length == 0 || input.WorkingDays.Any(x => x < 0 || x > 6)) throw new ApiException(400, "Invalid working hours, break or working days (Sunday=0).");
        await using var tx = await writeLock.Acquire(ct);
        var row = id is null ? new Shift() : await store.Shifts.Find(x => x.OrganizationId == tenant.OrganizationId && x.Id == id).FirstOrDefaultAsync(ct) ?? throw new ApiException(404, "Shift not found.");
        var profile = await store.Organizations.Find(x => x.OrganizationId == tenant.OrganizationId).FirstOrDefaultAsync(ct);
        row.Name = input.Name.Trim(); row.StartTime = input.StartTime; row.EndTime = input.EndTime; row.BreakMinutes = input.BreakMinutes; row.WorkingDays = input.WorkingDays.Distinct().ToArray(); row.TimeZone = profile?.TimeZone ?? "UTC"; row.UpdatedAtUtc = DateTime.UtcNow;
        if (id is null) { Stamp(row); await store.Shifts.InsertOneAsync(row, cancellationToken: ct); }
        else await store.Shifts.ReplaceOneAsync(x => x.OrganizationId == tenant.OrganizationId && x.Id == id, row, cancellationToken: ct);
        await tx.CommitAsync(ct); return Ok(row);
    }
    [HttpGet("shift-assignments")]
    public async Task<IActionResult> Assignments(CancellationToken ct)
    {
        var ids = (await employees.GetAllAsync(ct)).Select(x => x.Id).ToArray();
        return Ok(await store.ShiftAssignments.Find(x => x.OrganizationId == tenant.OrganizationId && ids.Contains(x.EmployeeId)).ToListAsync(ct));
    }
    [HttpPost("shift-assignments"), Authorize(Policy = "HRAdmin")]
    public Task<IActionResult> Assign(AssignmentInput input, CancellationToken ct) => SaveAssignment(null, input, ct);
    [HttpPut("shift-assignments/{id}"), Authorize(Policy = "HRAdmin")]
    public Task<IActionResult> UpdateAssignment(string id, AssignmentInput input, CancellationToken ct) => SaveAssignment(id, input, ct);
    private async Task<IActionResult> SaveAssignment(string? id, AssignmentInput input, CancellationToken ct)
    {
        var from = Date(input.EffectiveFrom); if (input.EffectiveTo != null && Date(input.EffectiveTo) < from) throw new ApiException(400, "Invalid effective dates.");
        await using var tx = await writeLock.Acquire(ct); await Employee(input.EmployeeId, ct);
        if (!await store.Shifts.Find(x => x.OrganizationId == tenant.OrganizationId && x.Id == input.ShiftId).AnyAsync(ct)) throw new ApiException(400, "Select a shift in this organization.");
        var existing = await store.ShiftAssignments.Find(x => x.OrganizationId == tenant.OrganizationId && x.EmployeeId == input.EmployeeId).ToListAsync(ct);
        if (existing.Any(x => x.Id != id && Date(x.EffectiveFrom) <= (input.EffectiveTo == null ? DateOnly.MaxValue : Date(input.EffectiveTo)) && (x.EffectiveTo == null || Date(x.EffectiveTo) >= from))) throw new ApiException(409, "Shift assignments cannot overlap. Supply an end date for each planned assignment.");
        var row = id == null ? new ShiftAssignment() : existing.SingleOrDefault(x => x.Id == id) ?? throw new ApiException(404, "Assignment not found for this employee.");
        row.EmployeeId = input.EmployeeId; row.ShiftId = input.ShiftId; row.EffectiveFrom = input.EffectiveFrom; row.EffectiveTo = input.EffectiveTo; row.UpdatedAtUtc = DateTime.UtcNow;
        if (id == null) { Stamp(row); await store.ShiftAssignments.InsertOneAsync(row, cancellationToken: ct); }
        else await store.ShiftAssignments.ReplaceOneAsync(x => x.OrganizationId == tenant.OrganizationId && x.Id == id, row, cancellationToken: ct); await tx.CommitAsync(ct); return Ok(row);
    }
    [HttpGet("salaries")]
    public async Task<IActionResult> Salaries(CancellationToken ct)
    {
        var self = tenant.IsHR ? null : (await Self(ct)).Id;
        return Ok(await store.Salaries.Find(x => x.OrganizationId == tenant.OrganizationId && (self == null || x.EmployeeId == self && x.Status == "Approved")).SortByDescending(x => x.EffectiveFrom).ToListAsync(ct));
    }
    [HttpPost("salaries"), Authorize(Policy = "HRAdmin")]
    public async Task<IActionResult> Salary(SalaryInput input, CancellationToken ct)
    {
        Date(input.EffectiveFrom); await using var tx = await writeLock.Acquire(ct); await Employee(input.EmployeeId, ct);
        var row = new SalaryRecord { EmployeeId = input.EmployeeId, Amount = input.Amount, Currency = input.Currency, EffectiveFrom = input.EffectiveFrom }; Stamp(row);
        await store.Salaries.InsertOneAsync(row, cancellationToken: ct); await tx.CommitAsync(ct); return Ok(row);
    }
    [HttpPut("salaries/{id}"), Authorize(Policy = "HRAdmin")]
    public async Task<IActionResult> UpdateSalary(string id, SalaryInput input, CancellationToken ct)
    {
        Date(input.EffectiveFrom); await using var tx = await writeLock.Acquire(ct); await Employee(input.EmployeeId, ct);
        var result = await store.Salaries.UpdateOneAsync(x => x.OrganizationId == tenant.OrganizationId && x.Id == id && x.Status == "Draft",
            Builders<SalaryRecord>.Update.Set(x => x.EmployeeId, input.EmployeeId).Set(x => x.Amount, input.Amount).Set(x => x.Currency, input.Currency).Set(x => x.EffectiveFrom, input.EffectiveFrom).Set(x => x.UpdatedAtUtc, DateTime.UtcNow), cancellationToken: ct);
        if (result.MatchedCount != 1) throw new ApiException(409, "Only existing salary drafts can be edited.");
        await tx.CommitAsync(ct); return NoContent();
    }
    [HttpDelete("payroll/{id}"), Authorize(Policy = "HRAdmin")]
    public async Task<IActionResult> DeletePayrollDraft(string id, CancellationToken ct)
    {
        await using var tx = await writeLock.Acquire(ct);
        var result = await store.Payroll.DeleteOneAsync(x => x.OrganizationId == tenant.OrganizationId && x.Id == id && x.Status == "Draft", ct);
        if (result.DeletedCount != 1) throw new ApiException(409, "Only existing payroll drafts can be removed.");
        await tx.CommitAsync(ct); return NoContent();
    }
    [HttpPost("salaries/{id}/approve"), Authorize(Policy = "OrganizationOwner")]
    public async Task<IActionResult> ApproveSalary(string id, CancellationToken ct)
    {
        await using var tx = await writeLock.Acquire(ct);
        var result = await store.Salaries.UpdateOneAsync(x => x.OrganizationId == tenant.OrganizationId && x.Id == id && x.Status == "Draft", Builders<SalaryRecord>.Update.Set(x => x.Status, "Approved").Set(x => x.ApprovedByUserId, tenant.UserId).Set(x => x.ApprovedAtUtc, DateTime.UtcNow).Set(x => x.UpdatedAtUtc, DateTime.UtcNow), cancellationToken: ct);
        if (result.ModifiedCount != 1) throw new ApiException(409, "Salary not found or already approved.");
        await tx.CommitAsync(ct); return NoContent();
    }
    [HttpGet("payroll"), Authorize(Policy = "HRAdmin")]
    public async Task<IActionResult> Payrolls(CancellationToken ct) => Ok(await store.Payroll.Find(x => x.OrganizationId == tenant.OrganizationId).SortByDescending(x => x.Period).ToListAsync(ct));
    [HttpPost("payroll"), Authorize(Policy = "HRAdmin")]
    public async Task<IActionResult> DraftPayroll(PayrollInput input, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(input.Period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)) throw new ApiException(400, "Period must use yyyy-MM.");
        await using var tx = await writeLock.Acquire(ct);
        var active = (await employees.GetAllAsync(ct)).Where(x => x.IsActive).Select(x => x.Id).ToHashSet();
        var salaries = await store.Salaries.Find(x => x.OrganizationId == tenant.OrganizationId && x.Status == "Approved").ToListAsync(ct);
        var lines = salaries.Where(x => active.Contains(x.EmployeeId) && Date(x.EffectiveFrom) <= month).GroupBy(x => x.EmployeeId).Select(g => g.OrderByDescending(x => x.EffectiveFrom).First()).Select(x => new PayrollLine(x.EmployeeId, x.Id, x.Amount, x.Currency)).ToList();
        if (lines.Count == 0) throw new ApiException(409, "No approved salaries effective at the start of this month.");
        var row = new Payroll { Period = input.Period, Lines = lines }; Stamp(row); await store.Payroll.InsertOneAsync(row, cancellationToken: ct); await tx.CommitAsync(ct); return Ok(row);
    }
    [HttpPost("payroll/{id}/approve"), Authorize(Policy = "OrganizationOwner")]
    public async Task<IActionResult> ApprovePayroll(string id, CancellationToken ct)
    {
        await using var tx = await writeLock.Acquire(ct);
        var row = await store.Payroll.Find(x => x.OrganizationId == tenant.OrganizationId && x.Id == id).FirstOrDefaultAsync(ct) ?? throw new ApiException(404, "Payroll not found.");
        if (row.Status == "Draft") {
            row.Status = "Approved"; row.ApprovedByUserId = tenant.UserId; row.ApprovedAtUtc = row.UpdatedAtUtc = DateTime.UtcNow;
            await store.Payroll.ReplaceOneAsync(x => x.OrganizationId == tenant.OrganizationId && x.Id == id && x.Status == "Draft", row, cancellationToken: ct);
        }
        // Approval is durable before slips. Retrying safely completes a partially generated payroll.
        foreach (var line in row.Lines) {
            var slip = new Payslip { Id = row.Id + ":" + line.EmployeeId, OrganizationId = tenant.OrganizationId, CreatedByUserId = tenant.UserId, PayrollId = row.Id, EmployeeId = line.EmployeeId, Period = row.Period, Amount = line.Amount, Currency = line.Currency };
            await store.Payslips.ReplaceOneAsync(x => x.OrganizationId == tenant.OrganizationId && x.Id == slip.Id, slip, new ReplaceOptions { IsUpsert = true }, ct);
        }
        await tx.CommitAsync(ct); return NoContent();
    }
    [HttpGet("payslips")]
    public async Task<IActionResult> Payslips(CancellationToken ct)
    {
        var self = tenant.IsHR ? null : (await Self(ct)).Id;
        return Ok(await store.Payslips.Find(x => x.OrganizationId == tenant.OrganizationId && (self == null || x.EmployeeId == self)).SortByDescending(x => x.Period).ToListAsync(ct));
    }
}
