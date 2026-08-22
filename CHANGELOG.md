# Changelog

All notable changes to Compendium will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- **All seven heavy adapters extracted to their own repositories** per
  [ADR-0006](docs/adr/0006-multi-repo-adapter-split.md). `Compendium.Adapters.Stripe`,
  `Compendium.Adapters.LemonSqueezy`, `Compendium.Adapters.Zitadel`,
  `Compendium.Adapters.Listmonk`, `Compendium.Adapters.OpenRouter`,
  `Compendium.Adapters.PostgreSQL`, and `Compendium.Adapters.Redis` are no longer
  part of this repository. They now live at `sassy-solutions/compendium-adapter-<vendor>`
  and are released independently on their own version cadence. Same NuGet
  `PackageId`s; version sequence continues from `1.0.0-preview.9` per package.
  Consumers using `<PackageReference>` are unaffected — the packages are still
  resolved from nuget.org. Consumers using `<ProjectReference>` against this repo's
  `src/Adapters/` paths must switch to `<PackageReference>`.
- **Removed `Compendium.Extensions.ExternalAdapters` meta-package.** Previously
  bundled DI registration helpers for Zitadel, Listmonk, and LemonSqueezy. Each
  adapter now ships its own `Add<Vendor>...` extension method. To migrate, replace
  `services.AddCompendiumExternalAdapters(...)` with per-adapter calls (e.g.
  `services.AddZitadelIdentity(config)`, `services.AddListmonkEmail(config)`,
  `services.AddLemonSqueezyBilling(config)`).
- **Removed `Stripe.net`, `Dapper`, `Testcontainers.PostgreSql`, and
  `Testcontainers.Redis` package pins from `Directory.Packages.props`** — those
  transitive dependencies now belong to the per-adapter repositories. `Npgsql` and
  `StackExchange.Redis` pins are kept for the in-tree health-check probes in
  `Compendium.Adapters.AspNetCore` and for `samples/02-MultiTenant-WithPostgres`.
- **One release train per package instead of one for the whole repository.** A
  single MinVer tag prefix gave all thirty-five packages one version, so a fix
  in one shipped as a release of the other thirty-four and the version number
  stopped saying what had changed. Each project under `src/` now declares its
  own `<MinVerTagPrefix>`; the ten projects another project here depends on
  share the `core-v` train, the twenty-five nothing depends on get one each.
  Tagging `geo-v1.0.6` releases `Compendium.Abstractions.Geo` and nothing else.
  Release tags are now `<train>-v<version>`; the old `v<version>` tags no longer
  trigger a release. See `docs/operations/release-trains.md`.
- **Publication gates: provenance and republication.** Two packages are public
  whose nuspec records a commit that is on no tag, so they went through neither
  the test gate nor the coverage gate and cannot be rebuilt.
  `scripts/verify-package-provenance.sh` now refuses to publish a package whose
  recorded commit is missing, unknown to the repository, different from the one
  being built, or on no tag. `scripts/verify-package-not-published.sh` replaces
  `dotnet nuget push --skip-duplicate`, which turned a republication into a
  silent no-op; a version that already exists on the feed is now a red run.
- **Adapter-specific fix entries moved out of this changelog.** Entries that
  described behaviour of the seven extracted adapters were describing code this
  repository no longer builds or publishes. They now belong to the changelog of
  the repository that owns each adapter. This changelog documents only the
  packages published from here.
- **Framework `tests/Integration` no longer requires Docker.** Per ADR-0007, all
  framework E2E tests run on the `InMemory*` implementations (event store, streaming,
  projection, idempotency, snapshot, process-manager). Adapter-specific integration
  tests (Testcontainers + real PG/Redis) now live alongside the adapter that owns
  them in the per-adapter repos.

### Removed

