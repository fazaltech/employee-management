using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace EmployeeApi.Models;

public abstract class TenantDocument
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString();
    public string OrganizationId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string CreatedByUserId { get; set; } = "";
}
// SQLite retains the authorization registry; this document holds organization HR settings.
public sealed class OrganizationProfile : TenantDocument
{
    public string OwnerUserId { get; set; } = "";
    public string Status { get; set; } = "Active";
    public string Name { get; set; } = "";
    public string TimeZone { get; set; } = "UTC";
}
public sealed class Shift : TenantDocument
{
    public string Name { get; set; } = "";
    public string StartTime { get; set; } = "09:00";
    public string EndTime { get; set; } = "17:00";
    public int BreakMinutes { get; set; }
    public int[] WorkingDays { get; set; } = [1, 2, 3, 4, 5];
    public string TimeZone { get; set; } = "UTC";
}
public sealed class ShiftAssignment : TenantDocument
{
    public string EmployeeId { get; set; } = "";
    public string ShiftId { get; set; } = "";
    public string EffectiveFrom { get; set; } = "";
    public string? EffectiveTo { get; set; }
}
public sealed record LeaveDecision(string Stage, string ApproverUserId, bool Approved, DateTime DecidedAtUtc, string Comment);
public sealed class LeaveRequest : TenantDocument
{
    public string EmployeeId { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "PendingManager";
    public List<LeaveDecision> Decisions { get; set; } = [];
}
public sealed class SalaryRecord : TenantDocument
{
    public string EmployeeId { get; set; } = "";
    [BsonRepresentation(BsonType.Decimal128)] public decimal Amount { get; set; }
    public string Currency { get; set; } = "PKR";
    public string EffectiveFrom { get; set; } = "";
    public string Status { get; set; } = "Draft";
    public string? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
}
public sealed record PayrollLine(string EmployeeId, string SalaryId, [property: BsonRepresentation(BsonType.Decimal128)] decimal Amount, string Currency);
public sealed class Payroll : TenantDocument
{
    public string Period { get; set; } = "";
    public string Status { get; set; } = "Draft";
    public List<PayrollLine> Lines { get; set; } = [];
    public string? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
}
public sealed class Payslip : TenantDocument
{
    public string PayrollId { get; set; } = "";
    public string EmployeeId { get; set; } = "";
    public string Period { get; set; } = "";
    [BsonRepresentation(BsonType.Decimal128)] public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
}
