# Design notes

Why this solution is shaped the way it is, including the parts that are deliberately
simpler than production would be.

---

## Layering

### Why clean architecture here

Five projects for three endpoints is more ceremony than this feature needs on its own
terms. A single Web API project with folders would satisfy the brief's request for
"services, controllers, models" and would be quicker to read.

It is layered anyway for two reasons, and it is worth being honest that only the second
is technical:

1. Consistency with the other assessments in this codebase, which use the same
   Api / Application / Core / Infrastructure split.
2. The `IPaymentRepository` seam. Once `PaymentService` and `PaymentEventHandler` depend
   on an interface rather than a `DbContext`, they can be unit-tested with a mock and no
   database at all, while the behaviour that genuinely needs a relational engine gets
   tested against one. That split is described under [Testing](#testing) and is the main
   thing the layering buys.

Dependencies point inward: Api to Infrastructure to Application to Core. Core has no
package references at all.

### Core is the domain layer

In the sibling Ninety One assessment, `Core` is the console entry point. There is no
console app here, so `Core` carries its conventional meaning: the domain. It holds
`Payment`, `PaymentStatus` and the `PaymentConfirmationReceived` event contract, and
nothing else. The difference in meaning between the two repositories is deliberate, not
an oversight.

### DTOs live in Application, not Api

`CreatePaymentRequest` and `PaymentResponse` sit in the Application layer because
`IPaymentService` names them in its signatures. Putting them in Api would invert the
dependency.

The alternative, a second near-identical set of wire contracts in Api plus a mapper
between them, would buy isolation from HTTP concerns that this project does not need at
this size. The data annotations on `CreatePaymentRequest` still drive ASP.NET model
validation from the Application assembly, because the attributes travel with the type.

---

## The event simulation

### The endpoint is a transport, not the logic

This is the part of the brief that is really about event-driven thinking, so the shape
matters more than the route.

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

### The route keeps the brief's shape

`/simulate-confirmation/{id}` is a verb at the root, which is not how a production API
would model it. That would be a sub-resource: `POST /payments/{id}/confirmations`. The
brief specifies this route, so it stays.

---

## Idempotency

### Confirmation, enforced by the database

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

Zero rows affected is ambiguous, meaning either already confirmed or no such payment, so
the handler does one follow-up existence check and returns three distinct outcomes.
`AlreadyConfirmed` answers `200`, because a duplicate delivery is not a client error.
Only `PaymentNotFound` answers `404`.

### Create, optionally

A `POST` that moves money is where duplicates hurt. `POST /payments` accepts an optional
`Idempotency-Key` header; repeating a request with the same key returns the payment
created the first time, with `200` instead of `201`.

A partial unique index on the column is what actually guarantees it. Two concurrent
requests with the same key cannot both insert, and the loser catches the constraint
violation and returns the winner's payment. The service checks for an existing key first
as a fast path, but that check is a courtesy; the index is the guarantee.

The frontend generates a key per submission and also disables the submit button while a
request is in flight, which covers the double-click case before it reaches the network.

---

## Data

### Status is an enum, stored as text

The brief models `Status` as a `string`. It is a `PaymentStatus` enum in the domain so
that an invalid status cannot be represented and the single legal transition
(`Pending` to `Confirmed`) lives in one place. It is persisted with `HasConversion<string>()`
so the column holds `"Pending"` or `"Confirmed"`, and serialised as a string, so both the
database and the wire contract still match the brief.

Storing text rather than an ordinal also means adding a state later (`Cancelled`,
`Failed`) cannot silently renumber existing rows.

### ConfirmedAt is not in the brief

The brief's model has four fields. `ConfirmedAt` is added because a payment system that
cannot say *when* a state change happened cannot be reconciled or audited. It is null
while the payment is pending.

### DTOs, not entities, on the boundary

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

Backend, 32 tests:

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

Frontend, 15 tests: form validation and trimming, the list's loading, empty, pending and
confirmed states, and the `api` module's `ProblemDetails` handling.

### What is not covered

There are no controller tests and no `WebApplicationFactory` integration tests. The
controller holds no logic beyond mapping outcomes to status codes, and those mappings were
verified by hand against a running instance. That is a real gap: the two bugs below were
both found by running the service, not by the test suite, and integration tests would have
caught the first one.

---

## Two bugs worth recording

Both were found by running the service rather than by the tests, which is itself worth
noting.

### RangeAttribute parses its bounds with the current culture

```csharp
[Range(typeof(decimal), "0.01", "1000000.00")]
```

`RangeAttribute` parses those strings using the current culture. On a machine whose
locale uses `,` as the decimal separator, `decimal.Parse("0.01")` throws, and the request
dies inside model binding with a `500` before reaching any application code. Every create
failed.

The fix is `ParseLimitsInInvariantCulture = true`. The reason this is worth writing down
is that it is invisible on a US-locale CI runner and fails only on a developer machine,
or the reverse.

The unit tests could not catch it, because they call `PaymentService.CreateAsync`
directly and bypass MVC model validation entirely.

### SQLite returns DateTimeKind.Unspecified, so the JSON lost its Z

SQLite stores `DateTime` as TEXT with no time zone. EF Core reads it back as
`DateTimeKind.Unspecified`, and `System.Text.Json` serialises that without a trailing
`Z`:

```
"createdAt":"2026-09-20T20:56:05.8026982"      before
"createdAt":"2026-09-20T21:01:32.923434Z"      after
```

The frontend does `new Date(iso)`, and JavaScript parses a `Z`-less string as local time.
Every timestamp would have displayed shifted by the machine's UTC offset, quietly
corrupting the audit trail that `ConfirmedAt` exists to provide.

Fixed with value converters in `PaymentsDbContext` that stamp `DateTimeKind.Utc` on read,
covered by a regression test.

---

## Framework notes

### EF Core 10 changed the ExecuteUpdateAsync signature

`ExecuteUpdateAsync` now takes a plain delegate rather than an expression tree, so the
chained form that EF Core 9 used no longer expresses the intent:

```csharp
// EF Core 9
.ExecuteUpdateAsync(s => s.SetProperty(...).SetProperty(...), ct);

// EF Core 10
.ExecuteUpdateAsync(setters =>
{
    setters.SetProperty(p => p.Status, PaymentStatus.Confirmed);
    setters.SetProperty(p => p.ConfirmedAt, confirmedAt);
}, ct);
```

### xUnit v3 runs on Microsoft.Testing.Platform

This is a structural change, not a version bump. The test project is a standalone
executable (`<OutputType>Exe</OutputType>`), and the .NET 10 SDK no longer supports
running MTP tests through VSTest. `dotnet test` therefore needs an explicit opt-in, which
lives in `global.json`:

```json
{ "test": { "runner": "Microsoft.Testing.Platform" } }
```

`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio` and `coverlet.collector` are all
VSTest components and are deliberately absent. Test invocations use `--project` rather
than a positional path, which is the MTP-mode form.

xUnit v3 also ships an analyzer (`xUnit1051`) that flags any call accepting a
`CancellationToken` that is not given `TestContext.Current.CancellationToken`. Each test
class exposes a `private static CancellationToken Ct` helper and threads it through, so
the build stays at zero warnings.

---

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