- **`Compendium.Abstractions.Storage` is no longer built or published from this
  repository.** The same `PackageId` was produced by two repositories on two
  feeds: this one on nuget.org (`1.0.x` train) and `SCOJH/storage` on its GitHub
  feed (`1.1.x` train). A package name must designate a single source, so this
  repository drops it: `src/Abstractions/Compendium.Abstractions.Storage/` and
  its test project are deleted and removed from `Compendium.sln`. `SCOJH/storage`
  is now the sole owner of the name. Nothing in this repository referenced the
  project — it was packed and pushed without ever being consumed here.
  Consumers: move to `Compendium.Abstractions.Storage` `1.1.x` from the
  `SCOJH/storage` feed. The `1.0.x` versions already on nuget.org stay listed
  and restorable — they are marked deprecated there, pointing at the `1.1.x`
  package as the alternative, so an existing restore warns instead of breaking.

### CI

- **Strict 90 % line-coverage gate** activated on `build-test`. With the
  integration-bound PG/Redis surface gone, the framework's aggregate line coverage is
  comfortably above 90 % and the gate is no longer informational.

### Added

- **`Compendium.Adapters.Kubernetes.Sandbox`** (POM-431). Kubernetes adapter
  for `IAgentSandbox`: provisions an ephemeral non-root pod per agent run,
  drives it through `pods/exec` (bash exec, base64 file read/write/edit),
  deletes the pod on dispose. Ships with a multi-stage Dockerfile for the
  sandbox base image (`deploy/sandbox/coding-agent.Dockerfile`), a Helm
  chart with default-deny `NetworkPolicy` + minimal `ServiceAccount`/RBAC
  (`deploy/sandbox/helm/`), an ArgoCD `Application` template
  (`deploy/sandbox/argocd-application.yaml`), unit tests for the pod spec
  and DI wiring, Testcontainers-backed k3s integration tests (auto-skip
  when Docker is unavailable), and operator docs
  (`docs/operations/coding-agent-sandbox.md`). Unblocks the Claude Code /
  Codex / Gemini / OpenCode runtimes.
- **`IOrganizationService.GetOrganizationByNameAsync`** on the public
  identity-abstractions surface (`Compendium.Abstractions.Identity`). Lets an
  identity-provider adapter resolve an organization by name and reuse the
  existing id when creation comes back as a conflict, so a provisioning saga
  can be retried without leaving orphan organizations behind.
- **Typed state reload on `IProcessManagerRepository`.** New
  `Task<Result<IProcessManager<TState>>> GetByIdAsync<TState>(Guid id, ct)` overload
  rehydrates a saga's persisted state into the original typed shape so resumed steps
  can detect already-completed external work (the foundation for idempotent saga
  retries). Implemented in this repository for `InMemoryProcessManagerRepository`
  (returns the original instance, with a `Conflict` error on type mismatch);
  the relational implementation ships from the repository that owns that adapter.
  Existing untyped `GetByIdAsync(Guid id, ct)` is unchanged.

### Added

- **AI agent loop primitives.** New `Compendium.Abstractions.AI.Agents` namespace
  introduces `IAgent`, `IAgentToolRegistry`, and the supporting models
  (`AgentRequest`, `AgentResult`, `AgentTurn`, `AgentTool`, `AgentToolInvocation`,
  `AgentLoopOptions`, `AgentTerminationReason`). The default
  `Compendium.Application.AI.Agents.StandardAgent` implements a ReAct-style loop
  on top of any `IAIProvider`: tool descriptions and an action grammar are
  rendered into the system prompt, and the agent parses an `\`\`\`action` JSON
  block out of each response to dispatch tool calls through the registry. Works
  with any chat-capable model, no native tool-calling format required.
- `ReActPromptBuilder` and `ReActActionParser` are exposed publicly so callers
  can build custom agents that share the same grammar.
- New sample `samples/04-AI-Agent` demonstrates a two-tool loop end-to-end and
  ships a scripted offline provider so it runs without an API key.
