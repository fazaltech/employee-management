# Multi-tenant EMS/HRM delivery

## Changed files and architecture

- `EmployeeApi/Models`: Organization and OrganizationMembership in SQLite; Department and Attendance in MongoDB; tenant, hierarchy, and optional account fields on Employee. ApplicationUser remains an authentication account with no EmployeeId.
- `EmployeeApi/Data/ApplicationDbContext.cs` and the `MultiTenantOrganizations` / `RequireVerifiedMembershipEmail` migrations: organization tables, membership foreign keys and indexes, verified-mailbox access requirement, unique Identity normalized email index, updated EF snapshot.
- `EmployeeApi/Services`: TenantContext/authorization, TenantWriteLock, HrmStore/index initialization, scoped EmployeeService, recoverable EmployeeProvisioningService, SMTP PasswordSetupService.
- `EmployeeApi/Controllers`: tenant-aware EmployeesController plus OrganizationsController, DepartmentsController, AttendanceController and PasswordSetupController.
- `EmployeeApi/DTOs/Employees`: DepartmentId/manager/provisioning inputs and explicit responses that hide provisioning internals and hide salary from employees/managers.
- `EmployeeApi/Program.cs`, configuration and exception handling: policies, dependency lifetimes, token expiration, password email rate limiting and sanitized failures.
- `employee-client/src/app`: tenant-aware services/models, employee form/directory, organization and department controls, membership and attendance panels, password setup page and login link.
- `scripts/migrate-legacy-employees.js`, `EmployeeApi/EmployeeApi.http`, README and this document: migration and API usage.

## Account/password configuration update

See [ACCOUNT-MANAGEMENT.md](ACCOUNT-MANAGEMENT.md) for administrator-set initial/optional update passwords, platform owner management, shared-account restrictions, and sample-data email flags. These rules supersede the original passwordless-only behavior described below. Both confirmation requirements and email sending now default to disabled for sample data; all email code and verification state are retained.

## Flows and permissions

Register/sign in as before. Registration grants no organization access. Create an organization to atomically create its active owner membership. Choose an organization, add departments, then employees with an optional manager. Owners can change organization membership roles and deactivate memberships; the last active owner cannot be removed. Owners and HR can manage employees and departments. PlatformAdmin is a separate Identity role, checked against Identity on each platform request; it grants status management only and does not bypass tenant membership.

Employee and manager visibility requires an active linked employment record. Employees see only themselves and their attendance; managers see themselves and direct reports only (not the recursive hierarchy). Owners/HR see their entire organization. Salary is returned only to owners/HR. Deactivating an employee stops employee/manager self-service; revoke the membership separately to revoke owner/HR access. Deleting linked employees or employees with attendance is blocked; deactivate them to preserve history. Departments in use and managers with reports cannot be deleted.

Every tenant API resolves active SQLite membership and active organization status on the server. An organization route or `X-Organization-Id` header selects context, never grants permission. Unauthenticated requests get 401; invalid/inactive memberships or missing tenant context get 403; invisible employee identifiers get 404. All application Mongo reads/writes include the authorized OrganizationId. Manager assignments validate organization, active manager, self-reference, and ancestor cycles. Writes serialize using a SQLite write transaction so concurrent API requests cannot introduce cycles or delete referenced departments.

Check-in uses server UTC, with one work period per employee/UTC date. An open prior period prevents another check-in; checkout specifies the original work date, including overnight work. Duplicate check-ins/checkouts are rejected. Owners, HR, and direct-report managers can add/correct Present, Absent or Leave records, with UTC timestamp and overlap validation and a required correction reason. Prior values and correcting user are retained in the document and available from the history endpoint.

## App provisioning and recovery

Employee POST requires a UUID `Idempotency-Key`. Persist it with the original body; retry the same body/key after a timeout. The API persists the employee and a payload fingerprint before account creation. Reusing the key with a different body returns 409. Unique tenant employee number, email, creation key and account-link indexes prevent duplicates.

