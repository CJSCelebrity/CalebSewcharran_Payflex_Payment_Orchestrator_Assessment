using PaymentOrchestratorAssessment.Application.Contracts;

namespace PaymentOrchestratorAssessment.Application.Interfaces;

/// <summary>
/// The result of a create request. <see cref="WasCreated"/> is false when an
/// idempotency key matched an existing payment, which lets the controller answer
/// 200 instead of 201 without the service knowing anything about HTTP.
/// </summary>
public record CreatePaymentResult(PaymentResponse Payment, bool WasCreated);

public interface IPaymentService
{
    Task<IReadOnlyList<PaymentResponse>> GetAllAsync(CancellationToken ct = default);

    Task<PaymentResponse?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<CreatePaymentResult> CreateAsync(
        CreatePaymentRequest request,
        string? idempotencyKey,
        CancellationToken ct = default);
}
