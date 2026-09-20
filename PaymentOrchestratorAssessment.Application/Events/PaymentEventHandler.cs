using Microsoft.Extensions.Logging;
using PaymentOrchestratorAssessment.Application.Interfaces;
using PaymentOrchestratorAssessment.Core.Events;

namespace PaymentOrchestratorAssessment.Application.Events;

public class PaymentEventHandler(
    IPaymentRepository repository,
    TimeProvider clock,
    ILogger<PaymentEventHandler> logger) : IPaymentEventHandler
{
    public async Task<ConfirmationOutcome> HandleAsync(
        PaymentConfirmationReceived @event,
        CancellationToken ct = default)
    {
        var confirmedAt = clock.GetUtcNow().UtcDateTime;

        var rowsAffected = await repository.ConfirmIfPendingAsync(@event.PaymentId, confirmedAt, ct);

        if (rowsAffected > 0)
        {
            logger.LogInformation(
                "Payment {PaymentId} confirmed from {Source} (event occurred at {OccurredAt:o})",
                @event.PaymentId, @event.Source, @event.OccurredAt);

            return ConfirmationOutcome.Confirmed;
        }

        // Zero rows means either "already confirmed" or "no such payment". Only one
        // of those is a client error, so it is worth the extra read to tell the
        // caller which.
        if (await repository.ExistsAsync(@event.PaymentId, ct))
        {
            logger.LogInformation(
                "Duplicate confirmation for payment {PaymentId} from {Source}; no state change",
                @event.PaymentId, @event.Source);

            return ConfirmationOutcome.AlreadyConfirmed;
        }

        logger.LogWarning(
            "Confirmation received from {Source} for unknown payment {PaymentId}",
            @event.Source, @event.PaymentId);

        return ConfirmationOutcome.PaymentNotFound;
    }
}
