using BusinessManagement.Application.Abstractions;
using BusinessManagement.Application.Authorization;
using BusinessManagement.Application.Common;
using BusinessManagement.Domain.Entities;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace BusinessManagement.Application.Customers;

public sealed class CustomerService(IAppDbContext db, ICurrentUser current, IBusinessAccessService access, TimeProvider clock) : ICustomerService
{
    // ------------------------------------------------------------------ business-scoped (members + admin)
    public async Task<CustomerDto?> LookupAsync(long businessId, string mobile, CancellationToken ct)
    {
        var a = await access.RequireAsync(businessId, null, ct);
        if (!a.Can(Permission.ViewCustomers) && !a.Can(Permission.AddCustomers)) throw new ForbiddenException();

        var normalized = MobileNumber.Normalize(mobile);
        var c = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Mobile == normalized, ct);
        return c is null ? null : await ToDtoAsync(c, ct);
    }

    public async Task<ResolveCustomerResponse> ResolveAsync(long businessId, ResolveCustomerRequest r, CancellationToken ct)
    {
        var a = await access.RequireAsync(businessId, Permission.AddCustomers, ct);
        var mobile = MobileNumber.Normalize(r.Mobile);

        var customer = await db.Customers.FirstOrDefaultAsync(x => x.Mobile == mobile, ct);
        var created = false;
        if (customer is null)
        {
            if (string.IsNullOrWhiteSpace(r.Name)) throw Fail.Validation("name", "Customer name is required for a new customer.");
            customer = new Customer { Mobile = mobile, CreatedBy = a.UserId };
            ApplyDetails(customer, r.Name, r.Address, r.StateCode, r.Gstin);
            db.Customers.Add(customer);
            try
            {
                await db.SaveChangesAsync(ct);
                created = true;
            }
            catch (DbUpdateException)
            {
                // Another request created the same mobile at the same moment: use that record (no duplicates).
                db.Customers.Entry(customer).State = EntityState.Detached;
                customer = await db.Customers.FirstOrDefaultAsync(x => x.Mobile == mobile, ct)
                           ?? throw new ConflictException("Could not save the customer. Please retry.");
            }
        }

        var linked = await db.CustomerBusinesses.AnyAsync(x => x.CustomerId == customer.Id && x.BusinessId == businessId, ct);
        if (!linked)
        {
            db.CustomerBusinesses.Add(new CustomerBusiness { CustomerId = customer.Id, BusinessId = businessId, LinkedAt = clock.GetUtcNow().UtcDateTime, LinkedBy = a.UserId });
            await db.SaveChangesAsync(ct);
        }
        return new ResolveCustomerResponse(await ToDtoAsync(customer, ct), created, !linked);
    }

    public async Task<PagedResult<CustomerDto>> ListForBusinessAsync(long businessId, CustomerListQuery q, CancellationToken ct)
    {
        await access.RequireAsync(businessId, Permission.ViewCustomers, ct);
        var query = db.Customers.AsNoTracking().Where(c => c.BusinessTags.Any(t => t.BusinessId == businessId));
        return await PageAsync(query, q, ct);
    }

    public async Task<CustomerDto> UpdateInBusinessAsync(long businessId, long customerId, UpdateCustomerRequest r, CancellationToken ct)
    {
        await access.RequireAsync(businessId, Permission.EditCustomers, ct);
        var c = await db.Customers.FirstOrDefaultAsync(x => x.Id == customerId && x.BusinessTags.Any(t => t.BusinessId == businessId), ct)
                ?? throw new NotFoundException("Customer not found in this business.");
        ApplyDetails(c, r.Name, r.Address, r.StateCode, r.Gstin);
        await db.SaveChangesAsync(ct);   // shared record: the change is audited with the acting member
        return await ToDtoAsync(c, ct);
    }

    // ------------------------------------------------------------------ Admin: global database
    public async Task<PagedResult<CustomerDto>> ListAllAsync(CustomerListQuery q, CancellationToken ct)
    {
        RequireAdmin();
        return await PageAsync(db.Customers.AsNoTracking(), q, ct);
    }

    public async Task<CustomerDto> GetAsync(long customerId, CancellationToken ct)
    {
        RequireAdmin();
        var c = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == customerId, ct) ?? throw new NotFoundException("Customer not found.");
        return await ToDtoAsync(c, ct);
    }

    public async Task<CustomerDto> UpdateAsync(long customerId, UpdateCustomerRequest r, CancellationToken ct)
    {
        RequireAdmin();
        var c = await db.Customers.FirstOrDefaultAsync(x => x.Id == customerId, ct) ?? throw new NotFoundException("Customer not found.");
        ApplyDetails(c, r.Name, r.Address, r.StateCode, r.Gstin);
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(c, ct);
    }

    // ------------------------------------------------------------------ helpers
    private void RequireAdmin() { if (!current.IsAdmin) throw new ForbiddenException(); }

    private static void ApplyDetails(Customer c, string name, string? address, string? stateCode, string? gstin)
    {
        c.Name = name.Trim();
        c.Address = address?.Trim();
        c.Gstin = string.IsNullOrWhiteSpace(gstin) ? null : Gstin.Normalize(gstin);
        c.StateCode = c.Gstin is not null ? Gstin.StateCode(c.Gstin) : (string.IsNullOrWhiteSpace(stateCode) ? null : stateCode.Trim());
    }

    private async Task<PagedResult<CustomerDto>> PageAsync(IQueryable<Customer> query, CustomerListQuery q, CancellationToken ct)
    {
        var (page, size) = Paging.Normalize(q.Page, q.PageSize);
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var t = q.Q.Trim();
            var digits = MobileNumber.Normalize(t);
            query = query.Where(c => c.Name.Contains(t) || c.Mobile.Contains(digits.Length > 0 ? digits : t));
        }
        query = q.Sort?.ToLowerInvariant() switch
        {
            "-name" => query.OrderByDescending(c => c.Name),
            "created" => query.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id),
            "-created" => query.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id),
            _ => query.OrderBy(c => c.Name).ThenBy(c => c.Id)
        };
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var visible = await VisibleBusinessIdsAsync(rows.Select(r => r.Id).ToList(), ct);
        var items = rows.Select(c => ToDto(c, visible.GetValueOrDefault(c.Id) ?? [])).ToList();
        return new PagedResult<CustomerDto>(items, total, page, size);
    }

    private async Task<CustomerDto> ToDtoAsync(Customer c, CancellationToken ct)
    {
        var visible = await VisibleBusinessIdsAsync([c.Id], ct);
        return ToDto(c, visible.GetValueOrDefault(c.Id) ?? []);
    }

    /// <summary>Business tags the caller may see. Members never learn about businesses they are not assigned to.</summary>
    private async Task<Dictionary<long, List<long>>> VisibleBusinessIdsAsync(List<long> customerIds, CancellationToken ct)
    {
        var tags = db.CustomerBusinesses.AsNoTracking().Where(t => customerIds.Contains(t.CustomerId));
        if (!current.IsAdmin)
        {
            var uid = current.UserId ?? throw new UnauthorizedException();
            var mine = db.BusinessMembers.Where(m => m.UserId == uid && m.IsActive).Select(m => m.BusinessId);
            tags = tags.Where(t => mine.Contains(t.BusinessId));
        }
        var rows = await tags.Select(t => new { t.CustomerId, t.BusinessId }).ToListAsync(ct);
        return rows.GroupBy(r => r.CustomerId).ToDictionary(g => g.Key, g => g.Select(x => x.BusinessId).OrderBy(x => x).ToList());
    }

    private static CustomerDto ToDto(Customer c, IReadOnlyList<long> businessIds) =>
        new(c.Id, c.Mobile, c.Name, c.Address, c.StateCode, c.Gstin, c.CreatedAt, businessIds);
}
