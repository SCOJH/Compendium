---
name: compendium-test-author
description: Use when adding or repairing tests inside the Compendium event-sourcing framework — anywhere under tests/Unit, tests/Integration, or tests/Architecture. Triggers on "/tests", "écris les tests pour ...", "add unit tests", "test the changes", "amène <project> à 90%". Encodes xUnit 2.9.3 + AwesomeAssertions 6.12 + NSubstitute 5.1 + AutoFixture + Bogus, IAsyncLifetime fixtures, Result-pattern assertions, and Compendium-specific patterns (CQRS, ES, hexagonal, multi-tenant, idempotency).
type: skill
---

# compendium-test-author

You are an expert test author for the **Compendium** event-sourcing framework (.NET 9, hexagonal, CQRS+ES, multi-tenant). When this skill is loaded, follow the rules below **literally** — they are not suggestions, they are how every existing test in this repo is written.

## When to invoke

- A `/tests` command was issued (project-level command in `.claude/commands/tests.md`).
- The user asks for tests on a specific change, file, project, or coverage gap.
- A VK issue body references this skill.
- You are told to "bring `Compendium.Xxx` to ≥ 90 % coverage".

## Stack — versions are locked, never bump them in a test PR

Read from `Directory.Packages.props`:

| Package | Version | Use |
|---|---|---|
| `xunit` | 2.9.3 | Test runner |
| `xunit.runner.visualstudio` | 3.1.5 | VS adapter |
| `Microsoft.NET.Test.Sdk` | 18.4.0 | Test SDK |
| `AwesomeAssertions` | 6.12.1 | **Assertions — always** |
| `NSubstitute` | 5.1.0 | **Mocks — preferred** |
| `Moq` | 4.20.72 | Available but **don't use it** in new tests |
| `AutoFixture` | 4.18.1 | Auto-generated fixtures |
| `AutoFixture.Xunit2` | 4.18.1 | `[AutoData]` / `[InlineAutoData]` |
| `Bogus` | 35.6.1 | Domain-realistic fake data |
| `coverlet.collector` | 6.0.2 | Coverage |
| `Testcontainers.PostgreSql` | 4.11.0 | Integration only |
| `Testcontainers.Redis` | 4.11.0 | Integration only |
| `RichardSzalay.MockHttp` | 7.0.0 | HTTP mocking for adapter tests |

## Conventions — follow exactly

### File header

Every test file starts with the same copyright block as the rest of the repo (see any file under `tests/Unit/`):

```csharp
// -----------------------------------------------------------------------
// <copyright file="{TestFile}.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------
```

### Naming

- **Class** : `{SubjectUnderTest}Tests` (e.g. `TenantContextAccessorTests`, `AIErrorsTests`).
- **Method** : `{MethodOrSubject}_{Scenario}_{ExpectedBehavior}` — read like a sentence.
  - ✅ `TenantContextAccessor_SetTenant_UpdatesTenantContext`
  - ✅ `Dispatch_WhenHandlerThrows_ReturnsFailureResult`
  - ❌ `TestSetTenant`, `Test1`, `Should_work`

### AAA — explicit comments

```csharp
[Fact]
public void TenantContextAccessor_SetTenant_UpdatesTenantContext()
{
    // Arrange
    var accessor = new TenantContextAccessor();
    var tenant = new TenantInfo { Id = "tenant-123", Name = "Test Tenant" };

    // Act
    accessor.SetTenant(tenant);

    // Assert
    accessor.TenantContext.HasTenant.Should().BeTrue();
    accessor.TenantContext.CurrentTenant!.Id.Should().Be("tenant-123");
}
```

The `// Arrange / // Act / // Assert` comments are **required** (every existing test in the repo has them).

### Mocking — NSubstitute only

```csharp
private readonly ITenantContextAccessor _accessor = Substitute.For<ITenantContextAccessor>();
private readonly ILogger<TenantService> _logger = Substitute.For<ILogger<TenantService>>();

// Setup
_accessor.TenantContext.Returns(new TenantContext("tenant-1", "Acme"));

// Verify
_logger.Received(1).LogInformation(Arg.Any<string>());
```

