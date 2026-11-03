using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace EmployeeApi.Models;

public sealed class Employee
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string EmployeeNumber { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string OrganizationId { get; set; } = string.Empty;
    public string DepartmentId { get; set; } = string.Empty;
    public string? ManagerEmployeeId { get; set; }
    public string? ApplicationUserId { get; set; }
    public string? ProvisioningUserId { get; set; }
    public string? CreationKey { get; set; }
    public string? CreationFingerprint { get; set; }
    // Retained in MongoDB solely for explicit legacy migration; never used for access.
    [BsonElement("Department"), BsonIgnoreIfNull]
    public string? LegacyDepartment { get; set; }

    public string JobTitle { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Salary { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime HireDate { get; set; }

    public bool IsActive { get; set; } = true;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAtUtc { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAtUtc { get; set; }
}
