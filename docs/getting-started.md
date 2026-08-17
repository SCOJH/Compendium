# Getting Started

This page walks you through building a tiny event-sourced service with Compendium: prerequisites, install, define an aggregate, wire DI, dispatch a command, and read a projection. Plan ~10 minutes; longer if it's your first event-sourced .NET app.

If you'd rather read working code, jump straight to [`samples/01-QuickStart-OrderAggregate`](https://github.com/SCOJH/Compendium/tree/main/samples/01-QuickStart-OrderAggregate). Everything below is taken from that sample.

## 1. Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- (Optional) Docker — only needed for the multi-tenant + PostgreSQL sample
- A C# project (`dotnet new console -o MyService`)

> **Note.** Compendium targets `net9.0`. Older runtimes are not supported.

## 2. Install the packages

The smallest useful set is `Compendium.Core` plus `Compendium.Application`. Add adapters as you need them.

```bash
dotnet add package Compendium.Core
dotnet add package Compendium.Abstractions
dotnet add package Compendium.Application

# Optional adapters
dotnet add package Compendium.Adapters.PostgreSQL
dotnet add package Compendium.Multitenancy
dotnet add package Compendium.Adapters.OpenRouter
```

> **Status.** Compendium is at **`v1.0.0-preview.1`**. APIs in `Compendium.Core` and `Compendium.Abstractions.*` are intended to be stable; adapter APIs may evolve.

## 3. Define an aggregate

Aggregates inherit from `AggregateRoot<TId>` (in `Compendium.Core.Domain.Primitives`). Domain events derive from `DomainEventBase` (in `Compendium.Core.Domain.Events`). Both are in zero-dependency `Compendium.Core`.

```csharp
using Compendium.Core.Domain.Events;
using Compendium.Core.Domain.Primitives;
using Compendium.Core.Results;

public sealed class OrderPlaced : DomainEventBase
{
    public OrderPlaced(string orderId, string customerId, decimal totalAmount, long version)
        : base(orderId, nameof(Order), version)
    {
        CustomerId = customerId;
        TotalAmount = totalAmount;
    }

    public string CustomerId { get; }
    public decimal TotalAmount { get; }
}

public sealed class OrderShipped : DomainEventBase
{
    public OrderShipped(string orderId, DateTimeOffset shippedAt, long version)
        : base(orderId, nameof(Order), version) => ShippedAt = shippedAt;

    public DateTimeOffset ShippedAt { get; }
}

public sealed class Order : AggregateRoot<Guid>
{
    private Order(Guid id) : base(id) { }

    public string CustomerId { get; private set; } = "";
    public decimal TotalAmount { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.Pending;

    public static Result<Order> Place(Guid id, string customerId, decimal totalAmount)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            return Result.Failure<Order>(Error.Validation("Order.CustomerId.Empty", "CustomerId required."));
        if (totalAmount <= 0m)
            return Result.Failure<Order>(Error.Validation("Order.TotalAmount.NotPositive", "Total must be > 0."));

        var order = new Order(id) { CustomerId = customerId, TotalAmount = totalAmount, Status = OrderStatus.Placed };
        order.AddDomainEvent(new OrderPlaced(id.ToString(), customerId, totalAmount, order.Version + 1));
        order.IncrementVersion();
        return Result.Success(order);
    }

    public Result Ship()
    {
        if (Status != OrderStatus.Placed)
            return Result.Failure(Error.Conflict("Order.NotPlaced", $"Cannot ship in status {Status}."));

        Status = OrderStatus.Shipped;
        AddDomainEvent(new OrderShipped(Id.ToString(), DateTimeOffset.UtcNow, Version + 1));
        IncrementVersion();
        return Result.Success();
    }
}

public enum OrderStatus { Pending, Placed, Shipped }
```

Two things to notice:

1. The aggregate **never throws** for expected failures — it returns `Result` / `Result<T>`. See [ADR 0001](adr/0001-result-pattern.md).
2. `AddDomainEvent` and `IncrementVersion` are `protected` on `AggregateRoot<TId>`; only the aggregate itself can raise events.

## 4. Define a command and a query

Commands and queries live in `Compendium.Abstractions.CQRS`; handlers live in `Compendium.Abstractions.CQRS.Handlers`.

