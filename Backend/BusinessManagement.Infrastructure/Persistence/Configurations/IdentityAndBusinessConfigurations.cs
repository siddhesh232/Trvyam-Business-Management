using BusinessManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessManagement.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users", t =>
        {
            t.HasCheckConstraint("CK_Users_Role", "[Role] IN (1,2,3)");
            t.HasCheckConstraint("CK_Users_AdminEmail", "[Role] <> 1 OR [Email] IS NOT NULL");
        });
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Username).HasMaxLength(60).IsRequired();
        b.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
        b.Property(x => x.Mobile).HasMaxLength(15).IsUnicode(false).IsRequired();
        b.Property(x => x.Email).HasMaxLength(254);
        b.Property(x => x.AvatarUrl).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.Username).IsUnique().HasFilter(ConfigurationHelpers.NotDeleted).HasDatabaseName("UX_Users_Username");
        // Exactly one Admin account (FRD s.13).
        b.HasIndex(x => x.Role).IsUnique().HasFilter("[Role] = 1").HasDatabaseName("UX_Users_SingleAdmin");
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("RefreshTokens");
        b.Property(x => x.TokenHash).HasColumnType("char(64)").IsRequired();
        b.Property(x => x.DeviceInfo).HasMaxLength(200);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<RefreshToken>().WithMany().HasForeignKey(x => x.ReplacedByTokenId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("UX_RefreshTokens_Hash");
        b.HasIndex(x => x.UserId).IncludeProperties(x => new { x.ExpiresAt, x.RevokedAt }).HasDatabaseName("IX_RefreshTokens_User");
    }
}

public sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> b)
    {
        b.ToTable("PasswordResetTokens", t => t.HasCheckConstraint("CK_PRT_Expiry", "[ExpiresAt] > [CreatedAt]"));
        b.Property(x => x.TokenHash).HasMaxLength(256).IsRequired();
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.UserId, x.ExpiresAt }).HasDatabaseName("IX_PRT_User");
    }
}

public sealed class BusinessConfiguration : IEntityTypeConfiguration<Business>
{
    public void Configure(EntityTypeBuilder<Business> b)
    {
        b.ToTable("Businesses", t => t.HasCheckConstraint("CK_Biz_Gstin",
            "[GstNumber] IS NULL OR [GstNumber] LIKE '[0-9][0-9][A-Z][A-Z][A-Z][A-Z][A-Z][0-9][0-9][0-9][0-9][A-Z][0-9A-Z]Z[0-9A-Z]'"));
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.GstNumber).HasColumnType("char(15)");
        b.Property(x => x.Category).HasMaxLength(80);
        b.Property(x => x.LogoUrl).HasMaxLength(500);
        b.Property(x => x.AddressLine).HasMaxLength(400);
        b.Property(x => x.StateCode).HasColumnType("char(2)");
        b.Property(x => x.RowVersion).IsRowVersion();
        b.UserFk(nameof(Business.CreatedBy), nameof(Business.UpdatedBy));
        b.HasIndex(x => x.Name).IsUnique().HasFilter(ConfigurationHelpers.NotDeleted).HasDatabaseName("UX_Biz_Name");
        b.HasIndex(x => x.GstNumber).IsUnique().HasFilter("[GstNumber] IS NOT NULL AND [IsDeleted] = 0").HasDatabaseName("UX_Biz_Gstin");
    }
}

public sealed class BusinessMemberConfiguration : IEntityTypeConfiguration<BusinessMember>
{
    public void Configure(EntityTypeBuilder<BusinessMember> b)
    {
        b.ToTable("BusinessMembers");
        b.HasKey(x => new { x.BusinessId, x.UserId });
        b.HasOne(x => x.Business).WithMany(x => x.Members).HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.User).WithMany(x => x.BusinessMemberships).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.UserFk(nameof(BusinessMember.AssignedBy));
        b.HasIndex(x => x.UserId).HasFilter("[IsActive] = 1").HasDatabaseName("IX_BM_User");
    }
}

public sealed class BusinessPermissionConfiguration : IEntityTypeConfiguration<BusinessPermission>
{
    public void Configure(EntityTypeBuilder<BusinessPermission> b)
    {
        b.ToTable("BusinessPermissions");
        b.HasKey(x => new { x.BusinessId, x.UserId });
        b.HasOne(x => x.Member).WithOne(x => x.Permissions)
            .HasForeignKey<BusinessPermission>(x => new { x.BusinessId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
        b.UserFk(nameof(BusinessPermission.UpdatedBy));
    }
}
