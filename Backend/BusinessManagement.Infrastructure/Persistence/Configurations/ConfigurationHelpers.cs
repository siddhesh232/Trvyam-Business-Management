using BusinessManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessManagement.Infrastructure.Persistence.Configurations;

internal static class ConfigurationHelpers
{
    internal const string NotDeleted = "[IsDeleted] = 0";

    /// <summary>Adds restricted (no cascade) FKs to Users for audit-style columns such as CreatedBy.</summary>
    internal static void UserFk<T>(this EntityTypeBuilder<T> b, params string[] properties) where T : class
    {
        foreach (var p in properties)
            b.HasOne<User>().WithMany().HasForeignKey(p).OnDelete(DeleteBehavior.Restrict);
    }
}
