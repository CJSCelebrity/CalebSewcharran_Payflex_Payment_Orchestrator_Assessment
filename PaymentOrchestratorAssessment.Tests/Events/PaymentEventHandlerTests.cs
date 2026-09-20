using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PaymentOrchestratorAssessment.Application.Events;
using PaymentOrchestratorAssessment.Application.Interfaces;
using PaymentOrchestratorAssessment.Core.Events;
using PaymentOrchestratorAssessment.Tests.TestDoubles;
using Shouldly;

namespace PaymentOrchestratorAssessment.Tests.Events;

/// <summary>
/// The confirmation event is where this exercise has behaviour worth testing: a
/// state transition that must be legal, recorded, and safe to replay.
/// </summary>
public class PaymentEventHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 10, 30, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly Mock<IPaymentRepository> _repository = new(MockBehavior.Strict);
    private readonly FixedTimeProvider _clock = new(Now);
    private readonly Guid _paymentId = Guid.NewGuid();

    private PaymentEventHandler NewHandler() =>
        new(_repository.Object, _clock, NullLogger<PaymentEventHandler>.Instance);

    private PaymentConfirmationReceived Event() =>
        new(_paymentId, OccurredAt: Now, Source: "unit-test");

    private void ConfirmAffects(int rows) => _repository
        .Setup(r => r.ConfirmIfPendingAsync(_paymentId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(rows);

    private void PaymentExists(bool exists) => _repository
        .Setup(r => r.ExistsAsync(_paymentId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(exists);

    [Fact]
    public async Task Confirming_a_pending_payment_reports_Confirmed()
    {
        ConfirmAffects(1);

        var outcome = await NewHandler().HandleAsync(Event(), Ct);

        outcome.ShouldBe(ConfirmationOutcome.Confirmed);
    }

    [Fact]
    public async Task Confirming_stamps_the_update_with_the_current_server_clock()
    {
        _clock.Advance(TimeSpan.FromMinutes(3));
        ConfirmAffects(1);

        await NewHandler().HandleAsync(Event(), Ct);

        _repository.Verify(
            r => r.ConfirmIfPendingAsync(
                _paymentId,
                Now.AddMinutes(3).UtcDateTime,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_successful_confirmation_does_not_ask_whether_the_payment_exists()
    {
        ConfirmAffects(1);

        await NewHandler().HandleAsync(Event(), Ct);

        _repository.Verify(
            r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Upstream payment events are delivered at-least-once, so a duplicate must be a
    /// no-op rather than an error or a second state change.
    /// </summary>
    [Fact]
    public async Task A_duplicate_confirmation_reports_AlreadyConfirmed_rather_than_an_error()
    {
        ConfirmAffects(0);
        PaymentExists(true);

        var outcome = await NewHandler().HandleAsync(Event(), Ct);

        outcome.ShouldBe(ConfirmationOutcome.AlreadyConfirmed);
    }

    [Fact]
    public async Task Confirming_an_unknown_payment_reports_PaymentNotFound()
    {
        ConfirmAffects(0);
        PaymentExists(false);

        var outcome = await NewHandler().HandleAsync(Event(), Ct);

        outcome.ShouldBe(ConfirmationOutcome.PaymentNotFound);
    }

    [Fact]
    public async Task The_handler_confirms_only_the_payment_the_event_names()
    {
        var otherId = Guid.NewGuid();
        ConfirmAffects(1);

        await NewHandler().HandleAsync(Event(), Ct);

        _repository.Verify(
            r => r.ConfirmIfPendingAsync(otherId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
