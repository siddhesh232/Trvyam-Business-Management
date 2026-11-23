using BusinessManagement.Application.Abstractions;
using BusinessManagement.Infrastructure.Email;
using BusinessManagement.Infrastructure.Pdf;
using BusinessManagement.Application.Invoices;
using BusinessManagement.Infrastructure.Persistence;
using BusinessManagement.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured. Supply it via environment variable ConnectionStrings__Default.");

        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString,
            sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<IInvoicePdfGenerator, QuestPdfInvoiceGenerator>();
        return services;
    }
}