- `ProjectionOptions.BackfillFromBeginningOnEmptyCheckpoint` (default `false`).
  Controls the cold-start behaviour of `LiveProjectionProcessor` when no
  projection has a persisted checkpoint:
  - `false` (legacy default) — jump to the current head of the event stream.
    Avoids replaying weeks of events on every restart.
  - `true` — start from position 0 and replay every event. Required when
    projections are the *only* writers to the read model; without it the read
    model stays empty and never catches up to the event store.
  Once any projection persists a checkpoint, that checkpoint takes over and
  this option is ignored.

### Notes

- `Compendium.Application` now references `Compendium.Abstractions.AI` so
  `StandardAgent` can sit in the application layer without forcing every
  consumer to add the project reference manually. No transitive contract change
  for existing CQRS / Saga / Idempotency users.

## [1.0.0-preview.4] - 2026-04-27

### Changed

- **Projections can now use DI dependencies.** `IProjectionManager.RegisterProjection<T>()`,
  `IProjectionManager.RebuildProjectionAsync<T>()`,
  `ILiveProjectionProcessor.RegisterProjection<T>()`, and
  `ServiceCollectionExtensions.AddProjection<T>()` no longer require
  `where TProjection : IProjection, new()`. Projections are resolved through the
  injected `IServiceProvider`, so any constructor dependency (logger, connection
  string, cache, metrics) is supported. Existing parameterless projections keep
  working as long as they are registered in DI; `AddProjection<T>()` now uses
  `TryAddSingleton<T>()` to register them automatically. Resolves #35.

### Added

