# Authentication & Authorization design (Phase 6)

## Tokens
- Access token: JWT (HS256), 15 min, claims `sub`, `role`, `name`, `jti`. Signing key only from environment (`Jwt__SigningKey`, >= 32 bytes; app refuses to start without it).
- Refresh token: 48 random bytes, 14 days, stored only as SHA-256 hash. Rotated on every use. Presenting an already-rotated token revokes **all** of that user's sessions. Mobile stores tokens in Keychain/Keystore.
- Known trade-off: two simultaneous refreshes from the same device can trip reuse detection and force a re-login.

## Admin password reset (FRD s.2.1)
`POST /auth/admin/forgot-password` -> always 202 (no account enumeration). Admin only; emailed 14-char temporary password, valid 6h, single-use, stored hashed; requesting a new one invalidates older ones. `POST /auth/admin/reset-password` validates it, sets the new password, revokes all sessions.

## Members (FRD s.2.2)
No self-service reset **and no self-service password change**: only the Admin can set a member's password (`POST /users/{id}/set-password`), which also ends the member's sessions. *Interpretation to confirm:* the FRD forbids "self-service password reset"; I extended this to password change as well. Say so if members should be able to change their own password with the current one.

## Permissions
- Business access = active `BusinessMembers` row; capability = `BusinessPermissions` flags (deny by default). Admin: everything. Employee: view permissions only, regardless of flags.
- `[RequireBusinessPermission(Permission.X)]` on routes with `{businessId}`: unauthenticated 401; business missing or not assigned 404 (existence hidden); permission missing 403.
- Own-record rule: `BusinessAccess.CanModifyOwn(createdBy, ownPermission)`. `ViewMemberWiseExpenses` never grants edit/delete.
- `GET /businesses/{id}/my-permissions` returns the effective permission map for the app UI; the server re-checks every action.

## Hardening
- PBKDF2-SHA256 (600k iterations, per-password salt), constant-time compare, timing-equalised unknown-user login.
- Rate limit on login/refresh/forgot/reset: 10/min/IP (configurable). Behind a reverse proxy, forwarded-header handling must be configured at deployment so the real client IP is used.
- No account lockout counter yet (no column in the approved schema); rate limiting is the current brute-force control.
- Login, password events logged to `AuditLogs`; failed logins logged without passwords.
