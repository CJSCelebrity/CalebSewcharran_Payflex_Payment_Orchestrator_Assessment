namespace PaymentOrchestratorAssessment.Tests.TestDoubles;

/// <summary>
/// A clock the tests control, so timestamps can be asserted exactly.
/// </summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now = Now.Add(by);
}
