using BusinessManagement.Application.Abstractions;
using BusinessManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BusinessManagement.Infrastructure.Persistence;

/// <summary>Used by `dotnet ef` so migrations can be created without booting the API (no secrets required).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                 ?? "Server=localhost,1433;Database=TrvyamBM;Integrated Security=false;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs).Options;
        return new AppDbContext(options, new SystemCurrentUser());
    }
}

/// <summary>Caller for non-request contexts (design time, seeding, background jobs): no user, no audit identity.</summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public long? UserId => null;
    public UserRole? Role => null;
    public bool IsAuthenticated => false;
    public bool IsAdmin => false;
    public string? IpAddress => null;
}
