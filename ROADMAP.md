# Compendium Roadmap

> Last updated: 2026-04-25

This document captures where Compendium is heading. It is a **living document**: priorities may shift as we learn from production usage and community feedback. **No date here is a commitment.** If you depend on a specific item, please open a Discussion to talk about it.

## Vision

Compendium exists to make event-sourced, multi-tenant SaaS development on .NET pragmatic and boring — in the best sense. We've built the same plumbing too many times across products: aggregate base classes, an event store, projection runners, tenant resolution, billing adapters, identity glue. Compendium is that plumbing extracted, hardened, and made reusable so that domain-focused teams can spend their time on the actual product.

The framework will stay small, hexagonal, and dependency-light. We optimize for the **second product** built on Compendium, not the first — meaning patterns must compose, abstractions must be honest, and breaking changes must be rare and well-signaled.

## Themes

The roadmap is organized around five durable themes:

- **Stable foundations** — Core APIs in `Compendium.Core` and `Compendium.Abstractions.*` reach 1.0 and follow strict semantic versioning. Breaking changes only on major bumps, with migration notes.
- **Multi-tenancy first-class** — Every adapter and infrastructure piece is tenant-aware by default. Cross-tenant leaks are bugs, not edge cases.
- **Adapter ecosystem** — Cover the SaaS-essentials surface (billing, identity, AI, email, observability) with first-party adapters, plus a clear extension model for community-built ones.
- **Production-grade observability** — Structured logs without PII (see [ADR 0004](docs/adr/0004-multi-tenancy-strategy.md) and the GDPR work in `PiiMasking`), OpenTelemetry traces, and meaningful metrics out of the box.
- **Developer experience** — Runnable samples for every major adapter, copy-pasteable snippets in docs, conventional patterns documented as ADRs.

## What's next

### 2026 Q2 — Saga pattern

Durable workflows for orchestrating multi-aggregate business processes. Today, coordinating "place order → reserve inventory → charge card → notify customer" across boundaries requires hand-rolled state machines. The saga pattern brings this in-framework.

Scope under consideration:
- **Process managers** — Long-running, event-driven coordinators with their own state.
- **Compensation** — Distributed rollback when a step in the saga fails.
- **Persistence** — Saga state stored in PostgreSQL (primary), with Redis evaluated for high-throughput cases.
- **Idempotency primitives** — Saga steps must be re-runnable without side effects.
- **Observability** — First-class traces and timelines for in-flight sagas.

Related discussion: open a thread under the `Roadmap` category if you have a use case you'd like considered.

### 2026 Q3 — New adapters

Additional adapters will be announced as the quarter approaches. Priorities are informed by Discussions and by demand signals from production deployments. If you have a strong need for a specific integration, please open a Discussion describing the use case; maintainers may create or label a tracking issue as `roadmap-input`.

### 2026 Q4 — New adapters

The cadence of new adapters continues. Specifics will be sharpened during Q3 based on what landed and what didn't.

## Out of scope (current)

We say no often, and we say it on purpose. The following are **not** planned, in the interest of keeping Compendium focused:

- **Built-in UI / dashboard** — Bring your own. Compendium exposes the data; rendering it is your stack's job.
- **Message broker abstraction** — If you need Kafka, RabbitMQ, NATS, or similar, use those libraries directly. Compendium will not ship a lowest-common-denominator broker interface.
- **Multi-language SDK** — .NET only. Server boundaries are HTTP/JSON; clients can be in any language.
- **ORM replacement** — Compendium is not an ORM. Read models that need rich querying should use the right tool (EF Core, Dapper, raw SQL) for that read model.
- **Legacy framework support** — `.NET Standard 2.0`, `.NET Framework`, etc. are not targets. Compendium tracks the latest LTS / current .NET.

## How to influence the roadmap

- **Open a Discussion** under the [Roadmap](https://github.com/SCOJH/Compendium/discussions/categories/roadmap) category (or under `Ideas` if Roadmap doesn't exist yet).
- **Vote with 👍** on issues tagged [`roadmap-input`](https://github.com/SCOJH/Compendium/issues?q=is%3Aissue+label%3Aroadmap-input).
- **Contribute** — see [CONTRIBUTING.md](CONTRIBUTING.md). Roadmap items are open to community implementation; coordinate via the relevant Discussion or issue first.

## Past releases

See [CHANGELOG.md](CHANGELOG.md) for what has shipped.

---

This roadmap is a living document. No date here is a commitment.
