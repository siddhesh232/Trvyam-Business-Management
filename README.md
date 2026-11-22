# Trvyam Business Management

Multi-business management app: React Native (Android/iOS) + ASP.NET Core Web API (.NET 10) + Microsoft SQL Server.

**Status:** Phases 1-5 done (approval package, backend setup, database layer; CI green). Next: Phase 6 authentication & authorization. App name: Trvyam Management.

## Review these first
1. [Requirements breakdown](Documentation/Architecture/01-requirements-breakdown.md)
2. [Solution architecture](Documentation/Architecture/02-solution-architecture.md)
3. [Database schema](Database/Schema/001_schema.sql) and [ER diagram](Documentation/Database/ERD.md)
4. [API endpoints](Documentation/API/api-endpoints.md)
5. [Screens & navigation](Documentation/Architecture/03-screens-and-navigation.md)
6. [Permission matrix](Documentation/Architecture/04-permission-matrix.md)
7. [Open decisions](Documentation/Setup/open-decisions.md)

See [PROGRESS.md](PROGRESS.md).

## Security note
This repository is public. Never commit secrets: use `.env` (git-ignored) and the placeholders in `.env.example`. Business source documents (FRD, prompts) are intentionally not included.

## Layout
`Backend/` (Clean Architecture solution) · `Frontend/` (React Native) · `Database/` · `Documentation/`
