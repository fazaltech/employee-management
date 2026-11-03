using System.ComponentModel.DataAnnotations;

namespace EmployeeApi.Configuration;

public sealed class MongoDbSettings
{
    public const string SectionName = "MongoDb";

    [Required]
    public string ConnectionString { get; init; } = string.Empty;

    [Required]
    public string DatabaseName { get; init; } = string.Empty;

    [Required]
    public string EmployeesCollectionName { get; init; } = "Employees";

    [Required]
    public string DepartmentsCollectionName { get; init; } = "Departments";

    [Required]
    public string AttendanceCollectionName { get; init; } = "Attendance";
}
