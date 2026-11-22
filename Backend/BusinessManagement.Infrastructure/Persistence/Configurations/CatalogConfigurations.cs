using BusinessManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessManagement.Infrastructure.Persistence.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("Customers", t => t.HasCheckConstraint("CK_Cust_Mobile",
            "[Mobile] NOT LIKE '%[^0-9]%' AND LEN([Mobile]) BETWEEN 10 AND 15"));
        b.Property(x => x.Mobile).HasMaxLength(15).IsUnicode(false).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Address).HasMaxLength(400);
        b.Property(x => x.StateCode).HasColumnType("char(2)");
        b.Property(x => x.Gstin).HasColumnType("char(15)");
        b.Property(x => x.RowVersion).IsRowVersion();
        b.UserFk(nameof(Customer.CreatedBy), nameof(Customer.UpdatedBy));
        // One global record per mobile number: no duplicates across businesses (FRD s.6).
        b.HasIndex(x => x.Mobile).IsUnique().HasFilter(ConfigurationHelpers.NotDeleted).HasDatabaseName("UX_Cust_Mobile");
        b.HasIndex(x => x.Name).HasDatabaseName("IX_Cust_Name");
    }
}

public sealed class CustomerBusinessConfiguration : IEntityTypeConfiguration<CustomerBusiness>
{
    public void Configure(EntityTypeBuilder<CustomerBusiness> b)
    {
        b.ToTable("CustomerBusinesses");
        b.HasKey(x => new { x.CustomerId, x.BusinessId });
        b.HasOne(x => x.Customer).WithMany(x => x.BusinessTags).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Business).WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        b.UserFk(nameof(CustomerBusiness.LinkedBy));
        b.HasIndex(x => x.BusinessId).HasDatabaseName("IX_CB_Business");
    }
}

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("Products", t =>
        {
            t.HasCheckConstraint("CK_Prod_Prices", "[SellingPrice] >= 0 AND [PurchasePrice] >= 0");
            t.HasCheckConstraint("CK_Prod_Gst", "[GstRate] BETWEEN 0 AND 100");
        });
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.Sku).HasMaxLength(50).IsRequired();
        b.Property(x => x.Category).HasMaxLength(80);
        b.Property(x => x.GstRate).HasPrecision(5, 2);
        b.Property(x => x.HsnCode).HasMaxLength(8).IsUnicode(false);
        b.Property(x => x.Unit).HasMaxLength(20).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.UserFk(nameof(Product.CreatedBy), nameof(Product.UpdatedBy));
        b.HasIndex(x => x.Sku).IsUnique().HasFilter(ConfigurationHelpers.NotDeleted).HasDatabaseName("UX_Prod_Sku");
    }
}

public sealed class BusinessProductConfiguration : IEntityTypeConfiguration<BusinessProduct>
{
    public void Configure(EntityTypeBuilder<BusinessProduct> b)
    {
        b.ToTable("BusinessProducts");
        b.HasKey(x => new { x.BusinessId, x.ProductId });
        b.HasOne(x => x.Business).WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Product).WithMany(x => x.BusinessMappings).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.ProductId).HasDatabaseName("IX_BP_Product");
    }
}
