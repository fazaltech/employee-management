# Organization EMS/HRM setup and upgrade

This guide supersedes earlier account, tenancy, and database setup notes where they conflict.

## Storage and access

ASP.NET Core 10 Identity, SQLite, MongoDB, and Angular 22 are retained. SQLite defaults to `Data Source=identity.db`; it holds credentials, the organization authorization registry, and one membership per user. Each registry organization has a required, unique owner user foreign key. Unique indexes and SQL triggers prevent extra owners, owner deactivation/removal, and platform memberships. Ownership changes require a separately designed migration; the old ownership-transfer command is retired.

MongoDB holds Organizations (HR profiles, timezone, owner reference and status), Departments, Employees, Attendance, Shifts, ShiftAssignments, Leaves, Salaries, Payroll, and Payslips. The SQLite registry remains authoritative for access and synchronizes organization profiles at initialization. Keeping this small registry in SQLite makes membership/ownership constraints transactional across API instances without requiring a Mongo replica set. All HR reads and writes include OrganizationId. Initialization is repeatable and creates tenant validators and indexes.

PlatformAdmin manages platform records but has no tenant membership. OrganizationOwner manages its organization. HRAdmin manages staff, roles up to HRAdmin, salary/payroll drafts, and existing shift assignments. Only the owner manages departments and creates/edits shifts. Managers see themselves and direct reports. Salary and payslip endpoints show managers/employees only their own compensation. No custom permission-grant system is introduced.

## Configuration

Existing local appsettings files remain ignored and are not overwritten. For a new checkout copy `EmployeeApi/appsettings.example.json` to `EmployeeApi/appsettings.json`. Supply `Jwt:Key` using User Secrets or `Jwt__Key` (at least 32 random characters). Configure MongoDb:ConnectionString and MongoDb:DatabaseName for your installation. SQLite connection overrides use `ConnectionStrings:DefaultConnection`.

