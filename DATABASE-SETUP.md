# Database initialization and platform administrator

The app remains ASP.NET Core 10 with ASP.NET Core Identity / EF Core 10.0.11 / SQLite and MongoDB.Driver 3.11.1. Existing EF migrations and connection settings are reused; no new schema migration is necessary for the seeder.

## Startup order

Normal startup and `--initialize-databases` both run:

1. Apply pending EF migrations to the configured Identity SQLite database.
2. Preflight the platform account's ID, normalized email, username, existing memberships, and (only for a new account) the initial password policy.
3. Create missing Mongo HR collections, enforce OrganizationId validation, and create missing indexes.
4. Recheck Identity conflicts under a SQLite transaction; create the PlatformAdmin role if necessary, create the user only if absent, and ensure its role assignment. Commit these Identity changes together.

Failure exits with a nonzero status before serving requests. Initialization never drops databases, truncates collections, removes indexes, changes another account's ID, resets an existing password, or creates an organization/customer membership. A failure during Mongo setup may leave already-created collections/indexes; rerunning safely completes the remaining work. Mongo and SQLite cannot commit as one distributed transaction, so Identity seeding occurs after Mongo setup succeeds.

The fixed platform account is:

- ID: `87534509-ce34-4f36-be3d-ebac2a367859`
- Email and UserName: `admin@peopleops.biz`
- Identity role: `PlatformAdmin`
- New-account FullName: `PeopleOps Administrator`; CreatedAtUtc: current UTC time.

UserManager performs normal normalization and password hashing. Existing matching accounts keep their password, FullName, CreatedAtUtc and EmailConfirmed. Matching identity fields may be canonicalized through UserManager; no Identity-table inserts or manual password hashes are used. Missing initial-password secrets are acceptable only when the matching account already exists.

## Separate platform account

The designated platform account is now `admin@peopleops.biz`, with a new stable user ID. The existing `micheal.cole@mail.com` account and its ownership of SoftTech and NorthStar Studio are preserved. No ownership transfer is required. The platform account receives only the global PlatformAdmin role, with no organization/customer memberships.

## Configure the databases

From the repository root:

```powershell
$env:ConnectionStrings__DefaultConnection = 'Data Source=identity.db'
$env:MongoDb__ConnectionString = 'mongodb://localhost:27017/'
$env:MongoDb__DatabaseName = 'TestDB'
$env:MongoDb__EmployeesCollectionName = 'Employees'
$env:MongoDb__DepartmentsCollectionName = 'Departments'
$env:MongoDb__AttendanceCollectionName = 'Attendance'
```

These match the checked-in local defaults. Never commit production connection strings or credentials. Collection names must be distinct. Start your existing Mongo service, or run `docker compose up -d mongodb` if using the supplied Compose setup.

SQLite uses a relative filename: run the commands below through `dotnet run --project EmployeeApi`, which uses the project directory. When launching the compiled DLL directly, use `EmployeeApi` as the working directory or supply an absolute SQLite path to avoid creating a second database.

## Supply a password for first-time creation only

Use a secret provider or the process environment. This PowerShell example prompts without echoing the password and does not put it in command history:

```powershell
$platformPassword = Read-Host 'Initial platform administrator password' -AsSecureString
$platformPasswordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($platformPassword)
try {
    $env:PLATFORM_ADMIN_INITIAL_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($platformPasswordPointer)
    dotnet run --project EmployeeApi --launch-profile http -- --initialize-databases
} finally {
    Remove-Item Env:PLATFORM_ADMIN_INITIAL_PASSWORD -ErrorAction SilentlyContinue
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($platformPasswordPointer)
    $platformPassword.Dispose()
}
```

The environment variable takes precedence over `PlatformAdmin:InitialPassword` in configuration. A UserSecretsId is now configured in the project: during Development, Visual Studio's **Manage User Secrets** can store `PlatformAdmin:InitialPassword` outside the repository. Development user secrets are not a production secret vault. Do not print secrets, pass real passwords as command-line literals, or add them to appsettings.

The current Identity policy requires at least eight characters, including uppercase, lowercase and a digit. The seeder executes Identity password validators and reports validation codes without including the password. After successful creation the initial secret can be removed. Subsequent startup never uses it to reset the account.

## Initialize and start

