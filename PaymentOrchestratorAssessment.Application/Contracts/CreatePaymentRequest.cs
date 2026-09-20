using System.ComponentModel.DataAnnotations;

namespace PaymentOrchestratorAssessment.Application.Contracts;

/// <summary>
/// The only fields a client may supply when creating a payment.
/// </summary>
/// <remarks>
/// Deliberately not the <c>Payment</c> entity: a client must not be able to choose
/// its own <c>Id</c>, set <c>Status</c> to Confirmed, or backdate <c>CreatedAt</c>.
/// </remarks>
public record CreatePaymentRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(100, MinimumLength = 1)]
    public string CustomerId { get; init; } = string.Empty;

    /// <summary>Must be positive. A zero or negative payment is not a payment.</summary>
    /// <remarks>
    /// The bounds are parsed with the invariant culture. Without that, a machine
    /// whose locale uses "," as the decimal separator cannot parse "0.01" and every
    /// create request fails with a 500 instead of validating.
    /// </remarks>
    [Range(typeof(decimal), "0.01", "1000000.00",
        ParseLimitsInInvariantCulture = true,
        ErrorMessage = "Amount must be between 0.01 and 1000000.00.")]
    public decimal Amount { get; init; }
}
