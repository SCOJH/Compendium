# 0012. Hand-rolled CQRS dispatch instead of an external mediator

* Status: Accepted
* Date: 2026-08-30 (recorded retroactively)
* Deciders: @sassy-solutions/maintainers

> **Honesty note.** Unlike the other ADRs, no artifact in this repository records
> an explicit evaluation of mediator libraries — the word "MediatR" appears nowhere
> in the history. What *is* in the code is unambiguous: a complete, deliberate
> re-implementation of the mediator pipeline shape (`IPipelineBehavior<TRequest,
> TResponse>`, `RequestHandlerDelegate<TResponse>` — MediatR's exact vocabulary)
> with zero mediator dependency. This record reconstructs the rationale from that
> evidence; the "alternatives" section is reasoning read out of the code, not
> minutes of a meeting.

## Context

Every feature in a Compendium application flows through command and query dispatch,
so whatever implements that dispatch is referenced by `Compendium.Application` —
and therefore by **every handler in every consumer**. The obvious off-the-shelf
answer is MediatR, whose `IPipelineBehavior` model is the de-facto standard shape
for cross-cutting concerns in .NET CQRS codebases.

Three forces pushed against taking the dependency:

1. **Fan-out.** The framework's whole dependency philosophy
   ([ADR-0003](0003-zero-dep-core.md)) is that anything in the core packages'
   graph propagates to everyone. A mediator in `Compendium.Application` is a
   version-pinning decision imposed on every consumer forever.
2. **Surface mismatch.** The framework needs a *thin* dispatch: resolve one
   handler, run an ordered behavior chain, return `Result<T>`
   ([ADR-0001](0001-result-pattern.md)). It does not need notifications, streaming
   requests, or exception-based flow — and a mediator's exception-first error model
   works against the Result contract at every call site.
3. **Ecosystem risk.** MediatR's move to a commercial license (2025) made the cost
   of the dependency explicit for a framework that redistributes it transitively.
   (Reconstructed motive — plausible given the timeline, not written down anywhere.)

## Decision

`Compendium.Application` ships its own dispatch, ~200 lines total:

- `ICommandDispatcher` / `IQueryDispatcher` interfaces with **sealed**
  implementations (`CommandDispatcher`, `QueryDispatcher`) — sealed-with-interface
  is enforced by the architecture tests, so consumers substitute via DI, never via
  inheritance.
- Handlers are resolved from `IServiceProvider` (`ICommandHandler<TCommand>`,
  `IQueryHandler<TQuery, TResponse>` from `Compendium.Abstractions`). A missing
  handler is `Result.Failure(Error.NotFound("Handler.NotFound", …))` — never an
  exception.
- The pipeline keeps the community's proven shape on purpose:
  `IPipelineBehavior<TRequest, TResponse>` + `RequestHandlerDelegate<TResponse>`,
  chained in registration order. Anyone who has written a MediatR behavior can
  write a Compendium behavior.
- Four behaviors ship (`Validation` — DataAnnotations, `Logging`, `Transaction`,
  `Idempotency`), all **opt-in**: nothing registers them by default, and no
  umbrella `AddCompendiumApplication()` exists. The consumer's composition root
  states exactly what runs.
- Dispatch is instrumented natively (`CompendiumTelemetry` activity source and
  counters) instead of through a behavior, so tracing exists even with an empty
  pipeline.

## Consequences

### Positive

- Zero third-party packages between a consumer's handler and the framework — no
  license exposure, no version conflicts, no transitive surprises.
- The error model is uniform: dispatch failures, validation failures, and business
  failures are all `Result`, mappable at one place at the HTTP edge.
- The pipeline shape is community-standard, so onboarding cost is near zero and
  migration *to or from* a mediator library stays mechanical.
- The dispatch surface is small enough to read in one sitting — a property worth
  actual money when it sits under every request.

### Negative / Trade-offs

- **We own the maintenance** of code the ecosystem would otherwise maintain —
  accepted because the surface is deliberately tiny and stable.
- **The pipeline covers commands only.** `QueryDispatcher` invokes the handler
  directly and runs **no behaviors** — queries get no validation, logging, or
  idempotency from the pipeline. The asymmetry is real, currently undocumented at
  the API level, and a consumer can be surprised by it.
- No notification/publish mechanism and no streaming requests — by scope, not
  omission; event fan-out belongs to projections and choreography, not the
  dispatcher.
- All wiring is manual (`AddScoped<ICommandDispatcher, CommandDispatcher>()`, one
  registration per behavior). Explicitness was chosen over convenience; the cost is
  a longer composition root and a real "forgot to register X" class of mistakes.
- Behavior interface detection in `Transaction`/`Idempotency` behaviors matches
  `ICommand` **by interface name via reflection** rather than by type identity — a
  shortcut that works but is weaker than the rest of the design.

## Alternatives considered

*(Reconstructed — see honesty note above.)*

- **MediatR.** The default choice; rejected on fan-out (a redistributed framework
  imposing the dependency on all consumers), error model (exceptions vs
  [ADR-0001](0001-result-pattern.md)), surface (notifications and streams unused),
  and, from 2025, commercial licensing of a transitively-shipped component.
- **Wolverine / Brighter.** Heavier still: runtime code generation, messaging
  ambitions, their own persistence opinions — each individually against the grain
  of a framework whose adapters are supposed to be the only opinionated parts.
- **No dispatcher at all** (inject `ICommandHandler<T>` directly). Genuinely
  simpler, and Result-compatible — but it dissolves the seam where cross-cutting
  behaviors and telemetry attach, pushing validation/logging/idempotency back into
  every handler or into decorators the consumer must compose by hand.
- **Source-generated dispatch** (compile-time handler registry). Attractive — no
  reflection, no service-locator lookup — but the framework has no source-generator
  infrastructure today (see the unshipped `IGeneratedEventRegistry` story), and
  runtime resolution via DI is the semantics consumers already understand.
