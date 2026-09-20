using PaymentOrchestratorAssessment.Application.Contracts;
using PaymentOrchestratorAssessment.Core.Payments;

namespace PaymentOrchestratorAssessment.Application.Mappers;

public static class PaymentMapper
{
    public static PaymentResponse ToResponse(this Payment payment) => new(
        payment.Id,
        payment.CustomerId,
        payment.Amount,
        payment.Status.ToString(),
        payment.CreatedAt,
        payment.ConfirmedAt);
}
