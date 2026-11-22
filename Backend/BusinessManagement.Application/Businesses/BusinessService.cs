using BusinessManagement.Application.Abstractions;
using BusinessManagement.Application.Common;
using BusinessManagement.Domain.Entities;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace BusinessManagement.Application.Businesses;

public sealed class BusinessService(IAppDbContext db, ICurrentUser current, MembershipManager membership) : IBusinessService
{
    private void RequireAdmin() { if (!current.IsAdmin) throw new ForbiddenException(); }

    // ------------------------------------------------------------------ reads
    public async Task<PagedResult<BusinessListItemDto>> ListAsync(BusinessListQuery q, CancellationToken ct)
    {
        if (current.UserId is not long uid) throw new UnauthorizedException();
        var (page, size) = Paging.Normalize(q.Page, q.PageSize);
        var query = db.Businesses.AsNoTracking().AsQueryable();

        if (!current.IsAdmin)   // data isolation: non-admins only ever see businesses they are assigned to
            query = query.Where(b => b.Members.Any(m => m.UserId == uid && m.IsActive));
        if (q.IsActive is bool active) query = query.Where(b => b.IsActive == active);
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = q.Q.Trim();
            query = query.Where(b => b.Name.Contains(term) || (b.Category != null && b.Category.Contains(term)));
        }
        query = q.Sort?.ToLowerInvariant() switch
        {
            "-name" => query.OrderByDescending(b => b.Name),
            "created" => query.OrderBy(b => b.CreatedAt).ThenBy(b => b.Id),
            "-created" => query.OrderByDescending(b => b.CreatedAt).ThenBy(b => b.Id),
            _ => query.OrderBy(b => b.Name).ThenBy(b => b.Id)
        };

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * size).Take(size).Select(b => new BusinessListItemDto(
            b.Id, b.Name, b.Category, b.LogoUrl, b.GstNumber, b.IsActive, b.Members.Count(m => m.IsActive))).ToListAsync(ct);
        return new PagedResult<BusinessListItemDto>(items, total, page, size);
    }

    public async Task<IReadOnlyList<BusinessListItemDto>> ListForUserAsync(long userId, CancellationToken ct)
    {
        RequireAdmin();
        return await db.Businesses.AsNoTracking()
            .Where(b => b.Members.Any(m => m.UserId == userId && m.IsActive)).OrderBy(b => b.Name)
            .Select(b => new BusinessListItemDto(b.Id, b.Name, b.Category, b.LogoUrl, b.GstNumber, b.IsActive, b.Members.Count(m => m.IsActive)))
            .ToListAsync(ct);
    }

    /// <summary>Callers must already have passed the business access check (controller filter).</summary>
    public async Task<BusinessDetailDto> GetAsync(long id, CancellationToken ct)
    {
        var b = await db.Businesses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Business not found.");
        var members = await db.BusinessMembers.AsNoTracking()
            .Where(m => m.BusinessId == id && m.IsActive)
            .OrderBy(m => m.User!.Name)
            .Select(m => new BusinessMemberDto(m.UserId, m.User!.Name, m.User.Username, m.User.Role.ToString(), m.AssignedAt)).ToListAsync(ct);
        return new BusinessDetailDto(b.Id, b.Name, b.Description, b.GstNumber, b.Category, b.LogoUrl, b.AddressLine, b.StateCode, b.IsActive, members);
    }

    // ------------------------------------------------------------------ writes (Admin)
    public async Task<BusinessDetailDto> CreateAsync(CreateBusinessRequest r, CancellationToken ct)
    {
        RequireAdmin();
        if (r.MemberIds is null || r.MemberIds.Count == 0)
            throw Fail.Validation("memberIds", "Select at least one member for the business.");
        await EnsureUniqueAsync(r.Name, r.GstNumber, null, ct);

        await using var tx = await db.BeginTransactionAsync(ct);
        var members = await membership.LoadAssignableUsersAsync(r.MemberIds, UserRole.Member, "memberIds", ct);
        var employees = r.EmployeeIds is { Count: > 0 }
            ? await membership.LoadAssignableUsersAsync(r.EmployeeIds, UserRole.Employee, "employeeIds", ct) : [];

        var business = new Business { CreatedBy = current.UserId!.Value, IsActive = true };
        Apply(business, r.Name, r.Description, r.GstNumber, r.Category, r.LogoUrl, r.AddressLine, r.StateCode);
        db.Businesses.Add(business);
        await db.SaveChangesAsync(ct);

        await membership.ApplyBusinessMembersAsync(business.Id, members.Concat(employees).Select(u => u.Id).ToList(), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(business.Id, ct);
    }

    public async Task<BusinessDetailDto> UpdateAsync(long id, UpdateBusinessRequest r, CancellationToken ct)
    {
        RequireAdmin();
        var business = await db.Businesses.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("Business not found.");
        await EnsureUniqueAsync(r.Name, r.GstNumber, id, ct);
        Apply(business, r.Name, r.Description, r.GstNumber, r.Category, r.LogoUrl, r.AddressLine, r.StateCode);
        business.IsActive = r.IsActive;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        RequireAdmin();
        var business = await db.Businesses.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("Business not found.");
        business.IsDeleted = true;   // soft delete: financial history is retained; audited as Delete
        await db.SaveChangesAsync(ct);
    }

    public async Task<BusinessDetailDto> SetMembersAsync(long id, SetBusinessMembersRequest r, CancellationToken ct)
    {
        RequireAdmin();
        if (!await db.Businesses.AnyAsync(b => b.Id == id, ct)) throw new NotFoundException("Business not found.");
        if (r.MemberIds is null || r.MemberIds.Count == 0)
            throw Fail.Validation("memberIds", "Every business must have at least one assigned member.");

        await using var tx = await db.BeginTransactionAsync(ct);
        var members = await membership.LoadAssignableUsersAsync(r.MemberIds, UserRole.Member, "memberIds", ct);
        var employees = r.EmployeeIds is { Count: > 0 }
            ? await membership.LoadAssignableUsersAsync(r.EmployeeIds, UserRole.Employee, "employeeIds", ct) : [];
        await membership.ApplyBusinessMembersAsync(id, members.Concat(employees).Select(u => u.Id).ToList(), ct);
        await db.SaveChangesAsync(ct);
        await membership.EnsureMinimumMemberAsync([id], ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    // ------------------------------------------------------------------ permissions
    public async Task<BusinessPermissionsDto> GetPermissionsAsync(long businessId, long userId, CancellationToken ct)
    {
        RequireAdmin();
        var m = await LoadMembershipAsync(businessId, userId, ct);
        return ToDto(m.Permissions ?? new BusinessPermission { BusinessId = businessId, UserId = userId });
    }

    public async Task<BusinessPermissionsDto> SetPermissionsAsync(long businessId, long userId, BusinessPermissionsDto d, CancellationToken ct)
    {
        RequireAdmin();
        var m = await LoadMembershipAsync(businessId, userId, ct);
        var p = m.Permissions;
        if (p is null)
        {
            p = new BusinessPermission { BusinessId = businessId, UserId = userId };
            db.BusinessPermissions.Add(p);
        }
        Copy(d, p);
        await db.SaveChangesAsync(ct);   // AppDbContext audits old/new flag values automatically
        return ToDto(p);
    }

    // ------------------------------------------------------------------ helpers
    private async Task<BusinessMember> LoadMembershipAsync(long businessId, long userId, CancellationToken ct)
    {
        if (!await db.Businesses.AnyAsync(b => b.Id == businessId, ct)) throw new NotFoundException("Business not found.");
        return await db.BusinessMembers.Include(x => x.Permissions)
                   .FirstOrDefaultAsync(x => x.BusinessId == businessId && x.UserId == userId && x.IsActive, ct)
               ?? throw new NotFoundException("This user is not assigned to the business.");
    }

    private async Task EnsureUniqueAsync(string name, string? gst, long? exceptId, CancellationToken ct)
    {
        var n = name.Trim();
        if (await db.Businesses.AnyAsync(b => b.Name == n && b.Id != exceptId, ct))
            throw new ConflictException("A business with this name already exists.");
        if (!string.IsNullOrWhiteSpace(gst))
        {
            var g = Gstin.Normalize(gst);
            if (await db.Businesses.AnyAsync(b => b.GstNumber == g && b.Id != exceptId, ct))
                throw new ConflictException("A business with this GST number already exists.");
        }
    }

    private static void Apply(Business b, string name, string? description, string? gst, string? category, string? logo, string? address, string? stateCode)
    {
        b.Name = name.Trim();
        b.Description = description?.Trim();
        b.GstNumber = string.IsNullOrWhiteSpace(gst) ? null : Gstin.Normalize(gst);
        b.Category = category?.Trim();
        b.LogoUrl = logo;
        b.AddressLine = address?.Trim();
        // The GSTIN's first two digits ARE the state code, so it always wins over a typed value.
        b.StateCode = b.GstNumber is not null ? Gstin.StateCode(b.GstNumber) : (string.IsNullOrWhiteSpace(stateCode) ? null : stateCode.Trim());
    }

    internal static BusinessPermissionsDto ToDto(BusinessPermission p) => new(
        p.CanViewBusiness, p.CanViewCustomers, p.CanAddCustomers, p.CanEditCustomers, p.CanViewProducts,
        p.CanViewSales, p.CanAddSales, p.CanEditOwnSales, p.CanDeleteOwnSales,
        p.CanViewExpenseTotal, p.CanAddExpenses, p.CanEditOwnExpenses, p.CanDeleteOwnExpenses, p.CanViewMemberWiseExpenses,
        p.CanRecordPayments, p.CanViewPayments, p.CanEditOwnPayments, p.CanDeleteOwnPayments,
        p.CanGenerateInvoices, p.CanViewInvoices, p.CanViewReports);

    private static void Copy(BusinessPermissionsDto d, BusinessPermission p)
    {
        p.CanViewBusiness = d.CanViewBusiness; p.CanViewCustomers = d.CanViewCustomers; p.CanAddCustomers = d.CanAddCustomers;
        p.CanEditCustomers = d.CanEditCustomers; p.CanViewProducts = d.CanViewProducts;
        p.CanViewSales = d.CanViewSales; p.CanAddSales = d.CanAddSales; p.CanEditOwnSales = d.CanEditOwnSales; p.CanDeleteOwnSales = d.CanDeleteOwnSales;
        p.CanViewExpenseTotal = d.CanViewExpenseTotal; p.CanAddExpenses = d.CanAddExpenses; p.CanEditOwnExpenses = d.CanEditOwnExpenses;
        p.CanDeleteOwnExpenses = d.CanDeleteOwnExpenses; p.CanViewMemberWiseExpenses = d.CanViewMemberWiseExpenses;
        p.CanRecordPayments = d.CanRecordPayments; p.CanViewPayments = d.CanViewPayments; p.CanEditOwnPayments = d.CanEditOwnPayments;
        p.CanDeleteOwnPayments = d.CanDeleteOwnPayments; p.CanGenerateInvoices = d.CanGenerateInvoices; p.CanViewInvoices = d.CanViewInvoices;
        p.CanViewReports = d.CanViewReports;
    }
}
