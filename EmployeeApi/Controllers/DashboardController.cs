using EmployeeApi.Models;
using EmployeeApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace EmployeeApi.Controllers;

public sealed record DashboardDay(string Date, int Present, int Absent, int Leave);
public sealed record DashboardDepartment(string Id, string Name, int Employees);
public sealed record DashboardAttendance(string EmployeeId, string EmployeeName, string Department,
    string WorkDate, DateTime? CheckInUtc, DateTime? CheckOutUtc, string Status);
public sealed record DashboardAction(string Label, int Count, string Destination);
public sealed record DashboardResponse(DateTime GeneratedAtUtc, string Scope, int TotalEmployees,
    int ActiveEmployees, int TodayAttendance, int PendingActions, IReadOnlyList<DashboardDay> WeeklyAttendance,
    IReadOnlyList<DashboardDepartment> Departments, IReadOnlyList<DashboardAttendance> RecentAttendance,
    IReadOnlyList<DashboardAction> Actions);

[ApiController, Authorize(Policy = "OrganizationMember"), Route("api/organizations/{organizationId}/dashboard")]
public sealed class DashboardController(IEmployeeService employees, HrmStore store, TenantContext tenant) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardResponse>> Get(CancellationToken ct)
    {
        // Reuse the same owner/HR, direct-report manager, and self-only employee visibility rules.
        var visible = await employees.GetAllAsync(ct);
        var byId = visible.ToDictionary(x => x.Id);
        var departments = await store.Departments.Find(x => x.OrganizationId == tenant.OrganizationId).ToListAsync(ct);
        var departmentNames = departments.ToDictionary(x => x.Id, x => x.Name);
        var now = DateTime.UtcNow;
        var today = now.ToString("yyyy-MM-dd");
        var start = now.Date.AddDays(-6).ToString("yyyy-MM-dd");
        var f = Builders<Attendance>.Filter;
        var scope = f.Eq(x => x.OrganizationId, tenant.OrganizationId) & f.In(x => x.EmployeeId, byId.Keys);
        // This bounded window powers both the chart and recent activity; it never reads another tenant.
        var week = await store.Attendance.Find(scope & f.Gte(x => x.WorkDate, start) & f.Lte(x => x.WorkDate, today))
            .SortByDescending(x => x.UpdatedAtUtc).ToListAsync(ct);
        var openCount = (int)await store.Attendance.CountDocumentsAsync(scope & f.Ne(x => x.CheckInUtc, null) & f.Eq(x => x.CheckOutUtc, null), cancellationToken: ct);
        var pendingAccess = tenant.IsHR ? visible.Count(x => x.ProvisioningUserId != null && x.ApplicationUserId == null) : 0;
        var actions = new List<DashboardAction> { new("Open attendance periods", openCount, "/attendance") };
        if (tenant.IsHR) actions.Add(new("App access awaiting completion", pendingAccess, "/employees"));
        var days = Enumerable.Range(0, 7).Select(i => {
            var date = now.Date.AddDays(i - 6).ToString("yyyy-MM-dd");
            var rows = week.Where(x => x.WorkDate == date).ToArray();
            return new DashboardDay(date, rows.Count(x => x.Status == "Present"), rows.Count(x => x.Status == "Absent"), rows.Count(x => x.Status == "Leave"));
        }).ToArray();
        var departmentCounts = visible.GroupBy(x => x.DepartmentId).Select(g => new DashboardDepartment(g.Key,
            departmentNames.GetValueOrDefault(g.Key, "Unassigned"), g.Count())).OrderByDescending(x => x.Employees).ToArray();
        var recent = week.Take(8).Select(x => new DashboardAttendance(x.EmployeeId,
            $"{byId[x.EmployeeId].FirstName} {byId[x.EmployeeId].LastName}",
            departmentNames.GetValueOrDefault(byId[x.EmployeeId].DepartmentId, "Unassigned"), x.WorkDate,
            x.CheckInUtc, x.CheckOutUtc, x.Status)).ToArray();
        return Ok(new DashboardResponse(now, tenant.IsHR ? "Organization" : tenant.Role == OrganizationRoles.Manager ? "Your team" : "Your records",
            visible.Count, visible.Count(x => x.IsActive), week.Count(x => x.WorkDate == today && x.Status == "Present"),
            openCount + pendingAccess, days, departmentCounts, recent, actions));
    }
}
