> Current EMS/HRM setup and breaking changes: [HRM-SETUP.md](HRM-SETUP.md). The historical notes below are superseded where they conflict.

# EMS/HRM dashboard

The Angular application's default route and post-login destination are now `/dashboard`. The existing Angular 22 standalone-component architecture, router, JWT authentication, HttpClient services, SCSS tokens, Identity/SQLite and MongoDB stores are retained. No UI, icon, chart, or other package dependency was added. The project had no icon/chart library; a shared SVG icon component and responsive CSS charts provide the visuals, with exact chart values available as text or an accessible table.

## Layout and existing features

- Fixed navy sidebar, desktop collapse, mobile drawer with backdrop, keyboard focus containment, Escape dismissal and active-route highlighting.
- Shared organization selector, employee search (including a mobile search control), pending-action notifications, and signed-in user/profile menu.
- Dashboard KPIs, seven-day attendance chart, department distribution, recent attendance and permission-aware quick actions. Loading, error, retry and empty states use actual API results; no fabricated sample metrics are included.
- Existing employee CRUD remains at `/employees`. `?action=add` opens the authorized employee form; `?q=...` filters the current authorized directory by name, email or employee number.
- Department management is now at `/departments`; attendance uses the existing panel and API at `/attendance`; account/organization controls are under `/settings`. These functions were previously combined on the employee page and are preserved in dedicated views.
- `/hierarchy` presents visible employee/manager relationships. `/reports` reuses the scoped dashboard charts and supports a CSV export of the seven-day attendance counts. Employee-role users do not see these staff navigation entries, and opening these pages directly does not fetch staff report/hierarchy data.
- Leave Management is omitted because the app has no leave-request/approval feature. The existing attendance status named Leave remains available.
- The notification control shows live pending actions, not invented unread-message counts; it does not add a notification delivery/inbox system.

## Data loading and scope

`WorkspaceService` loads existing `GET /api/organizations` and `GET /api/auth/account-options` after authentication. It holds the authorized organization list, selection and platform capability. Selection changes clear and reload page data. Dashboard request subscriptions are cancelled when the selection changes or the view is destroyed. Logout clears workspace state, and responses from previous authentication generations are ignored.

The only new backend endpoint is:

```text
GET /api/organizations/{organizationId}/dashboard
Authorization: Bearer <token>
```

`DashboardController` uses the existing OrganizationMember policy and `IEmployeeService.GetAllAsync`, which already enforces organization-wide owner/HR visibility, manager/direct-report visibility and employee self-only visibility. All Mongo attendance and department queries include the authorized OrganizationId; attendance queries additionally restrict EmployeeId to that visible set. The response is an explicit dashboard DTO and does not contain salary, email, account tokens, or provisioning internals.

Metric definitions:

- **Total / visible employees:** count of employee records visible to the caller, including inactive records.
- **Active employees:** visible records whose IsActive flag is true.
- **Today's attendance:** visible attendance records marked Present for the current server UTC date. This is a count, not an attendance rate or a count of everyone expected at work.
- **Pending actions:** all visible checked-in periods without checkout, plus unfinished app provisioning for owners/HR. It does not imply pending leave requests or an approval workflow.
- **Weekly chart:** recorded Present, Absent and Leave counts for today and the preceding six UTC dates. Missing records are never treated as absences.
- **Department chart:** visible employees grouped by their department, including inactive employees. Empty department groups are omitted from the distribution chart, but departments remain available on the department page.
- **Recent attendance:** up to eight most recently updated records within that seven-day window. Times and work dates are labeled UTC.

PlatformAdmin metrics use the existing `/api/platform/organizations` and `/api/platform/owners` APIs: total/active/suspended organizations and distinct owner accounts. Platform capability never grants access to an organization's employee dashboard. A platform administrator sees tenant data only when they also have an authorized membership.

## Files changed

- `EmployeeApi/Controllers/DashboardController.cs`: scoped dashboard endpoint and response DTOs.
- `employee-client/src/app/app.*`: responsive authenticated application shell.
- `employee-client/src/app/app.routes.ts`: dashboard/default route and dedicated workspace routes.
- `employee-client/src/app/core/services/workspace.service.ts`, `dashboard.service.ts`: shared workspace state and typed dashboard client.
- `employee-client/src/app/shared/icon.ts`: reusable SVG icons.
- `employee-client/src/app/features/dashboard/*`: dashboard and chart components shared by the overview and reports.
- `employee-client/src/app/features/workspace/*`: department, hierarchy, attendance and settings views using existing services/panels.
- `employee-client/src/app/features/employees/employees.*`: shared workspace selection, directory search, deep-linked creation, consistent styling/icons, and relocation of the combined management panels.
- Login/register components: dashboard destination after sign-in.
- `employee-client/src/styles.scss`: restrained teal accent and visible keyboard focus.
- README, this document and `EmployeeApi/EmployeeApi.http`: dashboard documentation and endpoint example.

## Delivery

No additional configuration, database migration, npm package, API credential or sample-data seeding is needed. Deploy/restart the updated API together with the rebuilt frontend so the new dashboard route is available. Existing Identity/Mongo configuration and migration requirements still apply. If no organization is assigned, the dashboard presents the workspace-settings onboarding action.

The API and Angular production builds were used for verification. No tests were added or run, following the existing instruction. The development servers were left stopped; browser layout and live API integration were not exercised during this change. Lists retain the existing unpaginated-directory behavior, so very large organizations may later need server-side aggregation/pagination. Dashboard information refreshes on entry, organization change or explicit Refresh; it is not a live push feed.
