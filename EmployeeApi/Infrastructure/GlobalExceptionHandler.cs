using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace EmployeeApi.Infrastructure;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            ApiException api => (api.Status, "Request could not be completed", api.Message),
            Microsoft.EntityFrameworkCore.DbUpdateException { InnerException: Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 } } => (409, "Account or membership conflict", "A conflicting record exists. Retry the operation or resolve the duplicate."),
            Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 5 or 6 } => (503, "Storage is busy", "Retry the operation shortly using the same creation key where applicable."),
            MongoWriteException write when write.WriteError.Category == ServerErrorCategory.DuplicateKey => (409, "Duplicate record", "A record with these unique fields already exists in this organization."),
            EmployeeConflictException conflict => (
                StatusCodes.Status409Conflict,
                "Employee conflict",
                conflict.Message),
            MongoException or TimeoutException => (
                StatusCodes.Status503ServiceUnavailable,
                "MongoDB is unavailable",
                "Check that MongoDB is running at the configured connection string."),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Unexpected server error",
                "An unexpected error occurred while processing the request.")
        };

        if (status >= 500)
        {
            logger.LogError("Request failed with status {StatusCode}; exception type {ExceptionType}", status, exception.GetType().Name);
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path
        }, cancellationToken);

        return true;
    }
}