Checking app access on create/update, or POSTing `employees/{id}/app-access`, records provisioning intent, creates a passwordless Identity user if needed, and creates an Employee membership in a SQLite transaction. A matching normalized-email account is reused; existing passwords, roles and revoked memberships are never overwritten. The employee account link is finalized after the Identity transaction commits, under a new write lock. A failure between stores can leave an employee with Pending access or an unused passwordless account/membership. Retry the original POST or the app-access endpoint to finish; no automatic deletion or cross-store rollback is attempted. Keep the employee email fixed after provisioning starts so a retry cannot link a different mailbox. `ProvisionAppAccess=false` does not revoke existing access.

After provisioning, use **Email password link**, or the employee can use **Set or reset your password** from sign-in. Email is deliberately a separate retryable step: SMTP failure cannot roll back or duplicate the employee. API responses never contain setup tokens or passwords. Existing verified accounts continue using their existing credentials. New provisioned memberships require verified email before any tenant access, including when an existing unverified account is reused; completing password setup verifies mailbox ownership. This prevents someone who registered another person's email from inheriting that person's employment access. Owners must complete any pending verification before promotion. Password setup uses Identity reset tokens (one-hour expiry and invalidated by successful reset), emailed to the account address. The client receives the token in the URL fragment and removes it from browser history immediately. Keep Data Protection keys persistent and shared across API instances to support these tokens.

## API changes

Swagger/OpenAPI remains at `/swagger` and `/openapi/v1.json` in Development.

| Routes | Access |
| --- | --- |
| GET/POST `/api/organizations` | Authenticated user; list memberships / create owned organization |
| PUT `/api/organizations/{org}` | Owner: rename |
| GET `/api/organizations/{org}/memberships` | Owner |
| PUT `/api/organizations/{org}/memberships/{userId}` | Owner: role/active state of existing membership |
| PUT `/api/organizations/{org}/status` | PlatformAdmin: Active/Suspended |
| GET/POST `/api/organizations/{org}/departments` | Member read; owner/HR create |
| PUT/DELETE `/api/organizations/{org}/departments/{id}` | Owner/HR |
| GET/POST `/api/organizations/{org}/employees` | Scoped read; owner/HR create |
| GET/PUT/DELETE `/api/organizations/{org}/employees/{id}` | Scoped read; owner/HR writes |
| POST `/api/organizations/{org}/employees/{id}/app-access` | Owner/HR: provision or retry |
| GET `/api/organizations/{org}/attendance?employeeId=...&from=yyyy-MM-dd&to=yyyy-MM-dd` | Scoped employee read; max 94 days |
| POST `/api/organizations/{org}/attendance/check-in` | Scoped employee; body EmployeeId/Notes |
| POST `/api/organizations/{org}/attendance/{employeeId}/{workDate}/check-out` | Scoped employee |
| PUT `/api/organizations/{org}/attendance/{employeeId}/{workDate}` | Owner/HR/authorized manager correction |
| GET `/api/organizations/{org}/attendance/{employeeId}/{workDate}/history` | Owner/HR/authorized manager |
| POST `/api/auth/password-setup/request` | Anonymous, rate limited; body Email |
| POST `/api/auth/password-setup` | Anonymous, rate limited; UserId/Token/Password |

Legacy `/api/employees` routes remain as aliases but now require `X-Organization-Id`. Breaking changes: unscoped access is removed, write permissions are restricted, employee create requires an idempotency UUID, and Department is replaced in API payloads by DepartmentId. ManagerEmployeeId is optional. Employee salary is nullable in responses for restricted roles. Legacy Department remains stored only for migration compatibility. No formal JobLevel model was added because there is no existing job-level workflow; JobTitle stays independent from authorization roles.

## Upgrade and configuration

1. Stop old API instances and back up SQLite (including committed WAL contents using a SQLite backup facility) and MongoDB. Do not run old and new APIs against the same data.
2. Audit `Users.NormalizedEmail` for duplicates before the new unique index. Resolve actual account ownership before merging duplicates; the migration intentionally fails rather than discard users. Pending EF migrations run at API startup, or apply with `dotnet tool run dotnet-ef database update --project EmployeeApi --startup-project EmployeeApi`. The generated migration has not been applied by this implementation session.
3. Create the appropriate organization(s), owner memberships and departments. Existing employees with no OrganizationId are preserved but invisible to all tenant endpoints. No tenant is guessed from an account or email.
4. For legacy data, verify organization ownership in SQLite, stop the API, and explicitly select the employee IDs for each organization. The mongosh script defaults to a validation-only dry run. Run against the configured database and collection, for example:

