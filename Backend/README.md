# Backend

ASP.NET Core (.NET 10) Web API, Clean Architecture: `Domain` -> `Application` -> `Infrastructure` -> `API`; tests in `BusinessManagement.Tests`.

## Configuration (environment variables - never commit values)
| Variable | Purpose |
|---|---|
| `ConnectionStrings__Default` | SQL Server connection string |
| `Jwt__SigningKey` | >= 32 byte random secret |
| `Database__MigrateOnStartup` | `true` to migrate + seed Admin at startup |
| `Email__Host`, `Email__Port` (587), `Email__Security` (`StartTls`/`SslOnConnect`/`None`), `Email__User`, `Email__Password`, `Email__FromAddress`, `Email__FromName` | SMTP for the Admin temporary-password email |
| `RateLimiting__AuthPermitPerMinute` | login/refresh/reset attempts per IP per minute (default 10) |
| `Seed__AdminUsername`, `Seed__AdminPassword` (>= 12 chars), `Seed__AdminEmail` | first-run Admin (only used if no Admin exists) |

## Migrations
Create via the "Generate EF migration" workflow: commit `Backend/migration-request.txt` containing the migration name, or run locally:
`dotnet ef migrations add <Name> -p BusinessManagement.Infrastructure -s BusinessManagement.API -o Persistence/Migrations`

## Free SMTP options
- **Gmail:** host `smtp.gmail.com`, port 587, `StartTls`, user = your Gmail address, password = a Google *App Password* (needs 2-Step Verification). Roughly 500 emails/day, plenty for Admin resets.
- **Brevo (free tier)** or any SMTP server you already have also work. Without SMTP configured the Admin reset email cannot be sent (the API still answers 202 and logs an error).
