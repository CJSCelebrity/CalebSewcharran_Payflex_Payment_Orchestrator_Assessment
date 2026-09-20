using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentOrchestratorAssessment.Application.Interfaces;
using PaymentOrchestratorAssessment.Infrastructure.DbContexts;
using PaymentOrchestratorAssessment.Infrastructure.Repositories;

namespace PaymentOrchestratorAssessment.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<PaymentsDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IPaymentRepository, PaymentRepository>();

        return services;
    }
}
