using BusinessManagement.Application.Auth;
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
        return services;
    }
}