With the first-time secret supplied securely (or after the account already exists):

```powershell
dotnet run --project EmployeeApi --launch-profile http -- --initialize-databases
```

The optional `--transfer-platform-ownership` operator command remains available only for an explicit future migration of memberships held by the designated platform account. It is not needed for this separate admin account and is never run on ordinary startup. It does not transfer Micheal's existing organization ownership.

The initialization-only command exits without opening an HTTP listener. After successful setup, start normally with `dotnet run --project EmployeeApi --launch-profile http`.

To apply only Identity schema migrations, use the existing workflow:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef database update --project EmployeeApi --startup-project EmployeeApi
```

The EF tool applies schema changes; it does not execute runtime platform seeding. If Visual Studio locks the running API's output, stop its debug session before building, or build to a separate output directory. This implementation was compiled successfully using `dotnet build EmployeeApi/EmployeeApi.csproj --no-restore -p:UseAppHost=false -o EmployeeApi/bin/database-setup`.

## Mongo collections and validation

Employees, Departments and Attendance collections are initialized even when empty. Organizations and OrganizationMemberships already live in SQLite; no duplicate Mongo Organizations collection or fake customer organization is created. Identity credentials and roles remain exclusively in SQLite.

All new or updated HR documents must have a nonempty string OrganizationId. New collections receive a strict JSON-schema validator. Existing custom validators are preserved and combined with the tenant requirement. Validation applies to future writes and does not rewrite or delete legacy unassigned documents; they remain invisible to tenant APIs until explicitly migrated with the existing legacy migration plan.

Indexes include tenant-unique employee numbers/emails, creation idempotency keys and account links; organization/manager and organization/department lookups; tenant-unique department names; tenant/employee/work-date attendance uniqueness; organization/work-date and organization/employee/open-period lookups. Existing index names are retained. Unassigned legacy employee records remain excluded from the tenant-unique number/email indexes. Duplicate assigned records or incompatible existing indexes stop initialization rather than deleting data or dropping constraints.

The Mongo principal needs permission to list/create collections, create indexes and modify collection validation (`collMod`). Concurrent collection creation is handled idempotently. Startup diagnostics avoid dumping driver exceptions that could contain credentials or HR data.

## Email and tenant authorization

The existing sample-data flags remain:

```json
{
  "Authentication": { "RequireConfirmedEmail": false },
  "Notifications": { "EmailEnabled": false }
}
```

Email fields, password setup, verification and SMTP implementation are preserved. New platform accounts remain EmailConfirmed=false. Re-enable both flags and configure SMTP when ready to require confirmation and deliver email; startup never marks users verified to bypass that flow.

PlatformAdmin remains an Identity role, checked server-side for platform operations. OrganizationOwner/HRAdmin/Manager/Employee remain membership roles. The seeder grants no membership or tenant bypass. Existing tenant policy handlers resolve membership and every HR Mongo query uses the authorized organization scope.

## Files and verification

- `Data/PlatformAdminSeeder.cs`: fixed-ID Identity seeding, conflict checks, secret handling, transactional role assignment.
- `Data/DatabaseInitializer.cs`: migration/preflight/Mongo/seed ordering.
- `Data/PlatformOwnershipTransfer.cs`: explicit one-time ownership-transfer command.
- `Services/HrmStore.cs`: repeated-safe collection validation and indexes, reused at startup and by existing services.
- `Program.cs`: dependency registration, startup initialization, safe failure messages, initialization/transfer CLI modes.
- `Configuration/MongoDbSettings.cs`, `appsettings.json`: configurable department/attendance collection names; existing SQLite and sample-data email settings preserved.
- `EmployeeApi.csproj`: development UserSecretsId.
- README and this file: setup instructions.

Build succeeded with zero warnings/errors. No tests were added or run. The previous attempt targeting Micheal stopped safely at the membership check. The separate admin account avoids that conflict without changing organization ownership.

## Completed local initialization

The separate `admin@peopleops.biz` account was created successfully through UserManager with the operator-supplied password entered through masked console input. Its role is PlatformAdmin, its organization membership count is zero, and EmailConfirmed remains false. Both existing Micheal organization-owner memberships were preserved. Mongo collection/validator/index initialization completed successfully. The temporary password environment variable was cleared; no password was added to source, configuration or documentation.
