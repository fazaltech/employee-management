namespace EmployeeApi.Infrastructure;

public sealed class EmployeeConflictException(string message) : Exception(message);
