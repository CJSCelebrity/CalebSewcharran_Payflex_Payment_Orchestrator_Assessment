using Microsoft.EntityFrameworkCore;
using PaymentOrchestratorAssessment.Core.Payments;
using PaymentOrchestratorAssessment.Infrastructure.Repositories;
using PaymentOrchestratorAssessment.Tests.TestDoubles;
using Shouldly;

namespace PaymentOrchestratorAssessment.Tests.Repositories;

/// <summary>
/// Run against a real SQLite database rather than EF Core's InMemory provider.
/// InMemory is not relational: it ignores the unique index the idempotency
/// behaviour depends on, so those tests would pass while production failed, and it
/// cannot execute ExecuteUpdateAsync at all.
/// </summary>
public class PaymentRepositoryTests : IDisposable
{
    private static readonly DateTime Created = new(2026, 9, 20, 10, 30, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly SqliteDatabaseFixture _database = new();

    // A fresh context per logical operation, so nothing passes because a value
    // happened to still be sitting in the change tracker.
    private PaymentRepository NewRepository() => new(_database.NewContext());

    private static Payment NewPayment(string? idempotencyKey = null, DateTime? createdAt = null) => new()
    {
        Id = Guid.NewGuid(),
        CustomerId = "CUST-001",
        Amount = 750m,
        Status = PaymentStatus.Pending,
        CreatedAt = createdAt ?? Created,
        ConfirmedAt = null,
        IdempotencyKey = idempotencyKey
    };

    [Fact]
    public async Task AddAsync_persists_a_payment_that_can_be_read_back()
    {
        var payment = NewPayment();

        var result = await NewRepository().AddAsync(payment, Ct);

        result.WasInserted.ShouldBeTrue();

        var stored = await NewRepository().GetByIdAsync(payment.Id, Ct);
        stored.ShouldNotBeNull();
        stored.CustomerId.ShouldBe("CUST-001");
        stored.Amount.ShouldBe(750m);
        stored.Status.ShouldBe(PaymentStatus.Pending);
        stored.ConfirmedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Status_is_persisted_as_text_not_an_ordinal()
    {
        await NewRepository().AddAsync(NewPayment(), Ct);

        await using var db = _database.NewContext();

        // Read the raw column rather than the mapped property, so this asserts on
        // what is in the database and not on the value converter.
        var stored = await db.Database
            .SqlQueryRaw<string>("SELECT \"Status\" AS \"Value\" FROM \"Payments\"")
            .SingleAsync(Ct);

        stored.ShouldBe("Pending");
    }

    [Fact]
    public async Task AddAsync_with_a_duplicate_idempotency_key_returns_the_winner_and_inserts_nothing()
    {
        var first = await NewRepository().AddAsync(NewPayment("key-abc"), Ct);

        var second = await NewRepository().AddAsync(NewPayment("key-abc"), Ct);

        first.WasInserted.ShouldBeTrue();
        second.WasInserted.ShouldBeFalse();
        second.Payment.Id.ShouldBe(first.Payment.Id);

        await using var db = _database.NewContext();
        (await db.Payments.CountAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task AddAsync_without_an_idempotency_key_allows_many_rows()
    {
        await NewRepository().AddAsync(NewPayment(), Ct);
        await NewRepository().AddAsync(NewPayment(), Ct);

        await using var db = _database.NewContext();
        (await db.Payments.CountAsync(Ct)).ShouldBe(2);
    }

    [Fact]
    public async Task FindByIdempotencyKeyAsync_returns_null_when_the_key_is_unknown()
    {
        (await NewRepository().FindByIdempotencyKeyAsync("never-used", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task FindByIdempotencyKeyAsync_finds_the_payment_that_used_the_key()
    {
        var payment = NewPayment("key-abc");
        await NewRepository().AddAsync(payment, Ct);

        var found = await NewRepository().FindByIdempotencyKeyAsync("key-abc", Ct);

        found.ShouldNotBeNull();
        found.Id.ShouldBe(payment.Id);
    }

    [Fact]
    public async Task ExistsAsync_distinguishes_a_stored_payment_from_an_unknown_id()
    {
        var payment = NewPayment();
        await NewRepository().AddAsync(payment, Ct);

        (await NewRepository().ExistsAsync(payment.Id, Ct)).ShouldBeTrue();
        (await NewRepository().ExistsAsync(Guid.NewGuid(), Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task ConfirmIfPendingAsync_moves_a_pending_payment_and_records_when()
    {
        var payment = NewPayment();
        await NewRepository().AddAsync(payment, Ct);
        var confirmedAt = Created.AddMinutes(3);

        var rows = await NewRepository().ConfirmIfPendingAsync(payment.Id, confirmedAt, Ct);

        rows.ShouldBe(1);

        var stored = await NewRepository().GetByIdAsync(payment.Id, Ct);
        stored!.Status.ShouldBe(PaymentStatus.Confirmed);
        stored.ConfirmedAt.ShouldBe(confirmedAt);
    }

    [Fact]
    public async Task ConfirmIfPendingAsync_does_not_alter_the_amount_or_customer()
    {
        var payment = NewPayment();
        await NewRepository().AddAsync(payment, Ct);

        await NewRepository().ConfirmIfPendingAsync(payment.Id, Created, Ct);

        var stored = await NewRepository().GetByIdAsync(payment.Id, Ct);
        stored!.Amount.ShouldBe(750m);
        stored.CustomerId.ShouldBe("CUST-001");
        stored.CreatedAt.ShouldBe(Created);
    }

    /// <summary>
    /// The important one. Upstream payment events are delivered at-least-once, so a
    /// duplicate must be a no-op rather than a second state change.
    /// </summary>
    [Fact]
    public async Task ConfirmIfPendingAsync_is_a_no_op_on_an_already_confirmed_payment()
    {
        var payment = NewPayment();
        await NewRepository().AddAsync(payment, Ct);
        var firstConfirmation = Created.AddMinutes(3);

        await NewRepository().ConfirmIfPendingAsync(payment.Id, firstConfirmation, Ct);
        var rows = await NewRepository().ConfirmIfPendingAsync(payment.Id, Created.AddHours(1), Ct);

        rows.ShouldBe(0);

        // The second delivery must not move the timestamp: the confirmation happened
        // once, whatever the network did afterwards.
        var stored = await NewRepository().GetByIdAsync(payment.Id, Ct);
        stored!.ConfirmedAt.ShouldBe(firstConfirmation);
    }

    [Fact]
    public async Task ConfirmIfPendingAsync_affects_no_rows_for_an_unknown_payment()
    {
        (await NewRepository().ConfirmIfPendingAsync(Guid.NewGuid(), Created, Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task ConfirmIfPendingAsync_leaves_other_payments_pending()
    {
        var target = NewPayment();
        var bystander = NewPayment();
        await NewRepository().AddAsync(target, Ct);
        await NewRepository().AddAsync(bystander, Ct);

        await NewRepository().ConfirmIfPendingAsync(target.Id, Created, Ct);

        await using var db = _database.NewContext();
        var stillPending = await db.Payments
            .Where(p => p.Status == PaymentStatus.Pending)
            .Select(p => p.Id)
            .ToListAsync(Ct);

        stillPending.ShouldBe(new[] { bystander.Id });
    }

    [Fact]
    public async Task GetAllNewestFirstAsync_returns_newest_first()
    {
        var oldest = NewPayment(createdAt: Created);
        var newest = NewPayment(createdAt: Created.AddMinutes(5));
        await NewRepository().AddAsync(oldest, Ct);
        await NewRepository().AddAsync(newest, Ct);

        var all = await NewRepository().GetAllNewestFirstAsync(Ct);

        all.Select(p => p.Id).ShouldBe(new[] { newest.Id, oldest.Id });
    }

    [Fact]
    public async Task GetAllNewestFirstAsync_returns_an_empty_list_when_there_are_no_payments()
    {
        (await NewRepository().GetAllNewestFirstAsync(Ct)).ShouldBeEmpty();
    }

    public void Dispose() => _database.Dispose();
}
