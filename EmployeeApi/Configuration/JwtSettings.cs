using System.ComponentModel.DataAnnotations;

namespace EmployeeApi.Configuration;

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    [Required]
    public string Key { get; init; } = string.Empty;

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    [Range(5, 1440)]
    public int ExpirationMinutes { get; init; } = 120;
}
