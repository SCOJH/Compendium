# 0011. Whitelist deserialization and read-time upcasting

* Status: Accepted
* Date: 2026-08-30 (recorded retroactively — the mechanism has been in `Compendium.Core.EventSourcing` since the event-sourcing surface was built; hardened alongside [ADR-0010](0010-logical-event-identity.md))
* Deciders: @sassy-solutions/maintainers

## Context

Reading an event stream means deserializing payloads whose **type name comes from a
database column**. Done naively, that is the classic .NET deserialization attack
surface: any mechanism that resolves an arbitrary type name from the data
(`TypeNameHandling`, polymorphic `$type` markers, `Type.GetType` on the column
value) lets whoever writes the column pick the type that gets constructed.

The second problem is schema evolution under an append-only contract: ADR-0005
commits to *never editing historical events*, so a V1 event written three years ago
must still become useful state today, next to V3 events written this morning.

Both problems sit in `Compendium.Core`, which has zero NuGet dependencies
([ADR-0003](0003-zero-dep-core.md)) — so whatever the answer is, it must be
expressible with the BCL alone.

## Decision

**Deserialization is whitelist-only, and the type never comes from the payload:**

- `SecureEventDeserializer` resolves the stored name through
  `IEventTypeRegistry.GetWhitelistedType(name)` — a frozen dictionary of explicitly
  registered domain event types. An unknown name is
  `Result.Failure("EventDeserializer.TypeNotWhitelisted")` and a logged security
  violation, not a lookup somewhere else.
- Serialization is **`System.Text.Json` exclusively** — it is BCL, so Core stays
  zero-dependency. There is no Newtonsoft.Json anywhere in the repository, no
  polymorphic `$type` handling, no custom `TypeInfoResolver`: the arbitrary-type
  vector is structurally absent, not configured away.
- On write, the runtime type is passed explicitly
  (`JsonSerializer.Serialize(evt, evt.GetType(), …)`) so the payload carries the
  concrete event's members, while the *identity* travels out-of-band in the stored
  type-name column ([ADR-0010](0010-logical-event-identity.md)).

**Schema evolution happens at read time, through chained upcasters:**

- Each event version is a distinct CLR type. An `IEventUpcaster<TSource, TTarget>`
  (usually via `EventUpcasterBase<TSource, TTarget>`) converts one version to the
  next; `EventVersionMigrator` chains them V1 → V2 → V3 by re-keying on the
  *produced* instance's runtime type and `EventVersion`, with a hard cap of 100
  steps as a circular-chain guard.
- Migration runs inside the deserializer, after the whitelist check. **The log is
  never rewritten** — an old row stays exactly as written, forever, and pays its
  migration on every read.

## Consequences

### Positive

- The deserialization attack surface is the registry's contents — a reviewable,
  explicit list — rather than "every type loadable in the process".
- Handlers and projections only ever see the **latest** event shape; version
  history is invisible above the deserializer.
- Append-only stays true: no backfill jobs, no dual-write windows, no "migration
  weekend". Adding V2 is: new type, one upcaster, register both.
- Zero serializer dependencies in Core, per ADR-0003.

### Negative / Trade-offs

- **Read-time cost is permanent**: a V1 row pays its upcasting chain on every
  replay until the aggregate is snapshotted past it. Accepted — replay-heavy paths
  have snapshots; correctness beats replay speed here.
- **A missing upcaster is not detected.** The migrator's chain simply stops and
  returns the event unchanged as a success — a V1 event with no registered chain
  silently poses as current. The XML documentation still promises detection; this
  is a real doc/code divergence and known debt.
- **Two adjacent registration policies disagree**: registering a duplicate event
  *type name* is refused ([ADR-0010](0010-logical-event-identity.md)); registering
  a duplicate *upcaster* replaces with a warning. Inconsistent by accident, not by
  argument.
- **The generic `DeserializeEvent<T>(string)` overload bypasses both mechanisms**
  — it checks `typeof(T).AssemblyQualifiedName` directly and never migrates. Two
  surfaces of the same class with different guarantees.
- **The in-memory stores never exercise this path**: they keep live `IDomainEvent`
  instances alongside the JSON, so the whitelist and migrator are only truly
  exercised by persistent adapters — which live in other repositories
  ([ADR-0006](0006-multi-repo-adapter-split.md)). The framework's own test suite
  covers the deserializer directly, but not through a store.

## Alternatives considered

- **Newtonsoft.Json with `TypeNameHandling.None` + custom binder.** Rejected —
  brings a dependency into the graph (against ADR-0003) to configure *away* a
  danger that System.Text.Json simply does not have.
- **System.Text.Json polymorphism (`[JsonDerivedType]`).** Rejected — puts the
  type discriminator *inside* the payload and couples the event hierarchy to
  serializer attributes; the store's type-name column plus registry does the same
  job with the identity kept out-of-band.
- **Weak schema / tolerant reader** (one event type per name forever, tolerate
  unknown fields, no versioned types). Rejected as the *only* mechanism — additive
  changes are indeed handled by tolerant reading (unknown JSON members are ignored,
  missing ones take defaults), but breaking shape changes need an explicit,
  testable transformation, which is what upcasters are.
- **Upcast-on-write / log rewriting migrations.** Rejected — violates the
  append-only contract of ADR-0005 and turns every schema change into a data
  migration with a failure blast radius.