- **Saga pattern, two flavors clearly separated.** Compendium now ships two
  distinct saga abstractions so users don't have to guess which kind they're
  using:
  - `Compendium.Abstractions.Sagas.ProcessManagers` — **DDD orchestration saga**
    (Vaughn Vernon's "Process Manager"): a stateful coordinator that groups
    aggregate operations inside one bounded context. Includes
    `ProcessManager<TState>` base class, `ProcessManagerOrchestrator`, and
    in-memory + PostgreSQL repositories.
  - `Compendium.Abstractions.Sagas.Choreography` — **Event-driven saga**:
    `IHandle<TEvent>` handlers, `IChoreographyRouter`, `IChoreographyContext`
    with correlation/causation propagation, `[Compensation]` metadata.
- `Compendium.Adapters.PostgreSQL.Sagas` — `PostgresProcessManagerRepository`
  with auto-schema creation, JSONB state snapshot, multi-tenant aware.
- DI helpers: `services.AddProcessManagers()`,
  `services.AddEventChoreography(...)`, and
  `services.AddPostgreSqlProcessManagerRepository(...)`.
- `docs/sagas.md` — decision tree, side-by-side comparison, migration guide
  from the deprecated `ISaga` API.

### Deprecated

- `Compendium.Application.Saga.ISaga`, `ISaga<TData>`, `SagaOrchestrator`,
  `ISagaOrchestrator`, `ISagaRepository`, `ISagaStepExecutor`,
  `ISagaFactory<,>` — kept with `[Obsolete]` for one minor version. The legacy
  API conflated orchestration and choreography; use `IProcessManager` or
  `IEventChoreography` instead. Removal scheduled for v1.0.

## [1.0.0-preview.3] - 2026-04-26

### Added

- Documentation site (DocFX, multi-version, GitHub Pages) (#17).
- 5 Architecture Decision Records (#14).
- Public `ROADMAP.md` (#15).
- Getting-started guide and 3 runnable samples (#20).
- 4 concept pages: event sourcing, hexagonal architecture, Result pattern, multi-tenancy (#21).
- 8 adapter how-to guides (AspNetCore, LemonSqueezy, Listmonk, OpenRouter, PostgreSQL, Redis, Stripe, Zitadel) (#22).

### Changed

- CodeQL Default Setup switched from `default` to `extended` query suite — adds maintainability/quality queries on top of security (csharp + actions).
- Dependabot now skips semver-major bumps on `Microsoft.Extensions.*`, `Microsoft.AspNetCore.*`, `Serilog.Settings.Configuration`, and `System.Text.Json` until the project moves to .NET 10 alongside Nexus (#25). Patch and minor bumps continue to flow.

### Security

- Pinned `softprops/action-gh-release` to commit SHA in `.github/workflows/release.yml` (#16, CodeQL `actions/unpinned-tag`, CWE-829, alert #28). 3rd-party action refs are now immutable.

## [1.0.0-preview.2] - 2026-04-25

### Added

- `Compendium.Adapters.Shared` — PII masking utilities used across adapters (introduced in #3).

### Changed

- Dependabot updates: `actions/upload-artifact` 4→7 (#4), `softprops/action-gh-release` 2→3 (#5), `actions/checkout` 4→6 (#6), `actions/cache` 4→5 (#7).
- OSS governance: CODEOWNERS, PR/issue templates, `SECURITY.md`, Code of Conduct, Dependabot config.

### Security

- CI: minimal `permissions:` block on workflows (#1, CodeQL `actions/missing-workflow-permissions`).
- Sanitize user-controlled path in tenant validation logs (#2, CodeQL `cs/log-forging`).
- Remove email from adapter logs for GDPR data minimization (#3, CodeQL `cs/exposure-of-sensitive-information`, 14 alerts closed).

## [1.0.0-preview.1] - 2026-04-24

### Added

First public preview release of Compendium, extracted from the
[Nexus](https://github.com/sassy-solutions/Nexus) monorepo.

**Core & Abstractions**

- `Compendium.Core` — DDD primitives: `AggregateRoot<TId>`, `ValueObject`, `Result<T>`, `Error`, `IDomainEvent` (zero external dependencies).
- `Compendium.Abstractions` — Shared ports and interfaces across layers.
- `Compendium.Abstractions.Identity` — Identity provider port.
- `Compendium.Abstractions.Email` — Email sender port.
- `Compendium.Abstractions.Billing` — Billing/subscription port.
- `Compendium.Abstractions.AI` — LLM/AI provider port.

**Application & Infrastructure**

- `Compendium.Application` — CQRS dispatchers, `ICommandHandler`, `IQueryHandler`.
- `Compendium.Infrastructure` — Resilience, telemetry, serialization primitives.
- `Compendium.Multitenancy` — Tenant context, scope, hierarchy.

**Adapters**

- `Compendium.Adapters.AspNetCore` — ASP.NET Core integration (health checks, problem details, DI glue).
- `Compendium.Adapters.PostgreSQL` — Event store and projection store on PostgreSQL.
- `Compendium.Adapters.Redis` — Cache, idempotency store.
- `Compendium.Adapters.Zitadel` — Zitadel identity adapter (`Compendium.Abstractions.Identity`).
- `Compendium.Adapters.Stripe` — Stripe billing adapter (`Compendium.Abstractions.Billing`).
- `Compendium.Adapters.LemonSqueezy` — LemonSqueezy billing adapter.
- `Compendium.Adapters.Listmonk` — Listmonk email adapter (`Compendium.Abstractions.Email`).
- `Compendium.Adapters.OpenRouter` — OpenRouter AI adapter (`Compendium.Abstractions.AI`).

**Extensions & Testing**

- `Compendium.Extensions.ExternalAdapters` — Composition helpers for external adapters.
- `Compendium.Testing` — Test utilities, fixtures, fakes for downstream consumers.

### Notes

- All 19 packages target `.NET 9` and are published on [nuget.org](https://www.nuget.org/packages?q=Compendium).
- Git history preserved from the originating Nexus monorepo via `git filter-repo`.
- Full MIT license.

[Unreleased]: https://github.com/SCOJH/Compendium/compare/v1.0.0-preview.4...HEAD
[1.0.0-preview.4]: https://github.com/SCOJH/Compendium/releases/tag/v1.0.0-preview.4
[1.0.0-preview.3]: https://github.com/SCOJH/Compendium/releases/tag/v1.0.0-preview.3
[1.0.0-preview.2]: https://github.com/SCOJH/Compendium/releases/tag/v1.0.0-preview.2
[1.0.0-preview.1]: https://github.com/SCOJH/Compendium/releases/tag/v1.0.0-preview.1
