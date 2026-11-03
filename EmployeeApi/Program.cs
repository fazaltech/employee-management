using System.Text;
using EmployeeApi.Configuration;
using EmployeeApi.Data;
using EmployeeApi.Infrastructure;
using EmployeeApi.Models;
using EmployeeApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "Employee API";
        document.Info.Version = "v1";
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Enter the access token returned by login or registration."
        };
        return Task.CompletedTask;
    });
    options.AddOperationTransformer((operation, context, cancellationToken) =>
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
            });
        }
        return Task.CompletedTask;
    });
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = builder.Configuration.GetValue("Authentication:RequireConfirmedEmail", true);
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

var jwtSection = builder.Configuration.GetSection(JwtSettings.SectionName);
var jwtSettings = jwtSection.Get<JwtSettings>()
    ?? throw new InvalidOperationException("JWT settings are missing.");

builder.Services
    .AddOptions<JwtSettings>()
    .Bind(jwtSection)
    .Validate(settings => settings.Key.Length >= 32, "JWT key must contain at least 32 characters.")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await users.GetUserAsync(context.Principal!);
                if (user is null || (builder.Configuration.GetValue("Authentication:RequireConfirmedEmail", true) && !user.EmailConfirmed))
                    context.Fail("Account is unavailable or email confirmation is required.");
            }
        };
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<TenantWriteLock>();
builder.Services.AddScoped<IAuthorizationHandler, TenantAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("OrganizationMember", p => p.RequireAuthenticatedUser().AddRequirements(new TenantRequirement(OrganizationRoles.All)));
    options.AddPolicy("OrganizationOwner", p => p.RequireAuthenticatedUser().AddRequirements(new TenantRequirement([OrganizationRoles.Owner])));
    options.AddPolicy("HRAdmin", p => p.RequireAuthenticatedUser().AddRequirements(new TenantRequirement([OrganizationRoles.Owner, OrganizationRoles.HR])));
    options.AddPolicy("Manager", p => p.RequireAuthenticatedUser().AddRequirements(new TenantRequirement([OrganizationRoles.Owner, OrganizationRoles.HR, OrganizationRoles.Manager])));
    options.AddPolicy("Employee", p => p.RequireAuthenticatedUser().AddRequirements(new TenantRequirement(OrganizationRoles.All)));
    options.AddPolicy("PlatformAdmin", p => p.RequireAuthenticatedUser().RequireAssertion(async context =>
    {
        var http = context.Resource as HttpContext;
        var manager = http?.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = manager == null ? null : await manager.GetUserAsync(context.User);
        return user != null && await manager!.IsInRoleAsync(user, "PlatformAdmin");
    }));
});
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(1));
builder.Services.AddScoped<PasswordSetupService>();
builder.Services.AddScoped<AccountAdministrationService>();
builder.Services.AddScoped<PlatformAdminSeeder>();
builder.Services.AddScoped<DatabaseInitializer>();
builder.Services.AddScoped<PlatformOwnershipTransfer>();
builder.Services.AddScoped<EmployeeProvisioningService>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("password-setup", context => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        { PermitLimit = 5, Window = TimeSpan.FromMinutes(15), QueueLimit = 0 }));
});
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

builder.Services
    .AddOptions<MongoDbSettings>()
    .Bind(builder.Configuration.GetSection(MongoDbSettings.SectionName))
    .ValidateDataAnnotations()
    .Validate(settings => new[] { settings.EmployeesCollectionName, settings.DepartmentsCollectionName, settings.AttendanceCollectionName }.Distinct(StringComparer.Ordinal).Count() == 3,
        "MongoDB employee, department and attendance collection names must be distinct.")
    .ValidateOnStart();

builder.Services.AddSingleton<IMongoClient>(serviceProvider =>
{
    var settings = serviceProvider.GetRequiredService<IOptions<MongoDbSettings>>().Value;
    var clientSettings = MongoClientSettings.FromConnectionString(settings.ConnectionString);
    clientSettings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
    return new MongoClient(clientSettings);
});
builder.Services.AddSingleton<HrmStore>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularClient", policy =>
        policy.WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

try
{
    using var scope = app.Services.CreateScope();
    if (args.Contains("--transfer-platform-ownership", StringComparer.Ordinal))
    {
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync(app.Lifetime.ApplicationStopping);
        await scope.ServiceProvider.GetRequiredService<PlatformOwnershipTransfer>().TransferAsync(app.Lifetime.ApplicationStopping);
        app.Logger.LogInformation("Explicit ownership transfer completed. Run --initialize-databases to finish platform setup.");
        await app.DisposeAsync();
        return;
    }
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(app.Lifetime.ApplicationStopping);
}
catch (DatabaseSetupException exception)
{
    app.Logger.LogCritical("Database initialization stopped: {SetupMessage}", exception.Message);
    Environment.ExitCode = 1;
    await app.DisposeAsync();
    return;
}
catch (Exception exception)
{
    // Database exceptions can contain connection strings, document contents, or credentials.
    app.Logger.LogCritical("Database initialization failed ({ExceptionType}). Check database availability, migration/index conflicts and configured permissions. Existing data is not recreated automatically.", exception.GetType().Name);
    Environment.ExitCode = 1;
    await app.DisposeAsync();
    return;
}

if (args.Contains("--initialize-databases", StringComparer.Ordinal))
{
    await app.DisposeAsync();
    return;
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("../openapi/v1.json", "Employee API v1");
        options.DocumentTitle = "Employee API - Swagger";
    });
}

app.UseCors("AngularClient");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/", () => Results.Ok(new
{
    service = "Employee API",
    version = "v1",
    openApi = "/openapi/v1.json"
})).AllowAnonymous();

app.Run();

public partial class Program;
