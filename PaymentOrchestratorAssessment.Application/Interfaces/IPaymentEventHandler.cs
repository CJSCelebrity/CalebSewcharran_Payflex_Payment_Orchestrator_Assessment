using PaymentOrchestratorAssessment.Core.Events;

namespace PaymentOrchestratorAssessment.Application.Interfaces;

/// <summary>
/// What happened when a confirmation event was applied.
/// </summary>
/// <remarks>
/// Three outcomes rather than a bool, because the caller has to distinguish
/// "I changed something" from "this was already done" from "no such payment",
/// and only the last of those is an error.
/// </remarks>
public enum ConfirmationOutcome
{
    /// <summary>The payment moved Pending to Confirmed on this call.</summary>
    Confirmed,

    /// <summary>The payment was already Confirmed. A duplicate delivery; not an error.</summary>
    AlreadyConfirmed,

    /// <summary>No payment exists with that id.</summary>
    PaymentNotFound
}

/// <summary>
/// Applies inbound payment events to state. The seam a real message consumer
/// would sit behind.
/// </summary>
public interface IPaymentEventHandler
{
    Task<ConfirmationOutcome> HandleAsync(PaymentConfirmationReceived @event, CancellationToken ct = default);
}
