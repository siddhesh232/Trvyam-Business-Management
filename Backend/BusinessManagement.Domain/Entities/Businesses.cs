using BusinessManagement.Domain.Common;
using BusinessManagement.Domain.Enums;

namespace BusinessManagement.Domain.Entities;

public class Business : ISoftDeletable
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? GstNumber { get; set; }
    public string? Category { get; set; }
    public string? LogoUrl { get; set; }
    public string? AddressLine { get; set; }
    public string? StateCode { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<BusinessMember> Members { get; set; } = [];
}

/// <summary>Grants ACCESS to a business. What the member may DO is in <see cref="BusinessPermission"/>.</summary>
public class BusinessMember
{
    public long BusinessId { get; set; }
    public long UserId { get; set; }
    public DateTime AssignedAt { get; set; }
    public long AssignedBy { get; set; }
    public bool IsActive { get; set; } = true;
    public Business? Business { get; set; }
    public User? User { get; set; }
    public BusinessPermission? Permissions { get; set; }
}

public class BusinessPermission
{
    public long BusinessId { get; set; }
    public long UserId { get; set; }
    public bool CanViewBusiness { get; set; } = true;
    public bool CanViewCustomers { get; set; }
    public bool CanAddCustomers { get; set; }
    public bool CanEditCustomers { get; set; }
    public bool CanViewProducts { get; set; }
    public bool CanViewSales { get; set; }
    public bool CanAddSales { get; set; }
    public bool CanEditOwnSales { get; set; }
    public bool CanDeleteOwnSales { get; set; }
    public bool CanViewExpenseTotal { get; set; }
    public bool CanAddExpenses { get; set; }
    public bool CanEditOwnExpenses { get; set; }
    public bool CanDeleteOwnExpenses { get; set; }
    /// <summary>Independent of CanViewExpenseTotal; grants no edit/delete rights (FRD s.5).</summary>
    public bool CanViewMemberWiseExpenses { get; set; }
    public bool CanRecordPayments { get; set; }
    public bool CanViewPayments { get; set; }
    public bool CanEditOwnPayments { get; set; }
    public bool CanDeleteOwnPayments { get; set; }
    public bool CanGenerateInvoices { get; set; }
    public bool CanViewInvoices { get; set; }
    public bool CanViewReports { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public BusinessMember? Member { get; set; }

    public bool Has(Permission p) => p switch
    {
        Permission.ViewBusiness => CanViewBusiness,
        Permission.ViewCustomers => CanViewCustomers,
        Permission.AddCustomers => CanAddCustomers,
        Permission.EditCustomers => CanEditCustomers,
        Permission.ViewProducts => CanViewProducts,
        Permission.ViewSales => CanViewSales,
        Permission.AddSales => CanAddSales,
        Permission.EditOwnSales => CanEditOwnSales,
        Permission.DeleteOwnSales => CanDeleteOwnSales,
        Permission.ViewExpenseTotal => CanViewExpenseTotal,
        Permission.AddExpenses => CanAddExpenses,
        Permission.EditOwnExpenses => CanEditOwnExpenses,
        Permission.DeleteOwnExpenses => CanDeleteOwnExpenses,
        Permission.ViewMemberWiseExpenses => CanViewMemberWiseExpenses,
        Permission.RecordPayments => CanRecordPayments,
        Permission.ViewPayments => CanViewPayments,
        Permission.EditOwnPayments => CanEditOwnPayments,
        Permission.DeleteOwnPayments => CanDeleteOwnPayments,
        Permission.GenerateInvoices => CanGenerateInvoices,
        Permission.ViewInvoices => CanViewInvoices,
        Permission.ViewReports => CanViewReports,
        _ => false
    };
}
