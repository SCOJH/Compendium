# Hexagonal Architecture

Compendium is built around a strict hexagonal (also known as *ports and adapters*) architecture. The domain code does not know that PostgreSQL, Stripe, or ASP.NET Core exist — it only knows about *interfaces it needs*. Concrete integrations plug in from the outside.

## Why hexagonal?

The first reason is **testability**: a Core that depends on nothing external is trivially unit-testable. No database fixtures, no HTTP mocks, no dependency injection containers needed for a domain test.

The second reason is **swap-ability**: when you discover that your billing provider needs to change, or that you need a Redis-backed projection store instead of in-memory, the domain stays untouched. You write a new adapter and re-wire DI.

The third reason is **clarity of dependencies**: when an arrow points the wrong way (e.g. domain code references `Microsoft.Data.SqlClient`), the linter / project reference setup tells you immediately.

For why we picked strict hexagonal over related styles (Onion, Clean), see [ADR 0002 — Hexagonal architecture](../adr/0002-hexagonal-architecture.md). For why `Compendium.Core` has zero NuGet dependencies, see [ADR 0003 — Zero-dependency Core](../adr/0003-zero-dep-core.md).

## The layers

```
                ┌──────────────────────────────────────────────┐
                │             Adapters (outermost)             │
                │  Compendium.Adapters.PostgreSQL, .Stripe,    │
                │  .Redis, .Zitadel, .AspNetCore, .Listmonk... │
                └────────────────────┬─────────────────────────┘
                                     │ implement
                                     ▼
                ┌──────────────────────────────────────────────┐
                │            Abstractions (ports)              │
                │  IBillingService, IIdentityProvider,         │
                │  IEventStore, IEmailSender, IAIProvider...   │
                └────────────────────┬─────────────────────────┘
                                     │ used by
                                     ▼
                ┌──────────────────────────────────────────────┐
                │       Application (orchestration)            │
                │  Command/Query handlers, dispatchers,        │
                │  policies, pipeline behaviors                │
                └────────────────────┬─────────────────────────┘
                                     │ operates on
                                     ▼
                ┌──────────────────────────────────────────────┐
                │           Core (innermost, zero-dep)         │
                │  AggregateRoot, ValueObject, Result,         │
                │  Error, IDomainEvent — pure DDD primitives   │
                └──────────────────────────────────────────────┘
```

Dependencies only point **inward**. `Core` knows about nothing else; `Application` references `Core` and `Abstractions`; adapters reference `Abstractions` (and possibly `Core` for entity types) but never the other way around.

## What lives where

### `Compendium.Core` — Pure domain primitives

Zero NuGet dependencies. Only the .NET BCL.

Examples:

- [`AggregateRoot<TId>`](https://github.com/SCOJH/Compendium/blob/ca25347/src/Core/Compendium.Core/Domain/Primitives/AggregateRoot.cs) — write-side base class
- [`ValueObject`](https://github.com/SCOJH/Compendium/blob/ca25347/src/Core/Compendium.Core/Domain/Primitives/ValueObject.cs) — equality by component
- [`Result<T>`](https://github.com/SCOJH/Compendium/blob/ca25347/src/Core/Compendium.Core/Results/Result.cs) — typed success/failure
- [`IDomainEvent`](https://github.com/SCOJH/Compendium/blob/ca25347/src/Core/Compendium.Core/Domain/Events/IDomainEvent.cs) — event contract

### `Compendium.Abstractions.*` — Ports

Interfaces that the domain needs to do its job, but does not implement itself.

- `Compendium.Abstractions` — generic infrastructure ports (event store, projection store, etc.)
- `Compendium.Abstractions.Identity` — identity-provider contracts
- `Compendium.Abstractions.Billing` — billing-provider contracts
- `Compendium.Abstractions.Email` — email-provider contracts
- `Compendium.Abstractions.AI` — AI-provider contracts

Adapters implement these. Application code consumes them. The domain typically does not (the domain talks to *the application*, not to ports directly — that boundary keeps the Core pure).

### `Compendium.Application` — Orchestration

CQRS dispatchers, command/query handlers, pipeline behaviors. Knows about Core and Abstractions, never about a specific adapter.

### `Compendium.Adapters.*` — Adapters

Concrete integrations. Each adapter is an opt-in NuGet package: pick `PostgreSQL` if you want the Postgres event store; pick `Stripe` if you bill through Stripe; pick `Zitadel` for OIDC. See [Adapters](../adapters/aspnetcore.md) for the per-adapter how-tos.

## A worked example

Suppose you have a billing flow. The Application layer code looks roughly like:

```csharp
public sealed class PlaceOrderHandler(
    IBillingService billing,        // ← port from Abstractions.Billing
    IEventStore eventStore)         // ← port from Abstractions
    : ICommandHandler<PlaceOrderCommand>
{
    public async Task<Result> Handle(PlaceOrderCommand cmd, CancellationToken ct)
    {
        var customerResult = await billing.EnsureCustomerAsync(cmd.Email, ct);
        if (customerResult.IsFailure) return customerResult.Error;

        // ... business logic on aggregate ...
        // ... persist domain events via eventStore ...
    }
}
```

Notice what the handler does *not* know: that `billing` is implemented by `StripeBillingService` (or `LemonSqueezyBillingService`), or that `eventStore` is backed by Postgres. Those are wiring decisions made in `Program.cs`. Replacing Stripe with LemonSqueezy is a one-line DI change — the handler does not move.

## Where to go next

- [Result Pattern](result-pattern.md) — the return type of all those handlers
- [Event Sourcing](event-sourcing.md) — what `IEventStore` actually persists
- [Multi-tenancy](multi-tenancy.md) — how tenant scope crosses all layers
- [ADR 0002](../adr/0002-hexagonal-architecture.md) and [ADR 0003](../adr/0003-zero-dep-core.md) — the original decisions
