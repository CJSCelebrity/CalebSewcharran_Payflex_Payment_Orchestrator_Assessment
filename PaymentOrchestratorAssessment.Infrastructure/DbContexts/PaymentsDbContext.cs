using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PaymentOrchestratorAssessment.Core.Payments;

namespace PaymentOrchestratorAssessment.Infrastructure.DbContexts;

public class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    // SQLite stores DateTime as TEXT with no time zone, so EF reads it back as
    // Unspecified. That serialises to JSON without a "Z", and a browser parsing
    // "2026-09-20T10:30:00" treats a UTC instant as local time. Every timestamp
    // this service writes is UTC, so say so on the way out.
    private static readonly ValueConverter<DateTime, DateTime> UtcDateTime = new(
        write => write,
        read => DateTime.SpecifyKind(read, DateTimeKind.Utc));

    private static readonly ValueConverter<DateTime?, DateTime?> NullableUtcDateTime = new(
        write => write,
        read => read.HasValue ? DateTime.SpecifyKind(read.Value, DateTimeKind.Utc) : null);

    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var payment = modelBuilder.Entity<Payment>();

        payment.HasKey(p => p.Id);

        payment.Property(p => p.CreatedAt).HasConversion(UtcDateTime);
        payment.Property(p => p.ConfirmedAt).HasConversion(NullableUtcDateTime);

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
