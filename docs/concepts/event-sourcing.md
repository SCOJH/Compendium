# Event Sourcing

Compendium models domain state as an **append-only log of events** rather than a mutable row in a database. Every change is recorded as a fact that happened, never lost, never overwritten.

## Why event sourcing?

Most CRUD systems store the *current state* and lose the history of how that state came to be. Event sourcing flips that: state is *derived* from a sequence of events, and the events themselves are the source of truth.

The trade-off is real — there is more upfront machinery (events, aggregates, projections) — but it pays back in:

- **Audit by construction**: you get a tamper-evident timeline for free, which is gold for SaaS, billing, identity, and anywhere compliance matters.
- **Time travel**: replay the log up to any point to debug, reproduce a customer issue, or back-test a new business rule.
- **Multiple read models**: the same events can feed many projections (search index, dashboard, ML feature store) without polluting the write model.
- **Better domain modeling**: events name what *happened in the business* (e.g. `OrderPlaced`, `LicenseRevoked`) rather than what was set in a column.

For when *not* to use it, see [ADR 0005 — Event sourcing over state-stored](../adr/0005-event-sourcing-vs-state.md).

## The shape of an event-sourced system

```
                ┌──────────────┐
   Command ───▶ │  Aggregate   │ ──▶ produces ──▶ IDomainEvent[]
                │ (write model)│
                └──────┬───────┘
                       │ persisted to
                       ▼
                ┌──────────────┐
                │  Event Store │ (append-only)
                └──────┬───────┘
                       │ replayed by
            ┌──────────┴──────────┐
            ▼                     ▼
     ┌─────────────┐       ┌─────────────┐
     │ Projection A│       │ Projection B│  ← read models
     └─────────────┘       └─────────────┘
```

The aggregate decides *whether* an action is allowed and emits the events describing what happened. The event store guarantees durable, ordered persistence. Projections rebuild the read shapes the rest of the application needs.

## The primitives Compendium gives you

### `IDomainEvent`

A small, mandatory contract for everything written to the log. From [`src/Core/Compendium.Core/Domain/Events/IDomainEvent.cs`](https://github.com/SCOJH/Compendium/blob/ca25347/src/Core/Compendium.Core/Domain/Events/IDomainEvent.cs):

```csharp
public interface IDomainEvent
{
    Guid EventId { get; }
    string AggregateId { get; }
    string AggregateType { get; }
    DateTimeOffset OccurredOn { get; }
    long AggregateVersion { get; }
    int EventVersion { get; }   // schema version — see "Versioning"
}
```

Every event carries enough metadata to be replayed deterministically and to resolve concurrency conflicts via `AggregateVersion`.

### `AggregateRoot<TId>`

The write model base class. From [`src/Core/Compendium.Core/Domain/Primitives/AggregateRoot.cs`](https://github.com/SCOJH/Compendium/blob/ca25347/src/Core/Compendium.Core/Domain/Primitives/AggregateRoot.cs):

```csharp
public abstract class AggregateRoot<TId> : Entity<TId>, IDisposable
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public long Version { get; private set; }
    public IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    // Aggregates raise events through AddDomainEvent / RaiseEvent on subclasses.
}
```

Aggregates raise events when business rules are satisfied and never write to a database directly. The infrastructure layer is responsible for persisting `DomainEvents` after a successful command.

### Projections

Projections are the read side. They consume events from the store and write to whatever shape your queries need (a SQL table, a Redis key, an in-memory dictionary). Compendium ships projection scaffolding under `Compendium.Infrastructure.Projections` — see [`src/Infrastructure/Compendium.Infrastructure/Projections/README.md`](https://github.com/SCOJH/Compendium/blob/ca25347/src/Infrastructure/Compendium.Infrastructure/Projections/README.md) for details.

A projection should be **idempotent**: replaying the same event twice must not corrupt the read model. This is what makes safe rebuilds, retries, and disaster recovery possible.

## Versioning

Real systems evolve. The `EventVersion` field on `IDomainEvent` lets you ship breaking event-schema changes without rewriting history:

- New events are written at the current version.
- Old events stay on disk at their original version.
- `IEventUpcaster` implementations transform old versions into the latest shape *at read time*.

See [`src/Core/Compendium.Core/EventSourcing/`](https://github.com/SCOJH/Compendium/tree/ca25347/src/Core/Compendium.Core/EventSourcing) for the upcaster contracts.

## Snapshots

For aggregates with very long histories, replaying every event on every load is wasteful. Compendium supports periodic snapshots — capture the materialized aggregate state at version *N*, and only replay events after *N* on subsequent loads. This is opt-in: most aggregates do not need it.

## Where to go next

- [Result Pattern](result-pattern.md) — how Compendium reports success/failure without exceptions
- [Hexagonal Architecture](hexagonal-architecture.md) — how aggregates stay decoupled from infrastructure
- [Multi-tenancy](multi-tenancy.md) — how events stay scoped to the right tenant
- [ADR 0005 — Event sourcing over state-stored](../adr/0005-event-sourcing-vs-state.md) — the decision and trade-offs
- [`samples/01-QuickStart-OrderAggregate`](https://github.com/SCOJH/Compendium/tree/main/samples/01-QuickStart-OrderAggregate) — a runnable in-memory example
