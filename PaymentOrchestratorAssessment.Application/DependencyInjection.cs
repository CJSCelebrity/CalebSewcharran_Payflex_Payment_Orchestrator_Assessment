using Microsoft.Extensions.DependencyInjection;
using PaymentOrchestratorAssessment.Application.Events;
using PaymentOrchestratorAssessment.Application.Interfaces;
using PaymentOrchestratorAssessment.Application.Services;

namespace PaymentOrchestratorAssessment.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPaymentEventHandler, PaymentEventHandler>();

        return services;
    }
}