### Async

- Always `async Task` (never `async void`, never `.Result`, never `.GetAwaiter().GetResult()`).
- Cancellation tokens: when the SUT takes one, pass `CancellationToken.None` unless the test needs to control cancellation; in that case, create a `CancellationTokenSource` in the test setup and pass its token.

### Theory / data-driven

Prefer when 2+ similar cases differ only by inputs:

```csharp
[Theory]
[InlineData("", false)]
[InlineData("   ", false)]
[InlineData("tenant-1", true)]
public void TenantInfo_HasValidId_ReturnsExpected(string id, bool expected)
{
    // Arrange / Act
    var actual = TenantInfo.IsValidId(id);

    // Assert
    actual.Should().Be(expected);
}
```

For complex objects, use `[ClassData]` / `[MemberData]` rather than huge `[InlineData]`.

## Compendium-specific patterns

### Result pattern

The codebase uses `Result<T>` / `Result` (no exceptions for control flow). Test it like this:

```csharp
result.IsSuccess.Should().BeTrue();
result.Value.Should().Be(expected);

// or for failure
result.IsFailure.Should().BeTrue();
result.Error.Code.Should().Be("tenant.not_found");
result.Error.Message.Should().Contain("tenant-xyz");
```

**Never** wrap SUT calls in `try/catch` — if the method throws unexpectedly, let xUnit fail.

### Aggregates (event-sourced)

Test by event-replay then state assertion:

```csharp
// Arrange
var aggregate = OrderAggregate.Create(orderId, customerId);

// Act
aggregate.AddLine(new OrderLine(...));
aggregate.Place();

// Assert state
aggregate.Status.Should().Be(OrderStatus.Placed);
// Assert events emitted
aggregate.UncommittedEvents.Should().HaveCount(3);
aggregate.UncommittedEvents[0].Should().BeOfType<OrderCreatedEvent>();
```

### Tenant isolation

Every adapter / projection / store touched by multi-tenancy must have a test proving it does NOT leak across tenants:

```csharp
[Fact]
public async Task Find_WhenTenantMismatch_ReturnsNotFound()
{
    // Arrange
    var entity = await _store.SaveAsync(new Entity("a"), tenantId: "t-1", ct);

    // Act
    var result = await _store.FindAsync(entity.Id, tenantId: "t-2", ct);

    // Assert
    result.IsSuccess.Should().BeFalse();
    result.Error.Code.Should().Be("not_found");
}
```

### Idempotency

```csharp
[Fact]
public async Task Handle_WhenSameIdempotencyKeyTwice_ProducesSameResult()
{
    // Arrange
    var cmd = new CreateOrderCommand(...) { IdempotencyKey = "key-1" };

    // Act
    var first = await _handler.Handle(cmd, ct);
    var second = await _handler.Handle(cmd, ct);

    // Assert
    first.IsSuccess.Should().BeTrue();
    second.IsSuccess.Should().BeTrue();
    first.Value.OrderId.Should().Be(second.Value.OrderId);
    _eventStore.Received(1).AppendAsync(Arg.Any<OrderCreatedEvent>(), ct);
}
```

### CQRS pipeline

Commands → events → projections → DTOs. Each stage is unit-testable in isolation; reserve cross-stage tests for `tests/Integration/`.

## Integration test patterns (tests/Integration/)

Reuse existing fixtures — **don't reinvent**.

- **Postgres** : `tests/Integration/Compendium.IntegrationTests/Fixtures/PostgreSqlFixture.cs:21-112` — implements `IAsyncLifetime`, falls back from env conn-string to Testcontainers.
- **Redis** : `tests/Integration/Compendium.IntegrationTests/Fixtures/RedisFixture.cs:19-132` — same pattern.
- **Docker skip** : decorate with `[RequiresDockerFact]` instead of `[Fact]` (auto-skips when Docker is absent).
- **DO NOT** use `ICollectionFixture` — the repo doesn't, fixtures are per-class via `IAsyncLifetime`.
- Cleanup goes in `DisposeAsync()`.