For Development/sample use:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Authentication__RequireConfirmedEmail = 'false'
$env:Notifications__EmailEnabled = 'false'
```

Email verification, SMTP delivery, and setup links remain implemented. Enable both flags and configure SMTP for deployment. Production does not create or modify the development platform account. Provision production administration separately through Identity tooling.

The requested initial development password has been saved to this machine's .NET User Secrets under `PlatformAdmin:InitialPassword`. It is not in the repository. On another machine provide that secret through .NET User Secrets or `PLATFORM_ADMIN_INITIAL_PASSWORD`. To avoid shell-history disclosure, use a secure prompt:

```powershell
$secret = Read-Host 'Initial development PlatformAdmin password' -AsSecureString
$env:PLATFORM_ADMIN_INITIAL_PASSWORD = [System.Net.NetworkCredential]::new('', $secret).Password
dotnet run --project EmployeeApi --no-launch-profile -- --initialize-databases
Remove-Item Env:PLATFORM_ADMIN_INITIAL_PASSWORD
```

Initialization creates `platform@peopleops.com`, display name `PlatformAdmin`, using UserManager and the PlatformAdmin role. Existing matching credentials are preserved. Development password policy permits the requested lowercase bootstrap credential; Production retains the uppercase requirement. Initial login is restricted to password change before any business endpoint. Changing/resetting a password invalidates prior JWTs through security-stamp validation. All users must sign in again after upgrading from older JWTs without security stamps.

## Back up and upgrade

Stop application writers first. Back up SQLite using sqlite3's `.backup` command (or SQLite's backup API). Do not copy a live SQLite file without its WAL. Example from the API directory:

```powershell
sqlite3 identity.db ".backup 'identity.before-hrm.backup.db'"
mongodump --uri='mongodb://localhost:27017/' --db=TestDB --out=backups/before-hrm
```

Protect backups as sensitive employee/account data. For a local copy of a stopped database with no WAL, copying the file also works. Restore SQLite with `.restore`; restore Mongo with `mongorestore` into a separately verified destination.

Run `dotnet build EmployeeApi`, then initialize with the command above. Startup also applies pending migrations. `OrganizationHrmWorkflows` adds the password-change flag, owner FK and unique constraints; `OwnershipGuards` installs the triggers after SQLite's table rebuild. Existing organizations migrate only when each has exactly one active owner. Duplicate owners, shared memberships, missing owners, owners linked to employee records, or invalid owner references must be reconciled explicitly before upgrading; nothing is silently deleted or reassigned. SQLite table rebuilds are not fully transactional: retain the backup and stop startup if migration fails.

Existing employee Salary fields are retained for historical preservation but no longer editable or returned as current salary. Create effective-dated salary records, review them, and approve through the new workflow. Legacy unassigned Mongo employees still require the existing explicit tenant migration. Organization creation is now platform-only and requires a new owner account in the same request. Old self-service creation and additional-owner creation are no longer supported.

## Explicit development reset ? permanently deletes data

Reset never runs during normal startup. It refuses Production, non-loopback MongoDB servers, Mongo database names other than the existing sample `TestDB` or names ending `_development`, custom collection names, and SQLite files other than `identity.db`/`identity.development.db` in the API directory. An additional exact database-name confirmation is required. It never drops databases or system/unrelated collections. To clear the existing sample databases instead, use `Data Source=identity.db`, MongoDb__DatabaseName=`TestDB`, and `--confirm-database=TestDB`. Verify the backup and target first.

After backing up and stopping the API, deliberately invoke:

```powershell
Set-Location EmployeeApi
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__DefaultConnection = 'Data Source=identity.development.db'
$env:MongoDb__ConnectionString = 'mongodb://localhost:27017/'
$env:MongoDb__DatabaseName = 'PeopleOps_development'
dotnet run --no-launch-profile -- --reset-development-databases --confirm-permanent-delete --confirm-database=PeopleOps_development
# Recreate schema, indexes and the secret-backed development seed:
dotnet run --no-launch-profile -- --initialize-databases
```

The reset switch, deletion switch, and exact database-name switch constitute the explicit confirmation of permanent deletion. After implementation, the user explicitly requested clean development data. The guarded reset was run against local `identity.db` and `TestDB` after verified backups on 2026-11-03.

## Workflows

- Platform Settings creates an organization and its owner, edits names/status, and resets owner passwords without the old password.
- Organization Settings manages account membership; employee pages provision app access and optionally reset allowed account passwords. Blank update passwords preserve existing credentials. Owners cannot be linked as employees or assigned departments.
- Leave submissions require a direct manager. Manager, HRAdmin, then owner decide in order. Decisions include identity, UTC time and comment; self-approval, duplicate stages and repeated approvers are forbidden. No-manager requests are blocked instead of skipping a stage.
- Shifts include working days (Sunday=0), break minutes, local hours and an organization-timezone snapshot. Overnight shifts are supported. Assignments are effective-dated and cannot overlap; edit an ongoing assignment's end date before assigning its successor. Attendance preserves the established **one period per UTC work date** model; it does not automatically mark late arrivals or calculate overtime from shifts. Check-in/out is personal; corrections remain audited.
- Salary changes start as drafts; owner approval is required. Monthly payroll snapshots approved salaries effective at the **first day of the month**, for active employees. It represents gross pay, without tax, deductions, partial-month proration, or statutory payroll calculations. Remove/recreate a draft when its salary inputs change. Approved payroll and salaries are immutable. Payslip generation follows durable approval and is retryable without duplicates.
- Dashboard pending actions include real leave, salary, and payroll records. New pages show loading, empty and error states, with responsive sidebar navigation.

## Verification

```powershell
dotnet test EmployeeApi.Tests
cd employee-client
npm run build
npm test -- --watch=false
```

Use Node 22.22.3 or 24.15.0+ as required by Angular 22. `python scripts/verify-hrm.py` runs live HTTP checks against a built API and local MongoDB. Each run creates isolated verification databases with random credentials and retains them for inspection; it never truncates or resets existing databases. It checks tenant isolation, role boundaries, initial password change, attendance sequencing, staged leave approval and payroll privacy.

Verification during implementation: backend unit/migration tests and Angular build/test passed; live HTTP checks used isolated databases. A later user-authorized clean reset recreated the development databases, applied all five migrations, and seeded PlatformAdmin. API startup, repeat initialization, and initial login were verified. The prior data is backed up in `.local-backups/before-clean-reset-20261103-173247` (excluded from Git). Browser visual verification was not performed.