```csharp
using Compendium.Abstractions.CQRS.Commands;
using Compendium.Abstractions.CQRS.Handlers;
using Compendium.Abstractions.CQRS.Queries;
using Compendium.Core.Results;

public sealed record PlaceOrderCommand(Guid OrderId, string CustomerId, decimal TotalAmount)
    : ICommand<Guid>;

public sealed record GetOrderSummaryQuery(Guid OrderId) : IQuery<OrderSummary>;
public sealed record OrderSummary(Guid OrderId, string CustomerId, decimal TotalAmount, string Status);

public sealed class PlaceOrderHandler(OrderSummaryProjection projection)
    : ICommandHandler<PlaceOrderCommand, Guid>
{
    public Task<Result<Guid>> HandleAsync(PlaceOrderCommand cmd, CancellationToken ct = default)
    {
        var result = Order.Place(cmd.OrderId, cmd.CustomerId, cmd.TotalAmount);
        if (result.IsFailure) return Task.FromResult(Result.Failure<Guid>(result.Error));

        projection.Apply(result.Value!.GetUncommittedEvents());
        return Task.FromResult(Result.Success(result.Value!.Id));
    }
}

public sealed class GetOrderSummaryHandler(OrderSummaryProjection projection)
    : IQueryHandler<GetOrderSummaryQuery, OrderSummary>
{
    public Task<Result<OrderSummary>> HandleAsync(GetOrderSummaryQuery q, CancellationToken ct = default)
    {
        var s = projection.Get(q.OrderId);
        return Task.FromResult(s is null
            ? Result.Failure<OrderSummary>(Error.NotFound("Order.NotFound", $"Order {q.OrderId} not found."))
            : Result.Success(s));
    }
}
```

The `OrderSummaryProjection` is just a `Dictionary<Guid, OrderSummary>` updated from incoming events — see the QuickStart sample for the full implementation.

## 5. Wire DI

Compendium ships dispatcher classes (`CommandDispatcher`, `QueryDispatcher` in `Compendium.Application.CQRS`) — register them and your handlers, and you're done.

```csharp
using Compendium.Abstractions.CQRS.Handlers;
using Compendium.Application.CQRS;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Compendium dispatchers
services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
services.AddSingleton<IQueryDispatcher, QueryDispatcher>();

// Your projection + handlers
services.AddSingleton<OrderSummaryProjection>();
services.AddSingleton<ICommandHandler<PlaceOrderCommand, Guid>, PlaceOrderHandler>();
services.AddSingleton<IQueryHandler<GetOrderSummaryQuery, OrderSummary>, GetOrderSummaryHandler>();

await using var provider = services.BuildServiceProvider();
```

> **Heads-up.** There is no umbrella `AddCompendium(...)` extension yet — register dispatchers and handlers explicitly. Each adapter brings its own `Add*` extension (e.g. `AddPostgreSqlEventStore`, `AddCompendiumMultitenancy`, `AddOpenRouter`).

## 6. Dispatch a command

```csharp
var commands = provider.GetRequiredService<ICommandDispatcher>();

var orderId = Guid.NewGuid();
var result = await commands.DispatchAsync<PlaceOrderCommand, Guid>(
    new PlaceOrderCommand(orderId, CustomerId: "cust-001", TotalAmount: 49.95m));

if (result.IsFailure)
{
    Console.Error.WriteLine($"{result.Error.Code}: {result.Error.Message}");
    return 1;
}

Console.WriteLine($"Placed order {result.Value}");
```

Dispatchers wrap your handler in a pipeline of `IPipelineBehavior<TRequest, TResponse>` (logging, validation, idempotency, transactions). Out of the box you get distributed tracing and metrics via `CompendiumTelemetry`.

## 7. Read a projection

```csharp
var queries = provider.GetRequiredService<IQueryDispatcher>();

var summary = await queries.DispatchAsync<GetOrderSummaryQuery, OrderSummary>(
    new GetOrderSummaryQuery(orderId));

Console.WriteLine(summary.Value);
// → OrderSummary { OrderId = ..., CustomerId = cust-001, TotalAmount = 49.95, Status = Placed }
```

## Next steps

- **[Concepts](concepts/event-sourcing.md)** — the *why* behind aggregates, projections, and the result pattern.
- **[Adapters](adapters/postgresql.md)** — wire a real event store, multi-tenancy, AI provider, billing, or auth.
- **[Samples](https://github.com/SCOJH/Compendium/tree/main/samples)** — three runnable projects:
  - `01-QuickStart-OrderAggregate` — the code on this page, in a single file you can `dotnet run`.
  - `02-MultiTenant-WithPostgres` — same model against a real Postgres event store, scoped per tenant.
  - `03-AI-WithOpenRouter` — Compendium's provider-agnostic `IAIProvider` against OpenRouter (with offline fallback).
- **[Architecture decisions](adr/README.md)** — the trade-offs we made, and didn't make, in writing this framework.
