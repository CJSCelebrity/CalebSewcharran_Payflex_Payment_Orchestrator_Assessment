using PaymentOrchestratorAssessment.Core.Payments;

namespace PaymentOrchestratorAssessment.Application.Interfaces;

/// <summary>
/// What the database did with an insert. <see cref="WasInserted"/> is false when
/// the unique index on the idempotency key rejected the row, in which case
/// <see cref="Payment"/> is the one that won the race.
/// </summary>
public record AddPaymentResult(Payment Payment, bool WasInserted);

public interface IPaymentRepository
{
    Task<IReadOnlyList<Payment>> GetAllNewestFirstAsync(CancellationToken ct = default);

    Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<Payment?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);

    Task<AddPaymentResult> AddAsync(Payment payment, CancellationToken ct = default);

    /// <summary>
    /// Moves a Pending payment to Confirmed in one conditional UPDATE. Returns rows
    /// affected, which is 0 when the payment is missing or already confirmed.
    /// </summary>
    Task<int> ConfirmIfPendingAsync(Guid id, DateTime confirmedAt, CancellationToken ct = default);
}
