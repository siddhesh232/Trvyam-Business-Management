namespace BusinessManagement.Domain.Enums;

public enum UserRole : byte { Admin = 1, Member = 2, Employee = 3 }
public enum PaymentMode : byte { Cash = 1, Upi = 2, BankTransfer = 3, Cheque = 4, Card = 5, Other = 9 }
public enum PaymentRecordStatus : byte { Valid = 1, Reversed = 2 }
public enum PaymentStatus { Unpaid, PartiallyPaid, Paid }
public enum SalesOrderStatus : byte { Active = 1, Cancelled = 2 }
public enum SalesReturnStatus : byte { Accepted = 1, Voided = 2 }
public enum InvoiceDocType : byte { TaxInvoice = 1, CreditNote = 2 }
public enum InvoiceStatus : byte { Issued = 1, Cancelled = 2, Amended = 3 }
public enum AdjustmentType : byte { OtherIncome = 1, Loss = 2, Adjustment = 3 }
public enum AdjustmentDirection : byte { IncreasesProfit = 1, DecreasesProfit = 2 }
public enum AuditAction : byte { Create = 1, Update = 2, Delete = 3, Reverse = 4, Cancel = 5, Login = 6, PermissionChange = 7 }

/// <summary>Individual capabilities configurable per member per business (FRD s.5).</summary>
public enum Permission
{
    ViewBusiness, ViewCustomers, AddCustomers, EditCustomers, ViewProducts,
    ViewSales, AddSales, EditOwnSales, DeleteOwnSales,
    ViewExpenseTotal, AddExpenses, EditOwnExpenses, DeleteOwnExpenses, ViewMemberWiseExpenses,
    RecordPayments, ViewPayments, EditOwnPayments, DeleteOwnPayments,
    GenerateInvoices, ViewInvoices, ViewReports
}
