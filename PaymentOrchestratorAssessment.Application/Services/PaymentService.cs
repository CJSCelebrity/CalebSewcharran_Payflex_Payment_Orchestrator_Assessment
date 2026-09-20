using Microsoft.Extensions.Logging;
using PaymentOrchestratorAssessment.Application.Contracts;
using PaymentOrchestratorAssessment.Application.Interfaces;
using PaymentOrchestratorAssessment.Application.Mappers;
using PaymentOrchestratorAssessment.Core.Payments;

namespace PaymentOrchestratorAssessment.Application.Services;

public class PaymentService(
    IPaymentRepository repository,
    TimeProvider clock,
    ILogger<PaymentService> logger) : IPaymentService
{
    public async Task<IReadOnlyList<PaymentResponse>> GetAllAsync(CancellationToken ct = default)
    {
        var payments = await repository.GetAllNewestFirstAsync(ct);

        return [.. payments.Select(p => p.ToResponse())];
    }

    public async Task<PaymentResponse?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var payment = await repository.GetByIdAsync(id, ct);

        return payment?.ToResponse();
    }

    public async Task<CreatePaymentResult> CreateAsync(
        CreatePaymentRequest request,
        string? idempotencyKey,
        CancellationToken ct = default)
    {
        var key = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();

        // Fast path: we have already seen this key, so return the payment we made the
        // first time rather than creating a second one. A double-clicked "Create"
        // button is the everyday version of this; a retried webhook is the version
        // that actually costs money.
        if (key is not null)
        {
            var existing = await repository.FindByIdempotencyKeyAsync(key, ct);

            if (existing is not null)
            {
                logger.LogInformation(
                    "Idempotency key {IdempotencyKey} already mapped to payment {PaymentId}",
                    key, existing.Id);

                return new CreatePaymentResult(existing.ToResponse(), WasCreated: false);
            }
        }

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            CustomerId = request.CustomerId.Trim(),
            Amount = request.Amount,
            Status = PaymentStatus.Pending,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            ConfirmedAt = null,
            IdempotencyKey = key
        };

        var result = await repository.AddAsync(payment, ct);

        // Another request with the same key raced past the check above and won.
        if (!result.WasInserted)
        {
            logger.LogInformation(
                "Lost idempotency race for key {IdempotencyKey}; returning payment {PaymentId}",
                key, result.Payment.Id);

            return new CreatePaymentResult(result.Payment.ToResponse(), WasCreated: false);
        }

        logger.LogInformation(
            "Created payment {PaymentId} for customer {CustomerId} of {Amount}",
            result.Payment.Id, result.Payment.CustomerId, result.Payment.Amount);

        return new CreatePaymentResult(result.Payment.ToResponse(), WasCreated: true);
    }
}
