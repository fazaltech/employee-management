using System.ComponentModel.DataAnnotations;
using System.Globalization;
using EmployeeApi.Infrastructure;
using EmployeeApi.Models;
using EmployeeApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace EmployeeApi.Controllers;

public sealed record CheckInRequest([Required] string EmployeeId, [StringLength(2000)] string Notes = "");
public sealed record AttendanceCorrectionRequest([Required] string Status, DateTime? CheckInUtc,
    DateTime? CheckOutUtc, [Required, StringLength(2000)] string Notes);
public sealed record AttendanceResponse(string Id, string EmployeeId, string WorkDate, DateTime? CheckInUtc,
    DateTime? CheckOutUtc, string Status, string Notes, string CreatedByUserId, string UpdatedByUserId,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc)
{
    public static AttendanceResponse From(Attendance a) => new(a.Id, a.EmployeeId, a.WorkDate, a.CheckInUtc,
        a.CheckOutUtc, a.Status, a.Notes, a.CreatedByUserId, a.UpdatedByUserId, a.CreatedAtUtc, a.UpdatedAtUtc);
}

[ApiController, Route("api/organizations/{organizationId}/attendance"), Authorize(Policy = "Employee")]
public sealed class AttendanceController(HrmStore store, IEmployeeService employees, TenantContext tenant,
    TenantWriteLock writeLock) : ControllerBase
{
    private async Task<Employee> RequireEmployee(string id, CancellationToken ct, bool active = false)
    {
        var e = await employees.GetByIdAsync(id, ct) ?? throw new ApiException(404, "Employee not found.");
        if (active && !e.IsActive) throw new ApiException(409, "The employee is inactive.");
        return e;
    }
    private static DateOnly ParseDate(string date)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            throw new ApiException(400, "Work date must use yyyy-MM-dd.");
        return result;
    }
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string employeeId, [FromQuery] string from, [FromQuery] string to, CancellationToken ct)
    {
        employeeId = (await RequireEmployee(employeeId, ct)).Id;
        var start = ParseDate(from); var end = ParseDate(to);
        if (end < start || end.DayNumber - start.DayNumber > 93) throw new ApiException(400, "Select a date range of at most 94 days.");
        var f = Builders<Attendance>.Filter;
        var rows = await store.Attendance.Find(f.Eq(x => x.OrganizationId, tenant.OrganizationId) & f.Eq(x => x.EmployeeId, employeeId)
            & f.Gte(x => x.WorkDate, from) & f.Lte(x => x.WorkDate, to)).SortByDescending(x => x.WorkDate).ToListAsync(ct);
        return Ok(rows.Select(AttendanceResponse.From));
    }
    [HttpPost("check-in")]
    public async Task<IActionResult> CheckIn(CheckInRequest request, CancellationToken ct)
    {
        await using var tx = await writeLock.Acquire(ct);
        var employee = await RequireEmployee(request.EmployeeId, ct, true);
        if (employee.ApplicationUserId != tenant.UserId) throw new ApiException(403, "Check-in is personal. Use audited corrections to manage attendance.");
        var employeeId = employee.Id;
        var now = DateTime.UtcNow;
        var open = await store.Attendance.Find(x => x.OrganizationId == tenant.OrganizationId && x.EmployeeId == employeeId && x.CheckInUtc != null && x.CheckOutUtc == null)
            .SortBy(x => x.CheckInUtc).FirstOrDefaultAsync(ct);
        if (open is not null)
            return Conflict(new ProblemDetails
            {
                Status = 409, Title = "Already checked in",
                Detail = $"An open work period exists for {open.WorkDate} (UTC). Check out of that period before checking in again.",
                Extensions = { ["openPeriod"] = AttendanceResponse.From(open) }
            });
        var today = now.ToString("yyyy-MM-dd");
        if (await store.Attendance.Find(x => x.OrganizationId == tenant.OrganizationId && x.EmployeeId == employeeId && x.WorkDate == today).AnyAsync(ct))
            return Conflict(new ProblemDetails { Status = 409, Title = "Attendance already recorded", Detail = "An attendance record already exists for today (UTC). Only one work period per day is supported. An authorized manager or HR administrator can correct the existing record." });
        var row = new Attendance { OrganizationId = tenant.OrganizationId, EmployeeId = employeeId,
            WorkDate = now.ToString("yyyy-MM-dd"), CheckInUtc = now, Notes = request.Notes.Trim(), CreatedByUserId = tenant.UserId, UpdatedByUserId = tenant.UserId };
        await store.Attendance.InsertOneAsync(row, cancellationToken: ct);
        await tx.CommitAsync(ct);
        return Ok(AttendanceResponse.From(row));
    }
    [HttpPost("{employeeId}/{workDate}/check-out")]
    public async Task<IActionResult> CheckOut(string employeeId, string workDate, CancellationToken ct)
    {
        ParseDate(workDate);
        await using var tx = await writeLock.Acquire(ct);
        var employee = await RequireEmployee(employeeId, ct, true);
        if (employee.ApplicationUserId != tenant.UserId) throw new ApiException(403, "Check-out is personal. Use audited corrections to manage attendance.");
        employeeId = employee.Id;
        var now = DateTime.UtcNow;
        var row = await store.Attendance.FindOneAndUpdateAsync(x => x.OrganizationId == tenant.OrganizationId && x.EmployeeId == employeeId && x.WorkDate == workDate && x.CheckInUtc != null && x.CheckInUtc <= now && x.CheckOutUtc == null,
            Builders<Attendance>.Update.Set(x => x.CheckOutUtc, now).Set(x => x.UpdatedAtUtc, now).Set(x => x.UpdatedByUserId, tenant.UserId),
            new FindOneAndUpdateOptions<Attendance> { ReturnDocument = ReturnDocument.After }, ct);
        if (row is null) return Conflict(new ProblemDetails { Status = 409, Title = "No open check-in", Detail = "There is no open check-in for this work date. Refresh attendance to see its current state." });
        await tx.CommitAsync(ct);
        return Ok(AttendanceResponse.From(row));
    }
    [HttpGet("{employeeId}/state")]
    public async Task<IActionResult> State(string employeeId, CancellationToken ct)
    {
        employeeId = (await RequireEmployee(employeeId, ct)).Id;
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        // Deliberately independent of the history date filter, so older open periods remain actionable.
        var open = await store.Attendance.Find(x => x.OrganizationId == tenant.OrganizationId && x.EmployeeId == employeeId && x.CheckInUtc != null && x.CheckOutUtc == null)
            .SortBy(x => x.CheckInUtc).FirstOrDefaultAsync(ct);
        var current = await store.Attendance.Find(x => x.OrganizationId == tenant.OrganizationId && x.EmployeeId == employeeId && x.WorkDate == today).FirstOrDefaultAsync(ct);
        return Ok(new { OpenPeriod = open is null ? null : AttendanceResponse.From(open), TodayRecord = current is null ? null : AttendanceResponse.From(current) });
    }
    [HttpPut("{employeeId}/{workDate}"), Authorize(Policy = "Manager")]
    public async Task<IActionResult> Correct(string employeeId, string workDate, AttendanceCorrectionRequest request, CancellationToken ct)
    {
        var date = ParseDate(workDate);
        var now = DateTime.UtcNow;
        if (date > DateOnly.FromDateTime(now) || request.Status is not ("Present" or "Absent" or "Leave")
            || (request.CheckInUtc.HasValue && request.CheckInUtc.Value.Kind != DateTimeKind.Utc)
            || (request.CheckOutUtc.HasValue && request.CheckOutUtc.Value.Kind != DateTimeKind.Utc)
            || request.CheckInUtc > now || request.CheckOutUtc > now
            || (request.Status == "Present" && request.CheckInUtc is null)
            || (request.Status != "Present" && (request.CheckInUtc != null || request.CheckOutUtc != null))
            || (request.CheckInUtc.HasValue && DateOnly.FromDateTime(request.CheckInUtc.Value) != date)
            || (request.CheckOutUtc.HasValue && (request.CheckInUtc is null || request.CheckOutUtc < request.CheckInUtc)))
            throw new ApiException(400, "Invalid attendance status or UTC work period.");
        await using var tx = await writeLock.Acquire(ct);
        employeeId = (await RequireEmployee(employeeId, ct)).Id;
        if (request.CheckInUtc != null && await store.Attendance.Find(x => x.OrganizationId == tenant.OrganizationId && x.EmployeeId == employeeId && x.WorkDate != workDate && x.CheckInUtc != null
            && (x.CheckOutUtc == null || x.CheckOutUtc > request.CheckInUtc) && (request.CheckOutUtc == null || x.CheckInUtc < request.CheckOutUtc)).AnyAsync(ct))
            throw new ApiException(409, "This work period overlaps another attendance record.");
        var row = await store.Attendance.Find(x => x.OrganizationId == tenant.OrganizationId && x.EmployeeId == employeeId && x.WorkDate == workDate).FirstOrDefaultAsync(ct);
        var isNew = row is null;
        row ??= new Attendance { OrganizationId = tenant.OrganizationId, EmployeeId = employeeId, WorkDate = workDate, CreatedByUserId = tenant.UserId };
        row.Corrections.Add(new AttendanceCorrection { ChangedByUserId = tenant.UserId, ChangedAtUtc = now, PreviousStatus = isNew ? null : row.Status,
            PreviousCheckInUtc = row.CheckInUtc, PreviousCheckOutUtc = row.CheckOutUtc, PreviousNotes = row.Notes, Reason = request.Notes.Trim() });
        row.CheckInUtc = request.CheckInUtc; row.CheckOutUtc = request.CheckOutUtc;
        row.Status = request.Status; row.Notes = request.Notes.Trim(); row.UpdatedAtUtc = now; row.UpdatedByUserId = tenant.UserId;
        if (isNew) await store.Attendance.InsertOneAsync(row, cancellationToken: ct);
        else await store.Attendance.ReplaceOneAsync(x => x.OrganizationId == tenant.OrganizationId && x.Id == row.Id, row, cancellationToken: ct);
        await tx.CommitAsync(ct);
        return Ok(AttendanceResponse.From(row));
    }
    [HttpGet("{employeeId}/{workDate}/history"), Authorize(Policy = "Manager")]
    public async Task<IActionResult> History(string employeeId, string workDate, CancellationToken ct)
    {
        ParseDate(workDate);
        employeeId = (await RequireEmployee(employeeId, ct)).Id;
        var row = await store.Attendance.Find(x => x.OrganizationId == tenant.OrganizationId && x.EmployeeId == employeeId && x.WorkDate == workDate).FirstOrDefaultAsync(ct);
        return row is null ? NotFound() : Ok(row.Corrections.Select(x => new { x.ChangedByUserId, x.ChangedAtUtc, x.PreviousStatus, x.PreviousCheckInUtc, x.PreviousCheckOutUtc, x.PreviousNotes, x.Reason }));
    }
}