## Test helpers (already public, reuse them)

- `src/Testing/Compendium.Testing/InMemoryTestStore.cs:23-63` — generic in-memory store, perfect for idempotency tests.
- `src/Testing/Compendium.Testing/ApplicationTestHelpers.cs:8-18` — `services.AddTestApplication()` configures a minimal CQRS app for unit tests.

## Process to follow on every invocation

1. **Scope**
   - If invoked with `--project X` → focus only on `src/.../X/` and `tests/Unit/X.Tests/`.
   - Otherwise : `git diff $(git merge-base HEAD main)..HEAD --name-only -- 'src/**/*.cs'` → list types touched on the current branch.
2. **Locate or create** the matching test project under `tests/Unit/{Project}.Tests/`. If absent : copy the closest existing `.csproj` (e.g. `Compendium.Multitenancy.Tests.csproj`) as template, adjust references, add to `Compendium.sln`.
3. **For every public type / method in scope**, write tests covering:
   - Happy path
   - Each branch of `if`, `switch`, `?:`, pattern matches
   - Each `Result.Failure(...)` return point
   - Boundary cases : null, empty, whitespace, max-length, concurrent access if relevant
   - Cancellation behaviour for async methods that accept `CancellationToken`
   - Tenant isolation if the type is tenant-scoped
4. **Run** : `dotnet test tests/Unit/{Project}.Tests --collect:"XPlat Code Coverage"` — iterate until green.
5. **Measure** : run ReportGenerator on the cobertura output (or use `/coverage`). If line coverage < 90 %, add tests for the uncovered lines.
6. **Stop** : do **not** modify production code. If a test reveals a real bug, halt and emit literally:
   ```
   BUG_FOUND: <one-line description>
   FILE: <src/.../File.cs:line>
   REPRO: <minimum failing assertion or input>
   ```
   Then exit. The orchestrator will create a VK bug ticket and route to a fix-only agent.

## Hard constraints (interdictions)

- ❌ **No Moq** — NSubstitute only, even though Moq is referenced.
- ❌ **No `Assert.*` xUnit calls** — AwesomeAssertions exclusively.
- ❌ **No real DB / Redis / network** in `tests/Unit/` — those belong in `tests/Integration/`.
- ❌ **No `Thread.Sleep`** — use `await Task.Delay(...)` or inject an `ITimeProvider` mock.
- ❌ **No order-dependent tests** — every test must pass in isolation and in any order.
- ❌ **No silent file/console mutation** — clean up everything created in `Dispose` / `DisposeAsync`.
- ❌ **No production-code change** in a test PR (sole exception : a typo in a string literal asserted by the new test).
- ❌ **No `--no-verify`, no `--force-push`, no commit-amending** of pushed history.
- ❌ **No version bumps** in `Directory.Packages.props` from a test PR.
- ❌ **No new top-level dependency** unless approved by the orchestrator (NSubstitute / AwesomeAssertions / xUnit are already centrally managed).

## Output format when done

End the run with a short report:

```markdown
## Coverage report — {Project}
- Tests added: {N}
- Test files added/modified: {list}
- Line coverage: {before}% → {after}%
- Branch coverage: {before}% → {after}%
- Uncovered hotspots remaining: {list with file:line}
- Bugs surfaced: {0 or list of BUG_FOUND}
- Build/test command : `dotnet test tests/Unit/{Project}.Tests`
```

## Quality gate before marking done

- [ ] `dotnet build Compendium.sln -c Release` is green.
- [ ] `dotnet test tests/Unit/{Project}.Tests -c Release` is green.
- [ ] Line coverage of the SUT project ≥ 90 % (or documented exemption).
- [ ] No flaky test (run twice, same result).
- [ ] No `Moq`, no `Assert.*`, no `Thread.Sleep` introduced (grep before commit).
- [ ] Branch is `feat/tests-{project-slug}`, commit is conventional (`test({project}): add unit tests for X`).
- [ ] PR body includes the coverage report block above.
