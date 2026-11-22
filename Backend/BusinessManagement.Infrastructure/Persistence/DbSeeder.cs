using BusinessManagement.Application.Abstractions;
using BusinessManagement.Domain.Entities;
using BusinessManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BusinessManagement.Infrastructure.Persistence;

/// <summary>
/// Creates the single Admin account on first run from configuration (Seed:Admin*). No default or hard-coded
/// credentials exist: if the settings are absent nothing is created. Idempotent.
/// </summary>
public static class DbSeeder
{
    public const int MinPasswordLength = 12;

    /// <returns>True if an admin was created.</returns>
    public static async Task<bool> SeedAdminAsync(AppDbContext db, IPasswordHasher hasher, IConfiguration cfg, CancellationToken ct = default)
    {
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Role == UserRole.Admin, ct)) return false;

        string? username = cfg["Seed:AdminUsername"], password = cfg["Seed:AdminPassword"], email = cfg["Seed:AdminEmail"];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(email))
            return false;
        if (password.Length < MinPasswordLength)
            throw new InvalidOperationException($"Seed:AdminPassword must be at least {MinPasswordLength} characters.");

        db.Users.Add(new User
        {
            Role = UserRole.Admin,
            Name = cfg["Seed:AdminName"] ?? "Administrator",
            Username = username.Trim(),
            PasswordHash = hasher.Hash(password),
            Mobile = cfg["Seed:AdminMobile"] ?? "0000000000",
            Email = email.Trim()
        });
        await db.SaveChangesAsync(ct);
        return true;
    }
}
