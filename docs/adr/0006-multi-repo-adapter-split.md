# 0006. Split heavy adapters into per-adapter repositories

* Status: Accepted
* Date: 2026-05-10
* Accepted: 2026-05-14
* Deciders: @sassy-solutions/maintainers

## Context

Compendium ships seven "heavy" adapters today, all living in the framework monorepo :

| Adapter | LOC | External SDK | Release driver |
|---|---|---|---|
| `Compendium.Adapters.PostgreSQL` | 3 692 | Npgsql | own pace |
| `Compendium.Adapters.Zitadel` | 3 752 | (HTTP) | Zitadel API churn |
| `Compendium.Adapters.LemonSqueezy` | 2 826 | (HTTP) | LS API churn |
| `Compendium.Adapters.Listmonk` | 1 546 | (HTTP) | Listmonk API churn |
| `Compendium.Adapters.Stripe` | 1 052 | Stripe.NET | Stripe.NET releases (~weekly) |
| `Compendium.Adapters.OpenRouter` | 935 | (HTTP) | OpenRouter API churn |
| `Compendium.Adapters.Redis` | 816 | StackExchange.Redis | own pace |

Plus two thin glue adapters that have no external SDK and tightly track the framework :

| Adapter | LOC | What |
|---|---|---|
| `Compendium.Adapters.AspNetCore` | 1 121 | middleware, ProblemDetails mappers, DI helpers |
| `Compendium.Adapters.Shared` | 30 | log-redaction helpers, etc. |

This shape has accumulated three frictions :

1. **Coverage measurement is dragged down by the integration-bound adapters.** After the May 2026 unit-coverage campaign every project sits ≥ 90 % except `Compendium.Adapters.PostgreSQL` (36 % — its DB-bound types are by design covered only by Docker integration tests, not unit). The aggregate global is pulled to 88.4 %, which makes a clean "≥ 90 %" CI gate impossible without per-project rules. See [the testing campaign log](../testing/coverage-campaign-2026-05.md).
2. **External SDK churn forces framework releases.** A weekly `Stripe.NET` patch bump bubbles up as a Compendium NuGet release ; consumers of the framework who don't care about Stripe still have to absorb it.
3. **Cross-cutting test boundaries.** Integration tests for `PostgreSQL` (Testcontainers + Postgres images), `Redis`, `Zitadel` (HTTP fixtures) all sit in the framework's `tests/Integration` and slow / complicate the framework CI.

We need a structure that decouples the framework's release cadence from the long tail of vendor-specific code without sacrificing the strict hexagonal layering decided in [ADR 0002](0002-hexagonal-architecture.md).

## Decision

Split the seven heavy adapters into **one repository per adapter**, leaving the thin glue adapters in the framework monorepo.

### Scope of the split

**Extracted (one repo each, named `compendium-adapter-<vendor>`)** :

- `compendium-adapter-postgresql`
- `compendium-adapter-zitadel`
- `compendium-adapter-lemonsqueezy`
- `compendium-adapter-listmonk`
- `compendium-adapter-stripe`
- `compendium-adapter-openrouter`
- `compendium-adapter-redis`

**Stays in `compendium`** :

- `Compendium.Core`, `Compendium.Application`, `Compendium.Abstractions.*`, `Compendium.Infrastructure`, `Compendium.Multitenancy`, `Compendium.Testing`
- `Compendium.Adapters.AspNetCore` — no external SDK, evolves lock-step with `Compendium.Application`.
- `Compendium.Adapters.Shared` — 30 LOC of cross-adapter helpers ; promote to a small `Compendium.Adapters.Common` NuGet *only when a second consumer needs it*.

### Per-adapter repository contract

Each extracted adapter repo :

