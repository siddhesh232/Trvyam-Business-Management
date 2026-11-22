using BusinessManagement.Application.Abstractions;
using BusinessManagement.Domain.Entities;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BusinessManagement.Tests.Database;

/// <summary>Database tests need a real SQL Server (CI provides one via TEST_SQL_CONNECTION; locally they are skipped).</summary>
public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION")))
            Skip = "TEST_SQL_CONNECTION is not set (SQL Server required).";
    }
}

public sealed class TestUser : ICurrentUser
{
    public long? UserId { get; set; }
    public UserRole? Role { get; set; }
    public bool IsAuthenticated => UserId is not null;
    public bool IsAdmin => Role == UserRole.Admin;
    public string? IpAddress { get; set; } = "127.0.0.1";
}

public sealed class DbFixture : IAsyncLifetime
{
    private readonly string _master = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION") ?? "";
    private readonly string _dbName = "tbm_" + Guid.NewGuid().ToString("N")[..12];
    private string _connectionString = "";
    public string ConnectionString => _connectionString;

    public long AdminId { get; private set; }
    public long MemberId { get; private set; }
    public long BusinessId { get; private set; }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_master)) return;
        _connectionString = new SqlConnectionStringBuilder(_master) { InitialCatalog = _dbName }.ConnectionString;

        await using var db = Create();
        await db.Database.MigrateAsync();

        var admin = new User { Role = UserRole.Admin, Name = "Admin", Username = "admin", PasswordHash = "x", Mobile = "9000000000", Email = "admin@example.test" };
        var member = new User { Role = UserRole.Member, Name = "Member One", Username = "member1", PasswordHash = "x", Mobile = "9000000001" };
        db.Users.AddRange(admin, member);
        await db.SaveChangesAsync();
        var biz = new Business { Name = "Test Biz", CreatedBy = admin.Id };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync();
        db.BusinessMembers.Add(new BusinessMember { BusinessId = biz.Id, UserId = member.Id, AssignedAt = DateTime.UtcNow, AssignedBy = admin.Id });
        await db.SaveChangesAsync();
        (AdminId, MemberId, BusinessId) = (admin.Id, member.Id, biz.Id);
    }

    public AppDbContext Create(ICurrentUser? user = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_connectionString).Options;
        return new AppDbContext(options, user ?? new SystemCurrentUser());
    }

    public AppDbContext CreateAsAdmin() => Create(new TestUser { UserId = AdminId, Role = UserRole.Admin });
    public AppDbContext CreateAsMember() => Create(new TestUser { UserId = MemberId, Role = UserRole.Member });

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(_master)) return;
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(_master);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"IF DB_ID('{_dbName}') IS NOT NULL BEGIN ALTER DATABASE [{_dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_dbName}]; END";
        await cmd.ExecuteNonQueryAsync();
    }

    public static string NewMobile() => "9" + Random.Shared.NextInt64(100_000_000, 999_999_999);
}
