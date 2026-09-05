# 0013. One abstraction package per concern

* Status: Accepted
* Date: 2026-08-30 (recorded retroactively — the shape is visible in `src/Abstractions/` and in the per-package release trains of [ADR-0008](0008-release-train-per-package.md))
* Deciders: @sassy-solutions/maintainers

## Context

[ADR-0002](0002-hexagonal-architecture.md) put the ports in `Compendium.Abstractions.*`
assemblies; [ADR-0006](0006-multi-repo-adapter-split.md) moved the heavy adapters to
their own repositories, consuming those ports as published NuGet packages. That
left open a granularity question with real consequences: **how many packages should
the ports be?**

A single fat `Compendium.Abstractions` would mean:

- An external adapter (say, a Geo provider) must reference — and re-release
  against — a package that also carries billing, AI, email, and search contracts it
  never touches. Every port change anywhere bumps everyone.
- A consumer's dependency graph declares intent poorly: "references Abstractions"
  says nothing; "references `Abstractions.Billing`" is documentation.
- The blast radius of a breaking change in one port is the entire port surface.

Against that, many small packages cost real overhead: more `.csproj` files, more
NuGet metadata, more release trains, slower cold builds.

## Decision

**One NuGet package per port concern.** The shape in `src/Abstractions/` is:

- A base **`Compendium.Abstractions`** carrying the contracts that are inseparable
  from the framework's execution model: CQRS markers and handler interfaces, the
  event store and snapshot ports, `IRepository`, and the saga contracts.
- **Satellite packages, one concern each** — `Compendium.Abstractions.AI`,
  `.Analytics`, `.Authorization`, `.Billing`, `.Caching`, `.CodingAgents`, `.Crm`,
  `.Documents`, `.Email`, `.FeatureFlags`, `.Geo`, `.Git`, `.Identity`, `.Jobs`,
  `.Messaging`, `.Notifications`, `.Realtime`, `.Search`, `.Secrets`, `.Speech`,
  `.Translation`, `.VectorStore`, `.Webhooks` — interfaces and DTOs only, no
  implementations, no vendor SDKs.
- An adapter references **exactly the port it implements** (verified in the
  in-tree adapters: `Adapters.GitHub` → `Abstractions.Git`, `Adapters.ClaudeCode`
  → `Abstractions.CodingAgents`, `Adapters.Scaleway.SecretManager` →
  `Abstractions.Secrets`).
- Combined with [ADR-0008](0008-release-train-per-package.md), each satellite that
  nothing in-repo depends on ships on its **own release train**: `geo-v1.0.6`
  releases the Geo contracts and nothing else.

## Consequences

### Positive

- An adapter's dependency surface is its port, full stop — a Geo adapter is never
  forced to re-release because the billing contracts changed.
- Breaking-change blast radius shrinks to one concern; strict semver applies per
  port instead of per "everything".
- The dependency graph becomes self-documenting: what a service references *is*
  the list of capabilities it uses.
- New concerns incubate cheaply: a new port is one small project with its own
  train, promotable to a domain repository later without touching the others.

### Negative / Trade-offs

- **Project proliferation is real**: 23 abstraction projects among 35 total, each
  with csproj, tests, packaging metadata, and a train. Accepted as the cost of the
  granularity; the template and MSBuild conventions absorb most of it.
- **The granularity is not uniform in practice.** Five satellites (`AI`, `Caching`,
  `CodingAgents`, `Git`, `Secrets`) are welded to the `core-v` train because an
  in-repo project references them — notably `Compendium.Application` →
  `Abstractions.AI` (the agent-loop contracts). Their *packaging* is independent
  but their *release cadence* is not. Anyone presenting this design should know
  that nuance.
- **Dependency shape is inconsistent between satellites**: some reference the base
  `Compendium.Abstractions`, others reference `Compendium.Core` directly. Harmless
  today, but it means "satellite" is not a single, uniform layer.
- **The architecture tests do not cover the satellites** — the layering rules are
  verified for the four main assemblies only. The satellites' discipline
  (interfaces-only, no vendor SDKs) currently holds by convention and review, not
  by a test.
- Discoverability: a newcomer faces 23 packages where one "Abstractions" would be
  simpler to find. Mitigated by the README package table and by naming.

## Alternatives considered

- **One fat `Compendium.Abstractions`.** Rejected — couples every adapter and
  consumer to every port's release cadence and blast radius; see Context.
- **Ports inside each adapter package.** Rejected — an application swapping vendors
  would swap contract assemblies too, defeating the point of ports; two adapters
  for the same concern could not share a contract.
- **Ports in `Compendium.Core`.** Rejected — Core is domain primitives with zero
  dependencies and zero opinions about infrastructure concerns
  ([ADR-0003](0003-zero-dep-core.md)); 23 concerns' worth of DTOs does not belong
  in the most-referenced assembly.
- **Grouping by broad family** (e.g. one `Abstractions.Persistence`, one
  `Abstractions.Communication`). Rejected — the groups would be arbitrary where
  per-concern is mechanical, and the domain-repo topology
  ([ADR-0006](0006-multi-repo-adapter-split.md)'s successor work) already groups at
  the level that matters: one *repository* per domain, each consuming its own
  abstraction package.