1. Depends on the published `Compendium.Abstractions.*` NuGet (whichever ports it implements).
2. Ships its own NuGet : `Compendium.Adapters.<Vendor>` and `Compendium.Adapters.<Vendor>.Tests` is internal.
3. Owns its **own integration tests** (Testcontainers, vendor sandboxes) so the framework CI no longer carries Docker images for `pg` / `redis`.
4. Sets a **CI line-coverage gate of 90 %** on the unit-testable surface ; types that genuinely require a live external system are covered by integration tests in the same repo.
5. Follows the **same testing conventions** as the framework (xUnit 2.9, FluentAssertions 6.12, NSubstitute 5.1, AAA explicit comments, Result-pattern assertions, `IAsyncLifetime` fixtures, no Moq / no `Assert.*` / no `Thread.Sleep`). Codified in the `compendium-test-author` skill, which the template ships with.
6. Tracks the framework via [Renovate](https://docs.renovatebot.com/) ; a Compendium release auto-opens a PR in every adapter repo within 24 h ; the adapter repo's CI must stay green or the PR blocks.

A starter template lives at `templates/adapter-dotnet/` in this repository (see PR opening this ADR). It is the canonical seed for a new adapter repo : copy, rename `<Vendor>`, push to a fresh GitHub repo, set up NuGet publishing.

### Migration order (suggestion, not binding)

1. **Compendium.Adapters.Stripe** first — heaviest external-SDK churn, smallest domain footprint, validates the workflow.
2. **Compendium.Adapters.PostgreSQL** — biggest coverage win on the framework side ; once extracted, the framework's global line coverage rises from 88.4 % to ~95 %, making a strict 90 % gate possible.
3. **Compendium.Adapters.Redis** — small, integration-bound, similar shape to PG.
4. The remaining four (`Zitadel`, `LemonSqueezy`, `Listmonk`, `OpenRouter`) follow once the workflow is proven.

Each migration is its own PR pair (one in `compendium` removing the project, one in the new repo bringing it in with full history via `git filter-repo`).

## Consequences

### Positive

- **Independent release cadence per adapter.** A Stripe.NET bump no longer triggers a framework release.
- **Coverage gate becomes clean.** Once PostgreSQL leaves, framework global goes from 88.4 % → ~95 %, and a strict per-project ≥ 90 % gate is achievable without exemptions.
- **Smaller, vendor-focused PRs.** Reviewers don't need to context-switch between framework internals and `Stripe.WebhookSignature` minutiae.
- **CI cost.** Framework CI no longer pulls Postgres / Redis images — faster pipelines, less GitHub Actions minutes.
- **Discoverability.** Each adapter repo has its own README, examples, support contract.
- **Open-source contribution surface.** Easier for outside contributors to focus on one vendor.

### Negative / Trade-offs

- **Cross-cutting changes to abstractions multiply in cost.** A modification to `IEventStore` or `IDomainEvent` in `Compendium.Abstractions` becomes N PRs (one per adapter repo) instead of one. Mitigated by :
  - **Strict semver discipline** on `Compendium.Abstractions.*` — breaking changes trigger major-version bumps and are batched.
  - **Renovate auto-PR** to every adapter repo on every Compendium release ; review burden distributed but mechanical.
  - A "release train" pattern : Compendium ships, adapters auto-PR within 24 h, central dashboard tracks who's behind.
- **Local-dev experience worsens** for someone modifying both framework and adapter at once. Mitigated by a `dotnet add reference` "linked-mode" documented in the template README — point the adapter project at a local `Compendium.Abstractions.csproj` instead of the NuGet during local hacking.
- **Repo proliferation.** Eight repos to maintain instead of one. Each gets its own Dependabot, Renovate, branch protection, secrets, NuGet publishing token. Mitigated by the template baking all of this in.
- **Documentation is now distributed.** A user looking at "what does Compendium support?" has to crawl multiple READMEs. Mitigated by an adapter index page on the framework's `docs/adapters/`.
- **Migration cost.** Each extraction is at least a half-day of work : history transplant, NuGet pipeline, branch protection, Dependabot, Renovate, badges, README, sample. We pay this once per adapter and never again.

## Alternatives considered

- **Status quo : keep the monorepo.** Rejected — the three frictions above (coverage gate impossible, framework releases coupled to vendor SDK churn, slow CI) are a permanent tax that compounds with every adapter we add.
- **Multi-package repository (one repo, many NuGets, separate release lines).** Rejected — possible with paths-filter on Actions and per-project versioning, but it doesn't address the CI-time cost of Docker images, doesn't simplify ownership / contribution, and still ties every release to the same git history. Worth it only if we expected ≤ 2 adapters.
- **Plugin-style runtime discovery** (adapters loaded at runtime via assembly probing). Rejected — adds a layer of indirection nobody asked for, breaks AOT publishing, and doesn't solve the source-organisation question.
- **Submodules** (one repo, adapters pulled as git submodules). Rejected — submodules are notoriously painful for contributors, don't help CI parallelism, and confuse most modern tooling (Renovate, GitHub Search, code navigation in IDEs).

## How to use the template

Generate a new repository from [`sassy-solutions/template-compendium-adapter-dotnet`](https://github.com/sassy-solutions/template-compendium-adapter-dotnet) (it's a GitHub template repo):

1. **Use this template** → name the new repo `compendium-adapter-<vendor>`.
2. The `template-cleanup` workflow auto-renames `Sample` → `<Vendor>` on first push.
3. Add the `NUGET_API_KEY` secret (org-level secret with `Compendium.*` glob recommended).
4. Implement the adapter, write tests to the 90% line gate.
5. Tag `v1.0.0-preview.N` (continuing the framework's sequence for that PackageId) → publishes to nuget.org.

## Migration log

Adapters extracted from this monorepo (chronological):

| Date | Adapter | New repo | First standalone version |
|---|---|---|---|
| 2026-05-12 | Stripe | [`compendium-adapter-stripe`](https://github.com/sassy-solutions/compendium-adapter-stripe) | `1.0.0-preview.9` |
| 2026-05-13 | OpenRouter | [`compendium-adapter-openrouter`](https://github.com/sassy-solutions/compendium-adapter-openrouter) | `1.0.0-preview.9` |
| 2026-05-13 | Listmonk | [`compendium-adapter-listmonk`](https://github.com/sassy-solutions/compendium-adapter-listmonk) | `1.0.0-preview.9` |
| 2026-05-13 | LemonSqueezy | [`compendium-adapter-lemonsqueezy`](https://github.com/sassy-solutions/compendium-adapter-lemonsqueezy) | `1.0.0-preview.9` |
| 2026-05-13 | Zitadel | [`compendium-adapter-zitadel`](https://github.com/sassy-solutions/compendium-adapter-zitadel) | `1.0.0-preview.9` |
| 2026-05-14 | PostgreSQL | [`compendium-adapter-postgresql`](https://github.com/sassy-solutions/compendium-adapter-postgresql) | `1.0.0-preview.9` |
| 2026-05-14 | Redis | [`compendium-adapter-redis`](https://github.com/sassy-solutions/compendium-adapter-redis) | `1.0.0-preview.9` |

All seven heavy adapters now live in their own repositories. The framework's `tests/Integration` no longer requires Docker (per [ADR-0007](0007-integration-test-split-and-inmemory-defaults.md)); per-adapter integration tests live alongside the adapter that owns them. The framework CI's line-coverage gate is now strict at ≥ 90 %.

### In-tree adapters staged for extraction

New adapters may incubate in-tree while their abstraction stabilizes, then follow the same extraction path once the abstraction ships a stable (non-preview) tag:

| Date added in-tree | Adapter | External SDK(s) | Scheduled extraction repo | Extraction trigger |
|---|---|---|---|---|
| 2026-07-24 | `Compendium.Adapters.GitHub` | Octokit, Sodium.Core | `compendium-adapter-github` | First stable (non-preview) tag of `Compendium.Abstractions.Git`. |

`Compendium.Adapters.GitHub` is unit-testable end-to-end (WireMock for the REST paths, NSubstitute for the Octokit paths), so unlike the integration-bound adapters above it does not depress the framework's unit-coverage gate and can incubate in-tree without a coverage exemption until the Git abstraction stabilizes.

The convenience meta-package `Compendium.Extensions.ExternalAdapters` (which previously re-exposed Zitadel + Listmonk + LemonSqueezy DI helpers) was removed as part of this transition — consumers wire each adapter directly through its own DI extension method (`AddStripeBilling`, `AddZitadelIdentity`, etc.).
