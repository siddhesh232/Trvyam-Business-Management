# 2. Proposed Solution Architecture

```
React Native (TS, Redux Toolkit, React Navigation, Axios)
        │  HTTPS / JSON, JWT bearer
        ▼
ASP.NET Core Web API (.NET 10)  ──  Application (use cases, DTOs, FluentValidation)
        │                              Domain (entities, rules, enums)
        ▼                              Infrastructure (EF Core, email, PDF, storage)
SQL Server (EF Core migrations)
```

## Backend layering (Clean Architecture)
- **Domain** - entities, enums, money/GST calculation rules, domain exceptions. No dependencies.
- **Application** - services per module, DTOs, validators, interfaces (`IAppDbContext`, `ICurrentUser`, `IPermissionService`, `IEmailSender`, `IInvoicePdfGenerator`).
- **Infrastructure** - `AppDbContext`, Fluent configs, migrations, interceptors (audit + soft delete), email, PDF (QuestPDF - *licence check needed, see open decisions*), file storage.
- **API** - controllers, auth, authorization handlers, exception middleware, Serilog, Swagger.

## Cross-cutting
- **AuthZ:** custom `IAuthorizationHandler` + `[RequirePermission(Permission.X)]` resolving *business id from route*; every query filtered through `IBusinessScope` so users never touch unassigned businesses (returns 404, not 403, to avoid leaking existence). Own-record rules checked in services.
- **Tokens:** 15-min access JWT; 14-day refresh token, rotated on use, reuse of a revoked token revokes the chain; only SHA-256 hashes stored. Mobile stores tokens in Keychain/Keystore (`react-native-keychain`).
- **Money:** `decimal` end-to-end, `MidpointRounding.AwayFromZero`, calculation done only on the server; client displays.
- **Concurrency:** `rowversion` on mutable aggregates; payment and invoice-number allocation under a transaction with `UPDLOCK`.
- **Errors:** RFC 7807 ProblemDetails. **Logging:** Serilog structured JSON, no PII/secrets.
- **Pagination:** `page`, `pageSize` (max 100), `sort`, filters; response envelope `{items,total,page,pageSize}`.
- **Versioning:** `/api/v1`.

## Mobile architecture
`screens` (UI only) → `store` (RTK slices + thunks) → `services/api` (Axios) . Navigation guards read permissions from the `auth` slice (UX only; server is authoritative). Mandatory folder structure from the master prompt is preserved; a `services/` and `types/` folder will be added under `src/`.

## Tooling assumptions
- React Native CLI (not Expo) for native splash/icon control.
- CI: GitHub Actions (backend build/test, mobile lint/test, Android build). iOS archive needs macOS runner/signing secrets supplied by you.
- Docker Compose for local SQL Server.
