using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PaymentOrchestratorAssessment.Application.Contracts;
using PaymentOrchestratorAssessment.Application.Interfaces;
using PaymentOrchestratorAssessment.Application.Services;
using PaymentOrchestratorAssessment.Core.Payments;
using PaymentOrchestratorAssessment.Tests.TestDoubles;
using Shouldly;

namespace PaymentOrchestratorAssessment.Tests.Services;

/// <summary>
/// The repository is mocked here, so these assert the service's own decisions:
/// what it stamps, what it trims, and when it does or does not go to the database.
/// Whether the database actually enforces the idempotency key is covered by the
/// repository tests, which run against real SQLite.
/// </summary>
public class PaymentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 10, 30, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly Mock<IPaymentRepository> _repository = new(MockBehavior.Strict);
    private readonly FixedTimeProvider _clock = new(Now);

    private PaymentService NewService() =>
        new(_repository.Object, _clock, NullLogger<PaymentService>.Instance);

    private void ExpectInsertToSucceed() => _repository
        .Setup(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((Payment p, CancellationToken _) => new AddPaymentResult(p, WasInserted: true));

    private static CreatePaymentRequest Request(string customerId = "CUST-001", decimal amount = 1499.99m) =>
        new() { CustomerId = customerId, Amount = amount };

    private static Payment ExistingPayment(string? idempotencyKey = null) => new()
    {
        Id = Guid.NewGuid(),
        CustomerId = "CUST-001",
        Amount = 250m,
        Status = PaymentStatus.Pending,
        CreatedAt = Now.UtcDateTime,
        IdempotencyKey = idempotencyKey
    };

    [Fact]
    public async Task CreateAsync_starts_the_payment_as_Pending()
    {
        ExpectInsertToSucceed();

        var result = await NewService().CreateAsync(Request(), idempotencyKey: null, Ct);

        result.WasCreated.ShouldBeTrue();
        result.Payment.Status.ShouldBe("Pending");
        result.Payment.CustomerId.ShouldBe("CUST-001");
        result.Payment.Amount.ShouldBe(1499.99m);
        result.Payment.ConfirmedAt.ShouldBeNull();
        result.Payment.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task CreateAsync_stamps_CreatedAt_from_the_server_clock_in_UTC()
    {
        ExpectInsertToSucceed();

        var result = await NewService().CreateAsync(Request(), idempotencyKey: null, Ct);

        result.Payment.CreatedAt.ShouldBe(Now.UtcDateTime);
    }

    [Fact]
    public async Task CreateAsync_trims_the_customer_id()
    {
        ExpectInsertToSucceed();

        var result = await NewService().CreateAsync(Request("  CUST-001  "), idempotencyKey: null, Ct);

        result.Payment.CustomerId.ShouldBe("CUST-001");
    }

    [Fact]
    public async Task CreateAsync_without_a_key_never_looks_up_by_idempotency_key()
    {
        ExpectInsertToSucceed();

        await NewService().CreateAsync(Request(), idempotencyKey: null, Ct);

        _repository.Verify(
            r => r.FindByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAsync_treats_a_blank_idempotency_key_as_absent()
    {
        ExpectInsertToSucceed();

        await NewService().CreateAsync(Request(), idempotencyKey: "   ", Ct);

        _repository.Verify(
            r => r.FindByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAsync_trims_the_idempotency_key_before_storing_it()
    {
        Payment? inserted = null;

        _repository
            .Setup(r => r.FindByIdempotencyKeyAsync("key-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment?)null);
        _repository
            .Setup(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .Callback((Payment p, CancellationToken _) => inserted = p)
            .ReturnsAsync((Payment p, CancellationToken _) => new AddPaymentResult(p, WasInserted: true));

        await NewService().CreateAsync(Request(), idempotencyKey: "  key-abc  ", Ct);

        inserted.ShouldNotBeNull();
        inserted.IdempotencyKey.ShouldBe("key-abc");
    }

    [Fact]
    public async Task CreateAsync_with_a_known_idempotency_key_returns_the_original_and_does_not_insert()
    {
        var original = ExistingPayment("key-abc");

        _repository
            .Setup(r => r.FindByIdempotencyKeyAsync("key-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(original);

        var result = await NewService().CreateAsync(Request(), idempotencyKey: "key-abc", Ct);

        result.WasCreated.ShouldBeFalse();
        result.Payment.Id.ShouldBe(original.Id);
        _repository.Verify(
            r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAsync_that_loses_the_insert_race_returns_the_winner()
    {
        var winner = ExistingPayment("key-abc");

        _repository
            .Setup(r => r.FindByIdempotencyKeyAsync("key-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment?)null);
        _repository
            .Setup(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AddPaymentResult(winner, WasInserted: false));

        var result = await NewService().CreateAsync(Request(), idempotencyKey: "key-abc", Ct);

        result.WasCreated.ShouldBeFalse();
        result.Payment.Id.ShouldBe(winner.Id);
    }

    [Fact]
    public async Task GetAllAsync_maps_every_payment_the_repository_returns()
    {
        IReadOnlyList<Payment> payments = [ExistingPayment(), ExistingPayment()];

        _repository
            .Setup(r => r.GetAllNewestFirstAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(payments);

        var all = await NewService().GetAllAsync(Ct);

        all.Count.ShouldBe(2);
        all.Select(p => p.Id).ShouldBe(payments.Select(p => p.Id));
        all.ShouldAllBe(p => p.Status == "Pending");
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_for_an_unknown_id()
    {
        _repository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment?)null);

        (await NewService().GetByIdAsync(Guid.NewGuid(), Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task GetByIdAsync_maps_a_found_payment()
    {
        var payment = ExistingPayment();

        _repository
            .Setup(r => r.GetByIdAsync(payment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var found = await NewService().GetByIdAsync(payment.Id, Ct);

        found.ShouldNotBeNull();
        found.Id.ShouldBe(payment.Id);
        found.Status.ShouldBe("Pending");
    }
}
