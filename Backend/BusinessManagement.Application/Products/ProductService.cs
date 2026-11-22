using BusinessManagement.Application.Abstractions;
using BusinessManagement.Application.Common;
using BusinessManagement.Domain.Entities;
using BusinessManagement.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace BusinessManagement.Application.Products;

public sealed class ProductService(IAppDbContext db, ICurrentUser current) : IProductService
{
    private void RequireAdmin() { if (!current.IsAdmin) throw new ForbiddenException(); }

    public async Task<PagedResult<ProductDto>> ListAsync(ProductListQuery q, CancellationToken ct)
    {
        RequireAdmin();
        var (page, size) = Paging.Normalize(q.Page, q.PageSize);
        var query = db.Products.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q.Category)) query = query.Where(p => p.Category == q.Category);
        if (q.IsActive is bool a) query = query.Where(p => p.IsActive == a);
        query = Search(query, q.Q);
        query = Sort(query, q.Sort);

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * size).Take(size).Select(p => new ProductDto(
            p.Id, p.Name, p.Description, p.Sku, p.Category, p.SellingPrice, p.PurchasePrice, p.GstRate, p.HsnCode, p.Unit, p.IsActive,
            p.BusinessMappings.Select(m => m.BusinessId).ToList())).ToListAsync(ct);
        return new PagedResult<ProductDto>(items, total, page, size);
    }

    public async Task<ProductDto> GetAsync(long id, CancellationToken ct)
    {
        RequireAdmin();
        return await db.Products.AsNoTracking().Where(p => p.Id == id).Select(p => new ProductDto(
            p.Id, p.Name, p.Description, p.Sku, p.Category, p.SellingPrice, p.PurchasePrice, p.GstRate, p.HsnCode, p.Unit, p.IsActive,
            p.BusinessMappings.Select(m => m.BusinessId).ToList())).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Product not found.");
    }

    public async Task<ProductDto> CreateAsync(SaveProductRequest r, CancellationToken ct)
    {
        RequireAdmin();
        await EnsureSkuFreeAsync(r.Sku, null, ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        var p = new Product { CreatedBy = current.UserId!.Value };
        Apply(p, r);
        db.Products.Add(p);
        await db.SaveChangesAsync(ct);
        if (r.BusinessIds is { Count: > 0 }) await MapAsync(p.Id, r.BusinessIds, ct);
        await tx.CommitAsync(ct);
        return await GetAsync(p.Id, ct);
    }

    public async Task<ProductDto> UpdateAsync(long id, SaveProductRequest r, CancellationToken ct)
    {
        RequireAdmin();
        var p = await db.Products.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Product not found.");
        await EnsureSkuFreeAsync(r.Sku, id, ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        Apply(p, r);   // past sales keep their own name/price/GST snapshot, so editing the catalogue is safe
        await db.SaveChangesAsync(ct);
        if (r.BusinessIds is not null) await MapAsync(id, r.BusinessIds, ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        RequireAdmin();
        var p = await db.Products.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Product not found.");
        p.IsDeleted = true;
        p.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    public async Task<ProductDto> SetBusinessesAsync(long id, SetProductBusinessesRequest r, CancellationToken ct)
    {
        RequireAdmin();
        if (!await db.Products.AnyAsync(x => x.Id == id, ct)) throw new NotFoundException("Product not found.");
        await using var tx = await db.BeginTransactionAsync(ct);
        await MapAsync(id, r.BusinessIds, ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<PagedResult<CatalogProductDto>> ListForBusinessAsync(long businessId, ProductListQuery q, CancellationToken ct)
    {
        var (page, size) = Paging.Normalize(q.Page, q.PageSize);
        var query = db.Products.AsNoTracking().Where(p => p.IsActive && p.BusinessMappings.Any(m => m.BusinessId == businessId));
        if (!string.IsNullOrWhiteSpace(q.Category)) query = query.Where(p => p.Category == q.Category);
        query = Sort(Search(query, q.Q), q.Sort);

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * size).Take(size).Select(p => new CatalogProductDto(
            p.Id, p.Name, p.Description, p.Sku, p.Category, p.SellingPrice, p.GstRate, p.HsnCode, p.Unit)).ToListAsync(ct);
        return new PagedResult<CatalogProductDto>(items, total, page, size);
    }

    // ------------------------------------------------------------------ helpers
    private async Task MapAsync(long productId, IReadOnlyList<long> businessIds, CancellationToken ct)
    {
        var wanted = businessIds.Distinct().ToList();
        var valid = await db.Businesses.Where(b => wanted.Contains(b.Id)).Select(b => b.Id).ToListAsync(ct);
        if (valid.Count != wanted.Count) throw Fail.Validation("businessIds", "One or more selected businesses do not exist.");

        var existing = await db.BusinessProducts.Where(m => m.ProductId == productId).ToListAsync(ct);
        foreach (var bid in wanted.Where(w => existing.All(e => e.BusinessId != w)))
            db.BusinessProducts.Add(new BusinessProduct { BusinessId = bid, ProductId = productId });
        db.BusinessProducts.RemoveRange(existing.Where(e => !wanted.Contains(e.BusinessId)));
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureSkuFreeAsync(string sku, long? exceptId, CancellationToken ct)
    {
        var s = sku.Trim();
        if (await db.Products.AnyAsync(p => p.Sku == s && p.Id != exceptId, ct))
            throw new ConflictException("A product with this SKU already exists.");
    }

    private static void Apply(Product p, SaveProductRequest r)
    {
        p.Name = r.Name.Trim(); p.Description = r.Description?.Trim(); p.Sku = r.Sku.Trim(); p.Category = r.Category?.Trim();
        p.SellingPrice = r.SellingPrice; p.PurchasePrice = r.PurchasePrice; p.GstRate = r.GstRate;
        p.HsnCode = string.IsNullOrWhiteSpace(r.HsnCode) ? null : r.HsnCode.Trim(); p.Unit = r.Unit.Trim(); p.IsActive = r.IsActive;
    }

    private static IQueryable<Product> Search(IQueryable<Product> q, string? term)
    {
        if (string.IsNullOrWhiteSpace(term)) return q;
        var t = term.Trim();
        return q.Where(p => p.Name.Contains(t) || p.Sku.Contains(t) || (p.Category != null && p.Category.Contains(t)));
    }

    private static IQueryable<Product> Sort(IQueryable<Product> q, string? sort) => sort?.ToLowerInvariant() switch
    {
        "-name" => q.OrderByDescending(p => p.Name),
        "sku" => q.OrderBy(p => p.Sku),
        "price" => q.OrderBy(p => p.SellingPrice).ThenBy(p => p.Id),
        "-price" => q.OrderByDescending(p => p.SellingPrice).ThenBy(p => p.Id),
        _ => q.OrderBy(p => p.Name).ThenBy(p => p.Id)
    };
}
