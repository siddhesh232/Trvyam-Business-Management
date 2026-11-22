using BusinessManagement.Application.Abstractions;
using BusinessManagement.Domain.Entities;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Infrastructure.Security;
using BusinessManagement.Tests.Database;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BusinessManagement.Tests.Api;

public sealed class FakeEmailSender : IEmailSender
{
    public sealed record Sent(string To, string Subject, string Body);
    private readonly List<Sent> _sent = [];
    public IReadOnlyList<Sent> Messages { get { lock (_sent) return _sent.ToList(); } }
    public void Clear() { lock (_sent) _sent.Clear(); }

    public Task SendAsync(string toAddress, string subject, string textBody, CancellationToken ct = default)
    {
        lock (_sent) _sent.Add(new Sent(toAddress, subject, textBody));
        return Task.CompletedTask;
    }
}

/// <summary>
/// One API host + one SQL Server database shared by all API tests (xUnit collection "Api", so they run serially).
/// Program.cs reads settings while the host is being built, so settings are supplied as environment variables.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string AdminPassword = "Admin-Pass-12345";
    public const string MemberPassword = "Member-Pass-12345";

    public DbFixture Db { get; } = new();
    public FakeEmailSender Email { get; } = new();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public bool HasSql => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION"));

    public async Task InitializeAsync()
    {
        await Db.InitializeAsync();
        var hasher = new Pbkdf2PasswordHasher();
        if (HasSql)
        {
            await using var db = Db.Create();
            (await db.Users.SingleAsync(u => u.Id == Db.AdminId)).PasswordHash = hasher.Hash(AdminPassword);
            (await db.Users.SingleAsync(u => u.Id == Db.MemberId)).PasswordHash = hasher.Hash(MemberPassword);
            await db.SaveChangesAsync();
        }

        Environment.SetEnvironmentVariable("ConnectionStrings__Default",
            HasSql ? Db.ConnectionString : "Server=localhost;Database=unused;TrustServerCertificate=True");
        Environment.SetEnvironmentVariable("Jwt__SigningKey", "test-only-signing-key-0123456789-abcdef-0123456789");
        Environment.SetEnvironmentVariable("RateLimiting__AuthPermitPerMinute", "100000");

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IEmailSender>();
                s.AddSingleton<IEmailSender>(Email);
            });
        });
    }

    public HttpClient CreateClient() => Factory.CreateClient();

    /// <summary>Directly sets a user's password (restores state after tests that change it).</summary>
    public async Task SetPasswordAsync(long userId, string password)
    {
        await using var db = Db.Create();
        (await db.Users.SingleAsync(u => u.Id == userId)).PasswordHash = new Pbkdf2PasswordHasher().Hash(password);
        await db.SaveChangesAsync();
    }

    public async Task<User> CreateUserAsync(UserRole role, string password = MemberPassword)
    {
        await using var db = Db.Create();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var user = new User
        {
            Role = role, Name = "U " + tag, Username = "u" + tag, Mobile = DbFixture.NewMobile(),
            PasswordHash = new Pbkdf2PasswordHasher().Hash(password)
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        await Db.DisposeAsync();
    }
}

[CollectionDefinition("Api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
