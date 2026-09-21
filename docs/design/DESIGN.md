# Design notes

Why this solution is shaped the way it is, including the parts that are deliberately
simpler than production would be.

---

## The event simulation

`POST /simulate-confirmation/{paymentId}` does not change state. It builds a
`PaymentConfirmationReceived` and hands it to `IPaymentEventHandler`. That handler is
what a Kafka consumer, an SQS listener or a provider webhook would call after
deserialising a message. The controller is just one more transport in front of it.
Replacing the simulation with a real consumer means adding a consumer class and deleting
an endpoint. No business logic moves.

The event carries `PaymentId`, `OccurredAt` and `Source` rather than being a bare id,
because "when did the upstream system say this happened" and "which channel delivered
it" are the first two questions asked when a payment is disputed.

### Dispatch is synchronous, on purpose

An in-memory queue with a `BackgroundService` would be closer to production, but it
would make the endpoint eventually consistent. A UI that refreshes immediately after a
`202` would show a payment that is still `Pending`, and the brief explicitly wants the
list to refresh and show `Confirmed`.

The honest simplification is synchronous dispatch behind an interface, with the
asynchrony noted here rather than half-built.

`/simulate-confirmation/{id}` is a verb at the root, which is not how a production API
would model it. That would be a sub-resource: `POST /payments/{id}/confirmations`. The
brief specifies this route, so it stays.

---

## Idempotency

Upstream payment events are delivered at-least-once. A provider that does not get a
timely `2xx` re-sends, so the same confirmation arriving twice is normal traffic.

The state change is a single conditional update:

```sql
UPDATE Payments SET Status = 'Confirmed', ConfirmedAt = @now
WHERE Id = @id AND Status = 'Pending'
```

Expressed in EF Core as `ExecuteUpdateAsync` with the status predicate in the `WHERE`.
Load-check-save would need a transaction or a concurrency token to be safe against two
confirmations racing. This is atomic in the database, so the second one affects zero rows
and the handler reports `AlreadyConfirmed`. `ConfirmedAt` is not overwritten by a
duplicate: the confirmation happened once, whatever the network did afterwards.

`POST /payments` accepts an optional `Idempotency-Key` header; repeating a request with the same key returns the payment created the first time, with `200` instead of `201`.

A partial unique index on the column is what actually guarantees it. Two concurrent
requests with the same key cannot both insert, and the loser catches the constraint
violation and returns the winner's payment. The service checks for an existing key first
as a fast path, but that check is a courtesy; the index is the guarantee.

The frontend generates a key per submission and also disables the submit button while a
request is in flight, which covers the double-click case before it reaches the network.

---

## Data

The brief models `Status` as a `string`. It is a `PaymentStatus` enum in the domain so
that an invalid status cannot be represented and the single legal transition
(`Pending` to `Confirmed`) lives in one place. It is persisted with `HasConversion<string>()`
so the column holds `"Pending"` or `"Confirmed"`, and serialised as a string, so both the
database and the wire contract still match the brief.

Storing text rather than an ordinal also means adding a state later (`Cancelled`,
`Failed`) cannot silently renumber existing rows.

The brief's model has four fields. `ConfirmedAt` is added because a payment system that
cannot say *when* a state change happened cannot be reconciled or audited. It is null
while the payment is pending.

`CreatePaymentRequest` exposes only `CustomerId` and `Amount`. A client cannot choose its
own `Id`, set `Status` to `Confirmed`, or backdate `CreatedAt`. All timestamps are set
server-side in UTC from an injected `TimeProvider` and converted for display in the
browser.

Validation is by data annotations on the DTO, so failures come back as
`ValidationProblemDetails` from the framework rather than hand-rolled checks. The frontend
mirrors the same rules for fast feedback but does not rely on them.

---

## Testing

### Why the tests split in two

The layering exists mostly to make this split possible.

**Application layer, with Moq.** `PaymentServiceTests` and `PaymentEventHandlerTests`
mock `IPaymentRepository`, so they assert the service's own decisions: what it stamps,
what it trims, and when it does or does not go to the database. They use
`MockBehavior.Strict`, so an unexpected repository call fails the test. That is what
actually enforces "no idempotency key means no key lookup" and "a successful confirmation
does not re-query existence", rather than a `Times.Never` assertion alone.

**Infrastructure layer, against real SQLite.** `PaymentRepositoryTests` runs against a
real SQLite database held in memory, not EF Core's `InMemory` provider. `InMemory` is not
relational: it ignores the unique index that the idempotency behaviour depends on, so
those tests would pass against it while the production code failed, and it cannot execute
`ExecuteUpdateAsync` at all. SQLite in-memory mode gives real relational semantics at
effectively the same speed.

Each test uses a fresh `DbContext` per logical operation, so nothing passes because a
value happened to still be sitting in the change tracker.

### What is covered

Backend:

- A new payment starts `Pending`, with a server-stamped UTC `CreatedAt`
- The customer id is trimmed, and a blank idempotency key is treated as absent
- Repeating a key returns the original payment and creates no second row
- Losing the insert race returns the winner
- Status is persisted as text, asserted by reading the raw column rather than the mapped property
- Confirming a pending payment moves it to `Confirmed` and records `ConfirmedAt`
- A duplicate confirmation is a no-op, with the timestamp unchanged
- Confirming an unknown payment reports `PaymentNotFound`
- Confirming one payment leaves the others `Pending`, and does not alter amount or customer
- Timestamps come back from the database as UTC
- The list comes back newest first

Frontend: form validation and trimming, the list's loading, empty, pending and
confirmed states, and the `api` module's `ProblemDetails` handling.

### What is not covered

There are no controller tests and no `WebApplicationFactory` integration tests. The
controller holds no logic beyond mapping outcomes to status codes, and those mappings were
verified by hand against a running instance. That is a real gap: the two bugs below were
both found by running the service, not by the test suite, and integration tests would have
caught the first one.


## Known trade-offs

Deliberate simplifications rather than oversights.

- **`EnsureCreated()` instead of migrations.** A reviewer should not need the `dotnet-ef`
  tool to start the service. `EnsureCreated` cannot evolve a schema, so a real service
  would use migrations from the first commit.
- **`decimal` on SQLite.** The EF Core SQLite provider stores `decimal` as `TEXT`, so
  ordering or range-filtering on `Amount` in SQL would sort lexicographically. Nothing
  here sorts or filters by amount, but this is a reason SQLite is a development
  convenience and not the production database.
- **Synchronous event dispatch**, discussed above.
- **No authentication.** Out of scope for the brief. In production this API would sit
  behind service-to-service auth, and `CustomerId` would be validated against a customer
  service rather than accepted as free text.
- **No pagination on `GET /payments`.** Fine for a demo, wrong for a real ledger.
- **The frontend re-fetches the list after each mutation** rather than patching local
  state. One extra request keeps the client from drifting out of step with the server,
  which is the right default until it costs something.
- **CORS origins are hard-coded to the two local Vite ports.** A deployed frontend would
  need its origin added to configuration.
