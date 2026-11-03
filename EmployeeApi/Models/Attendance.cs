using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace EmployeeApi.Models;

public sealed class Attendance
{
    public List<AttendanceCorrection> Corrections { get; set; } = [];
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string OrganizationId { get; set; } = "";
    public string EmployeeId { get; set; } = "";
    public string WorkDate { get; set; } = "";
    public DateTime? CheckInUtc { get; set; }
    public DateTime? CheckOutUtc { get; set; }
    public string Status { get; set; } = "Present";
    public string Notes { get; set; } = "";
    public string CreatedByUserId { get; set; } = "";
    public string UpdatedByUserId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AttendanceCorrection
{
    public string ChangedByUserId { get; set; } = "";
    public DateTime ChangedAtUtc { get; set; }
    public string? PreviousStatus { get; set; }
    public DateTime? PreviousCheckInUtc { get; set; }
    public DateTime? PreviousCheckOutUtc { get; set; }
    public string PreviousNotes { get; set; } = "";
    public string Reason { get; set; } = "";
}
