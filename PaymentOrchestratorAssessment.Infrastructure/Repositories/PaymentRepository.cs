using Microsoft.EntityFrameworkCore;
using PaymentOrchestratorAssessment.Application.Interfaces;
using PaymentOrchestratorAssessment.Core.Payments;
using PaymentOrchestratorAssessment.Infrastructure.DbContexts;

namespace PaymentOrchestratorAssessment.Infrastructure.Repositories;

public class PaymentRepository(PaymentsDbContext db) : IPaymentRepository
{
    // Newest first: the payment a user just created is the one they are looking for.
    // AsNoTracking because nothing read here is going to be mutated.
    public async Task<IReadOnlyList<Payment>> GetAllNewestFirstAsync(CancellationToken ct = default) =>
        await db.Payments
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

    public Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Payment?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default) =>
        db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey, ct);

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) =>
        db.Payments.AsNoTracking().AnyAsync(p => p.Id == id, ct);

    public async Task<AddPaymentResult> AddAsync(Payment payment, CancellationToken ct = default)
    {
        db.Payments.Add(payment);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (payment.IdempotencyKey is not null)
        {
            // Two requests with the same key raced and the unique index caught the
            // loser. The winner's payment is the answer.
            db.Entry(payment).State = EntityState.Detached;

            var winner = await FindByIdempotencyKeyAsync(payment.IdempotencyKey, ct);

            if (winner is null)
            {
                throw; // Not the conflict we assumed; let it surface.
            }

            return new AddPaymentResult(winner, WasInserted: false);
        }

        return new AddPaymentResult(payment, WasInserted: true);
    }

    // One conditional UPDATE does the whole job:
    //
    //   UPDATE Payments SET Status='Confirmed', ConfirmedAt=@t
    //   WHERE Id=@id AND Status='Pending'
    //
    // Read-check-save would need a transaction or a concurrency token to be safe
    // against two confirmations arriving at once; this is atomic in the database, so
    // the second one simply affects zero rows. Upstream payment events are delivered
    // at-least-once, so that case is normal traffic rather than a fault.
    public Task<int> ConfirmIfPendingAsync(Guid id, DateTime confirmedAt, CancellationToken ct = default) =>
        db.Payments
            .Where(p => p.Id == id && p.Status == PaymentStatus.Pending)
            .ExecuteUpdateAsync(setters =>
            {
                setters.SetProperty(p => p.Status, PaymentStatus.Confirmed);
                setters.SetProperty(p => p.ConfirmedAt, confirmedAt);
            }, ct);
}
