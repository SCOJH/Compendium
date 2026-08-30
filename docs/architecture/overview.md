# Architecture overview

> One page: the problem Compendium solves, the principle that shapes it, and where its
> boundaries run. The diagrams live in the [C4 views](c4-level1-context.md); the
> reasoning behind each structural choice lives in the [ADR index](../adr/README.md).

## The problem

Adopting CQRS and event sourcing on .NET usually means marrying an infrastructure.
The mainstream paths couple your domain model to an event store product, a message
bus SDK, or a mediator library before you have written a single business rule — and
every one of those dependencies propagates to every consumer of your domain
assemblies. Un-marrying later is a rewrite. Multi-tenancy, the other constant of
SaaS, is usually bolted on afterwards, one query filter at a time.

Compendium exists so that a team can build an event-sourced, multi-tenant SaaS
product **without that marriage**: aggregates, events, command handling, projections
and tenant scoping are expressed against BCL-only primitives and narrow ports.
Everything vendor-specific is an adapter package that the application chooses — and
can replace — at composition time.

## The central principle

**`Compendium.Core` has zero NuGet dependencies** ([ADR-0003](../adr/0003-zero-dep-core.md)):
domain primitives (`AggregateRoot<TId>`, `ValueObject`, `Entity`), the
`Result<T>`/`Error` types ([ADR-0001](../adr/0001-result-pattern.md)), domain events,
and the event-serialization contracts compile against the .NET BCL and nothing else.

Everything else is arranged around that core in strict hexagonal rings
([ADR-0002](../adr/0002-hexagonal-architecture.md)), with dependencies pointing
inward only:

- **Abstractions** — ports as interfaces, one NuGet package per concern
  (event store, AI, billing, geo, …), so a consumer references only the ports it uses.
- **Application** — CQRS dispatchers, pipeline behaviors, saga orchestration.
- **Infrastructure** — generic building blocks plus complete in-memory
  implementations of every port, which double as the framework's semantic reference
  ([ADR-0007](../adr/0007-integration-test-split.md)).
- **Multitenancy** — ambient, request-scoped tenant context with multi-source
  resolution that fails closed on disagreement ([ADR-0004](../adr/0004-multi-tenancy-strategy.md)).
- **Adapters** — vendor integrations. The heavy ones live in their own repositories
  with their own release cadence ([ADR-0006](../adr/0006-multi-repo-adapter-split.md));
  this repo keeps only thin glue (ASP.NET Core) and incubating adapters.

The write side is append-only event sourcing ([ADR-0005](../adr/0005-event-sourcing-vs-state.md));
read models are replayable projections; every fallible operation returns `Result<T>`.

## System boundaries

**Inside this repository**: the 35 framework packages — Core, the base plus
satellite abstraction packages, Application, Infrastructure, Multitenancy, Testing,
and six thin or incubating adapters. Each ships on its own release train (26 trains,
partitioned mechanically by the `ProjectReference` graph — see
[release trains](../operations/release-trains.md)).

**Outside, by design**:

- *Vendor adapters* (PostgreSQL event store, Redis, Zitadel, Stripe, the AI
  providers, …) live in separate repositories and only depend on the published
  abstraction packages.
- *The host application* owns its aggregates, command/query handlers, projection
  handlers — and, today, its aggregate rehydration logic and most of the DI wiring:
  there is no umbrella `AddCompendium()`.
- *Deliberate refusals* ([ROADMAP](../../ROADMAP.md)): no message-broker
  abstraction, no built-in UI, no ORM ambitions, no legacy .NET targets.

## What holds the shape

- **36 architecture tests** (NetArchTest) pin the dependency direction, event
  immutability, and CQRS/naming conventions.
- **Release trains + publication gates**: a tag releases exactly one train; every
  published package must come from a tagged, test-gated commit, and republishing an
  existing version is a hard failure.
- **In-memory implementations as contract**: any semantic gap between an in-memory
  implementation and a persistent adapter is treated as a framework bug, which keeps
  the whole framework-behaviour test suite Docker-free.
- **A 90 % line-coverage gate** on the unit-testable surface.

## Known limits — stated, not hidden

These are real gaps in the code as it stands; the C4 component view marks where each
one sits.

- **Aggregate rehydration is consumer-supplied.** `EventSourcedRepository` leaves
  `BuildAggregateFromEvents`/`ApplyEventsToAggregate` abstract; no convention-based
  `Apply` dispatch exists, although the README's quick-start implies one.
- **No outbox in this repository.** ADR-0005 names the outbox pattern, but the
  implementation belongs to persistent adapters that live elsewhere; the in-repo
  chain stops at the `IIntegrationEventPublisher` port, and nothing bridges domain
  events to integration events automatically.
- **Two projection subsystems coexist** in `Compendium.Infrastructure` (the legacy
  `ProjectionManager` and the streaming `EnhancedProjectionManager`/
  `LiveProjectionProcessor`), with different checkpoint models and no deprecation
  marking on the older one.
- **The query pipeline runs no behaviors** — validation, logging and idempotency
  behaviors apply to commands only.
- **Architecture tests cover 4 of 35 assemblies**, and the Core zero-dependency rule
  is guaranteed by the `.csproj` itself, not by a test.
- **Tenant scoping in infrastructure is opt-in**: stores take an optional
  `ITenantContext` — a container wired without it gets an event store with no tenant
  isolation, silently.
- **Compile-time event registration is an aspiration**: the
  `IGeneratedEventRegistry`/`[AutoRegisterEvent]` API shipped, but no source
  generator consumes it; event-type registration is manual and a missed type fails
  at read time (loudly, by design).

## Going deeper

- [C4 level 1 — system context](c4-level1-context.md)
- [C4 level 2 — containers (packages)](c4-level2-containers.md)
- [C4 level 3 — components of the core flow](c4-level3-components.md)
- [Architecture decision records](../adr/README.md)
