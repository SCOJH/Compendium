# Result Pattern

Compendium does not throw exceptions for control flow. Operations that can fail return a `Result<T>` (or `Result` for void) carrying a typed `Error`. Exceptions are reserved for situations that are genuinely exceptional — bugs, infrastructure crashes, things you do not expect to recover from.

## Why a Result type?

Three reasons:

1. **Exceptions are invisible at the call site**. `await SaveOrder(...)` looks identical whether it can fail with `OrderNotFoundException`, `OptimisticConcurrencyException`, or nothing at all. `Result<T>` makes the *possibility of failure* part of the type, which the compiler can enforce.

2. **Errors carry structure, not just text**. A `404 NotFound` is not the same as a `409 Conflict` is not the same as a `validation failure`. Compendium's `Error` records that distinction explicitly so adapters (e.g. ASP.NET Core problem-details middleware) can map it to the right HTTP status without parsing strings.

3. **Performance**. Throwing exceptions is expensive on .NET — stack capture, type lookup, finally blocks. For hot paths (event replay, command dispatching), explicit returns are dramatically cheaper.

The trade-off is verbosity: every fallible call now has an explicit branch. We accept that. For the long discussion, see [ADR 0001 — Result pattern over exceptions](../adr/0001-result-pattern.md).

## The shape

From [`src/Core/Compendium.Core/Results/Result.cs`](https://github.com/SCOJH/Compendium/blob/ca25347/src/Core/Compendium.Core/Results/Result.cs):

```csharp
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }   // Error.None on success

    public static Result Success();
    public static Result<T> Success<T>(T value);
    public static Result Failure(Error error);
    // ...
}
```

`Result<T>` adds `Value` (only safe to read when `IsSuccess`).

## Errors are values

From [`src/Core/Compendium.Core/Results/Error.cs`](https://github.com/SCOJH/Compendium/blob/ca25347/src/Core/Compendium.Core/Results/Error.cs):

```csharp
public sealed class Error : ValueObject
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.None);

    public string Code { get; }       // e.g. "Order.NotFound"
    public string Message { get; }    // human-readable
    public ErrorType Type { get; }    // NotFound | Validation | Conflict | Failure | ...
    public IReadOnlyDictionary<string, object> Metadata { get; }
}
```

`ErrorType` lets generic infrastructure (HTTP layer, logging, retries) make decisions without knowing the specific business domain.

## Typical usage

```csharp
public async Task<Result<Order>> GetOrderAsync(OrderId id, CancellationToken ct)
{
    var order = await _repository.FindAsync(id, ct);
    if (order is null)
        return Error.NotFound("Order.NotFound", $"Order {id} not found");

    return order;   // implicit conversion to Result<Order>.Success(order)
}
```

Composing several fallible calls without nested ifs:

```csharp
var customerResult = await EnsureCustomer(email, ct);
if (customerResult.IsFailure) return customerResult.Error;

var orderResult = await PlaceOrder(customerResult.Value, items, ct);
if (orderResult.IsFailure) return orderResult.Error;

return Result.Success();
```

For richer composition (`Map`, `Bind`, `Tap`, etc.) see [`Result.Extensions.cs`](https://github.com/SCOJH/Compendium/blob/ca25347/src/Core/Compendium.Core/Results/) — but use them sparingly. Loud explicit branching is usually clearer than a chain of monadic operators.

## Anti-patterns

A few mistakes to avoid:

- **Wrapping arbitrary exceptions in `Result.Failure`**. If an exception is genuinely unexpected (out of disk, null reference in your code), let it bubble. Catching everything to "return a clean Result" hides bugs.
- **Reading `result.Value` without checking `IsSuccess`**. `Result<T>.Value` on a failed result throws. Always guard.
- **Returning `Result<bool>`**. If the answer is just "did this succeed?", use `Result` (no value). `Result<bool>.Success(false)` is almost never what you want — that is a *successful* operation that returned false, which is rarely the correct semantic.
- **Generic `Error.Failure("Something went wrong")`**. Pick a specific code. Future-you, or your HTTP layer, will need it.

## Where to go next

- [Hexagonal Architecture](hexagonal-architecture.md) — Result types flow through every layer
- [Event Sourcing](event-sourcing.md) — aggregates return `Result<T>` when business rules reject a command
- [ADR 0001](../adr/0001-result-pattern.md) — the rationale and alternatives considered
