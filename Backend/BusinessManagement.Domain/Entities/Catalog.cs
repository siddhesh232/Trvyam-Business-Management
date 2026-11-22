using BusinessManagement.Domain.Common;

namespace BusinessManagement.Domain.Entities;

/// <summary>Global customer record, unique by normalised mobile number (FRD s.6).</summary>
public class Customer : ISoftDeletable
{
    public long Id { get; set; }
    public string Mobile { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? StateCode { get; set; }
    public string? Gstin { get; set; }
    public DateTime CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<CustomerBusiness> BusinessTags { get; set; } = [];
}

public class CustomerBusiness
{
    public long CustomerId { get; set; }
    public long BusinessId { get; set; }
    public DateTime LinkedAt { get; set; }
    public long LinkedBy { get; set; }
    public Customer? Customer { get; set; }
    public Business? Business { get; set; }
}

public class Product : ISoftDeletable
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string? Category { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal GstRate { get; set; }
    public string? HsnCode { get; set; }
    public string Unit { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<BusinessProduct> BusinessMappings { get; set; } = [];
}

public class BusinessProduct
{
    public long BusinessId { get; set; }
    public long ProductId { get; set; }
    public Business? Business { get; set; }
    public Product? Product { get; set; }
}
