# Architecture tests

Compendium ships a small but strict set of architecture tests under
`tests/Architecture/Compendium.ArchitectureTests/`. They are powered by
[NetArchTest.Rules](https://github.com/BenMorris/NetArchTest) + xUnit +
AwesomeAssertions and run on every CI build.

The goal is not coverage of behaviour — that is the job of the unit and
integration test projects. The goal is to **fail fast when someone introduces
a dependency or naming pattern that violates the framework's architectural
contract**, before the regression ships in a NuGet package.

## How the tests are organised

| File | Concern |
|---|---|
| `HexagonalLayeringTests.cs` | Inner rings cannot reference outer rings (Core / Abstractions / Application / Infrastructure / Adapters). |
| `CqrsConventionTests.cs` | The CQRS public surface is shaped the way the documentation promises (markers, dispatchers, behaviors). |
| `EventSourcingConventionTests.cs` | Domain events are immutable, live in the right namespace, and never leak into adapters. |
| `ResultPatternConventionTests.cs` | `Result` / `Result<T>` stay immutable and keep the documented `IsSuccess`/`IsFailure`/`Error` surface. |
| `NamingConventionTests.cs` | Predictable namespace + suffix conventions for dispatchers, behaviors, projections, event stores, integration events. |
| `AssemblyAnchors.cs` | Centralised marker types used to obtain references to each Compendium framework assembly. |

## Rules enforced

### Hexagonal layering — `HexagonalLayeringTests`

| Rule | Rationale |
|---|---|
| `Core_ShouldNotDependOn_Abstractions` | Core is the innermost ring — pure domain primitives with zero outbound dependencies. |
| `Core_ShouldNotDependOn_Application` | Core must not know about CQRS orchestration. |
| `Core_ShouldNotDependOn_Infrastructure` | Core must not know about persistence, telemetry, or resilience. |
| `Core_ShouldNotDependOn_AnyAdapter` | Core must never reach outward into concrete adapters. |
| `Abstractions_ShouldNotDependOn_Application` | Ports are contractual; they cannot depend on application orchestration. |
| `Abstractions_ShouldNotDependOn_Infrastructure` | Ports do not know their adapters; otherwise tests cannot substitute fakes. |
| `Abstractions_ShouldNotDependOn_AnyAdapter` | Same direction-of-dependency invariant — adapters depend on abstractions, never the reverse. |
| `Application_ShouldNotDependOn_Infrastructure` | Application talks to the outside world only through ports defined in Abstractions. |
| `Application_ShouldNotDependOn_AnyAdapter` | Adapters are runtime-pluggable; Application never references them by type. |
| `Infrastructure_ShouldNotDependOn_AnyAdapter` | Infrastructure provides shared plumbing for adapters — adapters depend on it, never the reverse. |

### CQRS conventions — `CqrsConventionTests`

| Rule | Rationale |
|---|---|
| `CommandMarkerInterface_ShouldLiveIn_AbstractionsCommandsNamespace` | Consumers can locate `ICommand` by its documented namespace. |
| `QueryMarkerInterface_ShouldLiveIn_AbstractionsQueriesNamespace` | Consumers can locate `IQuery<T>` by its documented namespace. |
| `Dispatchers_ShouldBe_Sealed` | `*Dispatcher` types are coordination leaves — extension belongs in pipeline behaviors, not subclassing. |
| `Dispatchers_ShouldHaveCorresponding_Interface` | Every concrete dispatcher exposes an interface so consumers can substitute their own. |
| `PipelineBehaviors_ShouldLiveIn_BehaviorsNamespace` | Behaviors are discoverable under `Compendium.Application.CQRS.Behaviors`. |
| `CommandHandlerInterface_DoesNotExposeMutableProperties` | Handlers are behaviour contracts, never data carriers. |

### Event sourcing — `EventSourcingConventionTests`

| Rule | Rationale |
|---|---|
| `IDomainEvent_ShouldOnlyExpose_ReadOnlyProperties` | Events are immutable historical facts. |
| `DomainEventBase_ShouldNotExpose_PublicSettableProperties` | The framework base class must not let downstream code mutate history. |
| `DomainEventBase_ShouldBe_Abstract` | Consumers extend it per concrete event type. |
| `DomainEventBase_ShouldImplement_IDomainEvent` | Canonical implementation. |
| `IDomainEvent_ShouldLiveIn_CoreDomainEventsNamespace` | The marker is part of the inner ring and stays in Core. |
| `DomainEventBase_ShouldNotDependOn_AnyAdapter` | Domain events are persistence-agnostic. |
| `EventStoreInterface_ShouldLiveIn_AbstractionsNamespace` | The event-store port is contractual and lives in Abstractions so adapters can implement it. |

### Result pattern — `ResultPatternConventionTests`

| Rule | Rationale |
|---|---|
| `Result_ShouldExpose_IsSuccessAndIsFailureProperties` | The documented Result surface (`IsSuccess`, `IsFailure`, `Error`). |
| `Result_PublicProperties_ShouldNotHavePublicSetters` | A Result is observed by callers and must be immutable from the outside. |
| `GenericResult_ShouldBe_Sealed` | `Result<T>` is a closed sum type — behaviour belongs in extension methods. |
| `GenericResult_ShouldInheritFrom_NonGenericResult` | Uniform success/failure semantics across both forms. |
| `GenericResult_PublicProperties_ShouldNotHavePublicSetters` | Same immutability invariant for the generic form. |
| `Error_ShouldLiveIn_ResultsNamespace` | `Error` is part of the Result pattern and lives next to `Result`. |
| `Result_ShouldExposeStaticFactoryMethods_SuccessAndFailure` | Heuristic — preserves the documented `Result.Success(...)` / `Result.Failure(...)` factories. Marked `[Trait("Category", "Heuristic")]`. |

### Naming — `NamingConventionTests`

| Rule | Rationale |
|---|---|
| `Interfaces_ShouldStartWith_CapitalI` | Across Core, Abstractions, Application, Infrastructure — the standard .NET convention. |
| `Dispatcher_Types_ShouldLiveIn_CqrsNamespace` | Every `*Dispatcher` class is part of the CQRS coordination layer. |
| `Behavior_Types_ShouldLiveIn_BehaviorsNamespace` | `*Behavior` types are pipeline behaviors and live under `CQRS.Behaviors`. |
| `EventStoreImplementations_ShouldLiveIn_EventSourcingNamespace` | Concrete `*EventStore` types live under `Infrastructure.EventSourcing`. |
| `ProjectionTypes_ShouldLiveIn_ProjectionsNamespace` | Public `*Projection` classes live under `Infrastructure.Projections`. |
| `DomainEvents_AssemblyDefined_ShouldLiveIn_CoreDomainEventsNamespace` | Every `IDomainEvent` Compendium ships in Core lives in the documented folder. |
| `IntegrationEvents_ShouldLiveIn_DomainEventsNamespace` | `*IntegrationEvent` types live alongside `IDomainEvent` under `Core.Domain.Events`. |

## Running the tests

```bash
dotnet test tests/Architecture/Compendium.ArchitectureTests -c Release
```

The whole suite executes in <1 second — there is no reason not to run it on
every commit.

## Adding a new rule

1. Pick the file that matches the concern (or create a new one if no good fit).
2. Use [NetArchTest's fluent API](https://github.com/BenMorris/NetArchTest#writing-rules):
   ```csharp
   var result = Types.InAssembly(AssemblyAnchors.Application)
       .That()
       .HaveNameEndingWith("Handler")
       .Should()
       .BeSealed()
       .GetResult();

   result.IsSuccessful.Should().BeTrue(
       "...; offending types: {0}",
       string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
   ```
3. Always include the offending-type list in the assertion message — the rule
   has to tell the next developer *why* it is shouting.
4. If a rule is genuinely advisory (best-effort heuristic, may have legitimate
   exceptions), add `[Trait("Category", "Heuristic")]` so it can be filtered.
5. Use `AssemblyAnchors` to obtain assembly references — never hard-code
   `typeof(...)` against a deeply-nested type that could be renamed.

## Why these rules exist

Compendium publishes ten NuGet packages whose stability matters to every
downstream consumer. A drive-by `using Compendium.Adapters.PostgreSQL;` in
`Compendium.Core` would silently force every consumer to drag PostgreSQL into
their build. Architecture tests are the cheapest possible way to keep that
drift from happening.
