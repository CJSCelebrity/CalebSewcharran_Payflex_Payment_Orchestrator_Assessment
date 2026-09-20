namespace PaymentOrchestratorAssessment.Application.Contracts;

/// <summary>
/// The payment as the API exposes it. Status is a string on the wire, matching the brief.
/// </summary>
public record PaymentResponse(
    Guid Id,
    string CustomerId,
    decimal Amount,
    string Status,
    DateTime CreatedAt,
    DateTime? ConfirmedAt);
