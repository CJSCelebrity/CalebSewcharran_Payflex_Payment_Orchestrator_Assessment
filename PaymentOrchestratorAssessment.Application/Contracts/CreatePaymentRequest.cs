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
    [Range(typeof(decimal), "0.01", "1000000.00",
        ErrorMessage = "Amount must be between 0.01 and 1000000.00.")]
    public decimal Amount { get; init; }
}
