namespace PaymentOrchestratorAssessment.Core.Payments;

/// <summary>
/// The lifecycle states a payment can be in.
/// </summary>
/// <remarks>
/// The brief models this as a <c>string</c>. It is an enum here so that an invalid
/// status is unrepresentable in the domain, and so the only legal transition
/// (Pending to Confirmed) can be enforced in one place. It is persisted as text
/// and exposed over the API as a string, so the stored data and the wire contract
/// both still match the brief.
/// </remarks>
public enum PaymentStatus
{
    Pending = 0,
    Confirmed = 1
}
