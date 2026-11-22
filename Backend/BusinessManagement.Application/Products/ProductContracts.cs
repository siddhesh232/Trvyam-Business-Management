using BusinessManagement.Application.Common;

namespace BusinessManagement.Application.Products;

public sealed record ProductListQuery(string? Q = null, string? Category = null, bool? IsActive = null, int Page = 1, int PageSize = 20, string? Sort = null);

/// <summary>Full record, Admin only (includes purchase/cost price and business mapping).</summary>
public sealed record ProductDto(long Id, string Name, string? Description, string Sku, string? Category, decimal SellingPrice,
    decimal PurchasePrice, decimal GstRate, string? HsnCode, string Unit, bool IsActive, IReadOnlyList<long> BusinessIds);

/// <summary>What a member sees when picking products for a sale: no cost price, no mapping.</summary>
public sealed record CatalogProductDto(long Id, string Name, string? Description, string Sku, string? Category,
    decimal SellingPrice, decimal GstRate, string? HsnCode, string Unit);

public sealed record SaveProductRequest(string Name, string? Description, string Sku, string? Category, decimal SellingPrice,
    decimal PurchasePrice, decimal GstRate, string? HsnCode, string Unit, bool IsActive, IReadOnlyList<long>? BusinessIds);

public sealed record SetProductBusinessesRequest(IReadOnlyList<long> BusinessIds);

public interface IProductService
{
    Task<PagedResult<ProductDto>> ListAsync(ProductListQuery query, CancellationToken ct);
    Task<ProductDto> GetAsync(long id, CancellationToken ct);
    Task<ProductDto> CreateAsync(SaveProductRequest request, CancellationToken ct);
    Task<ProductDto> UpdateAsync(long id, SaveProductRequest request, CancellationToken ct);
    Task DeleteAsync(long id, CancellationToken ct);
    Task<ProductDto> SetBusinessesAsync(long id, SetProductBusinessesRequest request, CancellationToken ct);
    /// <summary>Active products mapped to the business. Caller must hold ViewProducts (checked by the controller filter).</summary>
    Task<PagedResult<CatalogProductDto>> ListForBusinessAsync(long businessId, ProductListQuery query, CancellationToken ct);
}