```powershell
$env:EMS_ORGANIZATION_ID = '<existing-organization-uuid>'
$env:EMS_EMPLOYEE_IDS = '["<employee-object-id>"]'
$env:EMS_EMPLOYEES_COLLECTION = 'Employees'
mongosh 'mongodb://localhost:27017/TestDB' --file scripts/migrate-legacy-employees.js
# After reviewing the intended assignment and backup:
$env:EMS_APPLY = 'yes'
mongosh 'mongodb://localhost:27017/TestDB' --file scripts/migrate-legacy-employees.js
Remove-Item Env:EMS_APPLY
```

The script normalizes employee numbers/emails, creates organization-scoped departments from legacy text, sets DepartmentId, retains legacy Department text, and grants no app accounts. It checks collisions before writes and skips completed records on retry. Use separate explicit employee lists per organization. Preserve database backups for rollback; reverting only the application would reopen global employee access.

5. Mongo collections, validators and indexes are now initialized at startup after platform-account preflight; services retain the idempotent initialization guard. See DATABASE-SETUP.md. Resolve pre-existing index conflicts or duplicates explicitly; indexes are not dropped automatically. Original code did not create global unique indexes, but manually installed global email/number indexes must be reviewed because they would prevent reuse across organizations.
6. Configure SMTP and a browser-facing password setup URL. When Notifications:EmailEnabled=true, blank SMTP settings return 503. When false, sending is skipped without requiring SMTP. No real credentials are checked in:

```powershell
$env:Email__SmtpHost = 'smtp.example.com'
$env:Email__SmtpPort = '587'
$env:Email__EnableSsl = 'true'
$env:Email__From = 'peopleops@example.com'
$env:Email__Username = '<smtp-username>'
$env:Email__Password = '<secret-from-secret-manager>'
$env:Email__PasswordSetupUrl = 'https://your-frontend.example/password-setup'
```

7. Replace the existing development JWT key for production, use TLS, and persist Data Protection keys. The designated platform administrator is now seeded through RoleManager/UserManager at startup; see DATABASE-SETUP.md for the exact account and secure initial-password requirements. No public API or registration path grants PlatformAdmin, and no default password is created.

## Verification and limitations

The .NET API and Angular production builds passed. Angular was built with the cached Node 24.15.0 executable because the shell defaults to unsupported Node 20.13.1. No tests were added, edited, or run, as requested. Existing tests target the old employee model and require an explicitly authorized update before they can verify the new contract. No live database migration, email delivery, or browser/API integration run was performed.

SQLite write transactions serialize tenant writes across instances sharing the same SQLite database, including while MongoDB is accessed. This favors correctness over write throughput and requires all instances to use the same durable Identity database; independently copied SQLite files are unsupported. A crashed or interrupted Mongo write can still have committed even when the HTTP request fails; use the documented retry/status workflow. Mongo writes are document-atomic, not atomically committed with SQLite.

Attendance currently uses UTC work dates, one daily period, direct-report management, and a bounded date-range list. Organization timezones, split shifts, payroll, formal job levels, and recursive reporting permissions are not implemented. SMTP rate limits are per API process and remote IP; a multi-instance deployment should enforce shared rate limits at the trusted gateway. Department/employee lists are currently unpaginated, matching the existing directory architecture.


## Open attendance period handling

Selecting an employee now loads `GET /api/organizations/{org}/attendance/{employeeId}/state` alongside history. The response contains the authorized employee's open period (regardless of the selected date range) and today's UTC record. The UI displays an explicit checkout/correction action and disables new check-in while an open period or today's record exists. Requests remain server-authorized; another tab's conflicting changes return HTTP 409 and refresh the state.

Expected duplicate check-in and missing/already-completed checkout conditions return ProblemDetails directly, avoiding a thrown ApiException for these normal user conflicts. Old records are not auto-closed or deleted. One UTC work period per employee per day remains enforced. API and Angular builds passed; no tests were added or run, and no attendance data was modified during the fix. Rebuild/restart the API debug session to load the updated controller.
