using BusinessManagement.API.Filters;
using BusinessManagement.Application.Common;
using BusinessManagement.Application.Customers;
using BusinessManagement.Application.Products;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BusinessManagement.API.Controllers;

/// <summary>Admin-managed product catalogue.</summary>
[ApiController, Authorize(Roles = "Admin")]
[Route("api/v1/products")]
public sealed class ProductsController(IProductService products) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ProductDto>> List([FromQuery] ProductListQuery query, CancellationToken ct) => products.ListAsync(query, ct);

    [HttpGet("{id:long}")]
    public Task<ProductDto> Get(long id, CancellationToken ct) => products.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<ProductDto>> Create(SaveProductRequest request, CancellationToken ct)
    {
        var created = await products.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:long}")]
    public Task<ProductDto> Update(long id, SaveProductRequest request, CancellationToken ct) => products.UpdateAsync(id, request, ct);

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await products.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Replaces the set of businesses in which this product is available.</summary>
    [HttpPut("{id:long}/businesses")]
    public Task<ProductDto> SetBusinesses(long id, SetProductBusinessesRequest request, CancellationToken ct) => products.SetBusinessesAsync(id, request, ct);
}

[ApiController, Authorize]
[Route("api/v1/businesses/{businessId:long}")]
public sealed class BusinessCatalogController(IProductService products, ICustomerService customers) : ControllerBase
{
    /// <summary>Active products available to this business (no cost price).</summary>
    [HttpGet("products"), RequireBusinessPermission(Permission.ViewProducts)]
    public Task<PagedResult<CatalogProductDto>> Products(long businessId, [FromQuery] ProductListQuery query, CancellationToken ct) =>
        products.ListForBusinessAsync(businessId, query, ct);

    [HttpGet("customers"), RequireBusinessPermission(Permission.ViewCustomers)]
    public Task<PagedResult<CustomerDto>> Customers(long businessId, [FromQuery] CustomerListQuery query, CancellationToken ct) =>
        customers.ListForBusinessAsync(businessId, query, ct);

    /// <summary>Read-only lookup by mobile number in the global customer database (404 if none).</summary>
    [HttpGet("customers/lookup")]
    public async Task<ActionResult<CustomerDto>> Lookup(long businessId, [FromQuery] string mobile, CancellationToken ct)
    {
        if (!MobileNumber.IsValid(mobile)) throw Fail.Validation("mobile", "Enter a valid mobile number (10-15 digits).");
        var c = await customers.LookupAsync(businessId, mobile, ct);
        return c is null ? NotFound() : c;
    }

    /// <summary>Finds the customer by mobile or creates them, and tags this business. Never duplicates a customer.</summary>
    [HttpPost("customers/resolve"), RequireBusinessPermission(Permission.AddCustomers)]
    public Task<ResolveCustomerResponse> Resolve(long businessId, ResolveCustomerRequest request, CancellationToken ct) =>
        customers.ResolveAsync(businessId, request, ct);

    [HttpPut("customers/{customerId:long}"), RequireBusinessPermission(Permission.EditCustomers)]
    public Task<CustomerDto> UpdateCustomer(long businessId, long customerId, UpdateCustomerRequest request, CancellationToken ct) =>
        customers.UpdateInBusinessAsync(businessId, customerId, request, ct);
}

/// <summary>Admin view of the global customer database.</summary>
[ApiController, Authorize(Roles = "Admin")]
[Route("api/v1/customers")]
public sealed class CustomersController(ICustomerService customers) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<CustomerDto>> List([FromQuery] CustomerListQuery query, CancellationToken ct) => customers.ListAllAsync(query, ct);

    [HttpGet("{id:long}")]
    public Task<CustomerDto> Get(long id, CancellationToken ct) => customers.GetAsync(id, ct);

    [HttpPut("{id:long}")]
    public Task<CustomerDto> Update(long id, UpdateCustomerRequest request, CancellationToken ct) => customers.UpdateAsync(id, request, ct);
}
