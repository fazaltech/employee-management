> Current EMS/HRM setup and breaking changes: [HRM-SETUP.md](HRM-SETUP.md). The historical notes below are superseded where they conflict.

# Administrator passwords and email configuration

Sample-data defaults in `EmployeeApi/appsettings.json`:

```json
{
  "Authentication": { "RequireConfirmedEmail": false },
  "Notifications": { "EmailEnabled": false }
}
```

The flags are independent. With confirmation disabled, login, organization selection, membership authorization, and owner assignment do not require verified email. EmailConfirmed and RequiresVerifiedEmail are retained, not rewritten to true. With sending disabled, the email service returns before generating tokens, validating SMTP configuration, or connecting to SMTP. The setup-request response explains that email is disabled. Account creation with an administrator-provided password does not require SMTP or successful email delivery.

To restore verification and email, set both flags to true and configure the existing `Email` SMTP settings. Registration then returns 202 with `requiresEmailConfirmation` rather than issuing a token; unconfirmed logins and bearer-token requests are rejected until verification. Existing secure password setup remains available from sign-in and verifies the email after successful completion. Email sending is an explicit, retryable step through the existing password-setup request endpoint; account creation itself does not send email. Choosing confirmation=true with sending=false intentionally requires an externally completed verification; use both false for sample data.

## Accounts and passwords

| Endpoint | Authority | Request |
| --- | --- | --- |
| POST `/api/platform/organizations/{organizationId}/owners` | PlatformAdmin | FullName, Email, optional Password; creates OrganizationOwner |
| PUT `/api/platform/organizations/{organizationId}/owners/{userId}` | PlatformAdmin | FullName, optional Password; target must already be an owner in this organization |
| GET `/api/platform/owners` | PlatformAdmin | List owner accounts and organizations |
| GET `/api/platform/organizations` | PlatformAdmin | List organizations for owner creation |
| POST `/api/organizations/{organizationId}/accounts` | OrganizationOwner | FullName, Email, Role, optional Password |
| PUT `/api/organizations/{organizationId}/accounts/{userId}` | OrganizationOwner | FullName, optional Password |
| PUT `/api/organizations/{organizationId}/memberships/{userId}` | OrganizationOwner | Role, IsActive; existing scoped role-management flow |

Create requires an initial password when email sending is disabled. When sending is enabled, omitting it creates a passwordless account for the existing secure email setup flow. A duplicate email returns 409 and never resets, replaces or takes over the existing account. Creation and membership commit in one SQLite transaction. If a create response is lost, list existing members/owners instead of creating a second account with a new email.

On update, omit `password` (or use null) to retain the existing password hash. An explicitly supplied empty string is rejected. A supplied new password is validated against the configured Identity password validators, then reset using `UserManager.GeneratePasswordResetTokenAsync` and `ResetPasswordAsync`. Creation uses `UserManager.CreateAsync`. There is no shared default password, and neither plaintext passwords nor reset tokens are stored in application documents, logged, or returned. Admin password resets do not mark emails verified. Existing JWTs remain valid until expiry unless blocked by the current account/membership/confirmation checks.

Profile/password changes and account creation return explicit safe projections only. Account email remains immutable through these update endpoints, consistent with linked employee email protection; password and name changes cannot silently redirect an employee's mailbox.

## Scope and hierarchy

OrganizationOwner >= HRAdmin >= Manager >= Employee. No organization endpoint accepts PlatformAdmin as a role. Owners retain the existing ability to manage peer owners, subject to the last-active-owner protection. Target membership and role are checked server-side under the SQLite write lock. Membership updates and employee provisioning also reject PlatformAdmin targets.

Owners can change organization-specific access for members of their organization, but cannot edit profile/password for an account that has **any** membership in another organization, including inactive memberships. A global password change would affect that other organization. PlatformAdmin owner management can update shared owner accounts, but these endpoints never manage PlatformAdmin accounts. Shared non-owner accounts use the retained user-controlled password setup mechanism when email is enabled; there is no tenant-admin bypass.

Employee create/update also accepts optional `password` with `provisionAppAccess=true`. HR retains its existing ability to provision Employee accounts; changing an existing linked password additionally checks target role, organization, and the shared-account restriction. No-password updates leave credentials unchanged. A create retry reuses the reserved account without resetting its password. Existing email accounts are never overwritten by an initial-password create request: omit the password to link an already-passworded account, or use an authorized explicit update. Passwords are excluded even from persisted employee idempotency fingerprints. Cross-store recovery remains as documented in MULTITENANCY.md; an employee update may persist before a later account-provisioning failure, so inspect Pending status and retry.

## Frontend and delivery

The organization access panel supports new accounts and optional password updates. The employee form supports initial/new passwords. Platform administrators see a separate owner-account panel, with organization selection. Blank update password fields are omitted from requests and passwords are cleared after successful saves. They are never stored in browser local storage. Password-setup and registration UI retain the email workflow and show server messages for disabled email or required verification.

Changed areas: AccountsController and AccountAdministrationService (new); authentication, membership and employee provisioning controllers/services; tenant authorization and write-lock checks; Program configuration; appsettings flags; Angular account, employee and platform panels, auth handling; API request examples and documentation. No schema change or new migration is needed. API and Angular builds are used for verification; no tests were added or run, and no servers were started or account credentials changed during implementation.
