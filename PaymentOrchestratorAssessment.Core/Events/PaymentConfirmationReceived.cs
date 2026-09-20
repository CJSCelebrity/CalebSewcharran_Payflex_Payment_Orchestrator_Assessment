namespace PaymentOrchestratorAssessment.Core.Events;

/// <summary>
/// An inbound event stating that a payment has been confirmed upstream (by a
/// provider webhook, a Kafka message, a settlement file).
/// </summary>
/// <remarks>
/// This is the message, not the HTTP request. <c>POST /simulate-confirmation/{id}</c>
/// is only one transport that can deliver it; a Kafka consumer would construct the
/// same record and hand it to the same handler.
/// </remarks>
/// <param name="PaymentId">The payment the event refers to.</param>
/// <param name="OccurredAt">When the upstream system says the event happened.</param>
/// <param name="Source">Which transport delivered it, for traceability in logs.</param>
public record PaymentConfirmationReceived(Guid PaymentId, DateTimeOffset OccurredAt, string Source);
