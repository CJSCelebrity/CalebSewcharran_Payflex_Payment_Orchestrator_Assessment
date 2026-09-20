namespace PaymentOrchestratorAssessment.Core.Payments;

/// <summary>
/// A payment instruction raised by a customer.
/// </summary>
public class Payment
{
    public Guid Id { get; set; }

    public string CustomerId { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public PaymentStatus Status { get; set; }

    /// <summary>Always UTC. Set by the server, never by the client.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the confirmation event was applied. Null while the payment is pending.
    /// Not required by the brief, but a payment system that cannot say when a state
    /// change happened cannot be reconciled or audited.
    /// </summary>
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>
    /// Optional client-supplied key used to collapse retries of the same create
    /// request into a single payment. Null when the caller did not send one.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}
