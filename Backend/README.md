# Backend

ASP.NET Core (.NET 10) Web API, Clean Architecture: `Domain` -> `Application` -> `Infrastructure` -> `API`; tests in `BusinessManagement.Tests`.

## Configuration (environment variables - never commit values)
| Variable | Purpose |
|---|---|
| `ConnectionStrings__Default` | SQL Server connection string |
| `Jwt__SigningKey` | >= 32 byte random secret |
| `Database__MigrateOnStartup` | `true` to migrate + seed Admin at startup |
| `Seed__AdminUsername`, `Seed__AdminPassword` (>= 12 chars), `Seed__AdminEmail` | first-run Admin (only used if no Admin exists) |

## Migrations
Create via the "Generate EF migration" workflow: commit `Backend/migration-request.txt` containing the migration name, or run locally:
`dotnet ef migrations add <Name> -p BusinessManagement.Infrastructure -s BusinessManagement.API -o Persistence/Migrations`
