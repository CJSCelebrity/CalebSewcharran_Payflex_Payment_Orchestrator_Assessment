using Microsoft.EntityFrameworkCore;
using PaymentOrchestratorAssessment.Core.Payments;

namespace PaymentOrchestratorAssessment.Infrastructure.DbContexts;

public class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var payment = modelBuilder.Entity<Payment>();

        payment.HasKey(p => p.Id);

        payment.Property(p => p.CustomerId)
            .IsRequired()
            .HasMaxLength(100);

        payment.Property(p => p.Amount)
            .HasPrecision(18, 2);

        // Stored as text ("Pending"/"Confirmed") rather than an ordinal, so the
        // database stays readable and adding a state later cannot silently renumber
        // existing rows.
        payment.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        payment.Property(p => p.IdempotencyKey)
            .HasMaxLength(100);

        // Partial unique index: the database, not application code, is what
        // guarantees one payment per idempotency key under concurrent retries.
        payment.HasIndex(p => p.IdempotencyKey)
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");

        payment.HasIndex(p => p.CreatedAt);
    }
}
