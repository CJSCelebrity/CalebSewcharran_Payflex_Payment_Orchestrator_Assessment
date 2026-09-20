using Microsoft.AspNetCore.Mvc;
using PaymentOrchestratorAssessment.Application.Contracts;
using PaymentOrchestratorAssessment.Application.Interfaces;
using PaymentOrchestratorAssessment.Core.Events;

namespace PaymentOrchestratorAssessment.Api.Controllers;

/// <summary>
/// HTTP transport for payments. Holds no business logic: it translates requests
/// into service or handler calls and outcomes into status codes.
/// </summary>
[ApiController]
[Produces("application/json")]
public class PaymentsController(
    IPaymentService payments,
    IPaymentEventHandler confirmations) : ControllerBase
{
    /// <summary>Lists all payments, newest first.</summary>
    [HttpGet("/payments")]
    [ProducesResponseType<IReadOnlyList<PaymentResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PaymentResponse>>> GetAll(CancellationToken ct)
    {
        return Ok(await payments.GetAllAsync(ct));
    }

    /// <summary>Fetches a single payment.</summary>
    /// <remarks>
    /// Not in the brief, but POST /payments returns 201 and a 201 needs somewhere
    /// for its Location header to point at.
    /// </remarks>
    [HttpGet("/payments/{paymentId:guid}")]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentResponse>> GetById(Guid paymentId, CancellationToken ct)
    {
        var payment = await payments.GetByIdAsync(paymentId, ct);

        return payment is null
            ? NotFoundProblem(paymentId)
            : Ok(payment);
    }

    /// <summary>Creates a payment with status Pending.</summary>
    /// <param name="request">Customer and amount.</param>
    /// <param name="idempotencyKey">
    /// Optional. Repeating a request with the same key returns the payment created
    /// the first time instead of creating another one.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("/payments")]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PaymentResponse>> Create(
        CreatePaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var result = await payments.CreateAsync(request, idempotencyKey, ct);

        // A replayed idempotent request did not create anything, so 201 would be a lie.
        if (!result.WasCreated)
        {
            return Ok(result.Payment);
        }

        return CreatedAtAction(
            nameof(GetById),
            new { paymentId = result.Payment.Id },
            result.Payment);
    }

    /// <summary>
    /// Stands in for the upstream confirmation event, in place of a Kafka consumer
    /// or a provider webhook.
    /// </summary>
    /// <remarks>
    /// The route is the one the brief specifies. Note that it is the endpoint that
    /// is the stand-in, not the state change: the controller builds a
    /// <see cref="PaymentConfirmationReceived"/> and hands it to the same handler a
    /// real consumer would call, so replacing this endpoint with a consumer touches
    /// no business logic.
    ///
    /// Safe to call repeatedly. A second call on an already-confirmed payment
    /// returns 200 with the payment unchanged rather than an error, because
    /// at-least-once delivery means duplicates are normal traffic, not faults.
    /// </remarks>
    [HttpPost("/simulate-confirmation/{paymentId:guid}")]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentResponse>> SimulateConfirmation(
        Guid paymentId,
        CancellationToken ct)
    {
        var @event = new PaymentConfirmationReceived(
            paymentId,
            OccurredAt: DateTimeOffset.UtcNow,
            Source: "simulated-webhook");

        var outcome = await confirmations.HandleAsync(@event, ct);

        if (outcome == ConfirmationOutcome.PaymentNotFound)
        {
            return NotFoundProblem(paymentId);
        }

        var payment = await payments.GetByIdAsync(paymentId, ct);

        return payment is null
            ? NotFoundProblem(paymentId)
            : Ok(payment);
    }

    private ObjectResult NotFoundProblem(Guid paymentId) => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Payment not found",
        detail: $"No payment exists with id {paymentId}.");
}
