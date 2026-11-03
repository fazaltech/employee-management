# Employee Management

A multi-tenant EMS/HRM application with ASP.NET Core 10, ASP.NET Core Identity, EF Core/SQLite, MongoDB, JWT authentication, and Angular 22.

## Architecture

- `EmployeeApi` — .NET 10 Web API.
- `EmployeeApi.Tests` — xUnit unit tests.
- `employee-client` — Angular 22 standalone application.
- SQLite stores Identity users, roles, claims, logins, and tokens. Passwords are stored only as Identity password hashes.
- MongoDB stores the `Employees` collection in `TestDB` at `mongodb://localhost:27017/`.

## Prerequisites

- .NET SDK 10
- Node.js `22.22.3`, `24.15.0`, or newer supported release (Angular 22 does not run on Node 20)
- npm 8+
- MongoDB on port `27017`, or Docker Desktop

## Database and platform setup

See [DATABASE-SETUP.md](DATABASE-SETUP.md) before starting the updated API. Startup applies Identity migrations, validates the platform account, initializes Mongo collections/indexes, and seeds the PlatformAdmin role/account. The designated platform account is `admin@peopleops.biz`; Micheal retains his existing organization ownership. No ownership transfer is needed. New installations require `PLATFORM_ADMIN_INITIAL_PASSWORD` from a secure source; matching existing accounts keep their password.

## Run locally

Start MongoDB directly, or use Docker:

```powershell
docker compose up -d mongodb
```

Restore tools/packages and run the API:

```powershell
dotnet tool restore
dotnet restore
dotnet run --project EmployeeApi --launch-profile http
```

The API runs at `http://localhost:5286`. Swagger UI is at `http://localhost:5286/swagger`, and its OpenAPI document is at `http://localhost:5286/openapi/v1.json`. Both documentation endpoints are available in Development. The EF migration is applied automatically and creates the local Identity database on first start.

In Swagger UI, use the register or login endpoint to obtain an access token, click **Authorize**, and paste the token without the `Bearer` prefix. You can then try the protected endpoints.

In a second terminal, run Angular:

```powershell
cd employee-client
npm install
npm start
```

Open `http://localhost:4200`, create an account, and then manage employees. A ready-to-run API request collection is in `EmployeeApi/EmployeeApi.http`.

## Dashboard

The home page is now `/dashboard`, with role-aware metrics, charts, employee search, responsive navigation and dedicated department, hierarchy, attendance, reports and settings views. See [DASHBOARD.md](DASHBOARD.md) for data definitions, changed files and delivery notes.

## Multi-tenant workflows and upgrade

See [MULTITENANCY.md](MULTITENANCY.md) for the changed files, role rules, API routes, retry behavior, migration instructions, SMTP configuration, and limitations.

Sign in, create or select an organization, add departments, and manage its employees and attendance. The creator receives an OrganizationOwner membership. Provision employee app access optionally, then email a secure password setup link. Owners manage roles in the Organization access panel. Managers see their own records and direct reports; employees see only themselves.

Existing MongoDB employees remain preserved but hidden until explicitly assigned to an organization using the documented migration script. Do not assume upgrading automatically assigns old data. The API retains `/api/employees` aliases but requires an authorized `X-Organization-Id`; new clients use `/api/organizations/{id}/employees`.

See [ACCOUNT-MANAGEMENT.md](ACCOUNT-MANAGEMENT.md) for administrator password flows and the sample-data flags `Authentication:RequireConfirmedEmail=false` and `Notifications:EmailEnabled=false`.

## Configuration

Defaults live in `EmployeeApi/appsettings.json`. For deployed environments, override secrets and connection strings with environment variables or a secret manager:

```powershell
$env:Jwt__Key = "use-a-long-random-production-secret-of-at-least-32-characters"
$env:MongoDb__ConnectionString = "mongodb://localhost:27017/"
$env:MongoDb__DatabaseName = "TestDB"
$env:ConnectionStrings__DefaultConnection = "Data Source=identity.db"
```

The checked-in JWT key is only for local development and must be replaced before deployment.

## EF Core migrations

The initial migration has already been applied to `EmployeeApi/identity.db`. The new MultiTenantOrganizations migration adds organizations, memberships and unique normalized account emails; audit existing duplicate emails before upgrading. The API also applies pending migrations automatically at startup. After changing an Identity/EF model, create and apply the next migration with:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations add MigrationName --project EmployeeApi --startup-project EmployeeApi
dotnet tool run dotnet-ef database update --project EmployeeApi --startup-project EmployeeApi
```

MongoDB does not use EF migrations. Tenant indexes are initialized before HR operations; use the explicit legacy-data migration described in MULTITENANCY.md.

## Build

```powershell
dotnet build EmployeeApi/EmployeeApi.csproj
cd employee-client
npm run build
```

Use a supported Node version listed above. No tests were added or run for the multi-tenant implementation, as requested.
