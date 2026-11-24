using BusinessManagement.Application.Adjustments;
using BusinessManagement.Application.Audit;
using BusinessManagement.Application.Auth;
using BusinessManagement.Application.Businesses;
using BusinessManagement.Application.Customers;
using BusinessManagement.Application.Expenses;
using BusinessManagement.Application.Financials;
using BusinessManagement.Application.Invoices;
using BusinessManagement.Application.Payments;
using BusinessManagement.Application.Returns;
using BusinessManagement.Application.Sales;
using BusinessManagement.Application.Products;
using BusinessManagement.Application.Users;
using BusinessManagement.Application.Authorization;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessManagement.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>(ServiceLifetime.Singleton);
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IBusinessAccessService, BusinessAccessService>();
        services.AddScoped<MembershipManager>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IBusinessService, BusinessService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IExpenseService, ExpenseService>();
        services.AddScoped<ISalesService, SalesService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<InvoiceSequenceAllocator>();
        services.AddScoped<IReturnService, ReturnService>();
        services.AddScoped<IAdjustmentService, AdjustmentService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<FinancialDataLoader>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IReportService, ReportService>();
        return services;
    }
}
