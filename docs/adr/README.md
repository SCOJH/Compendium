# Architecture Decision Records

ADRs document the structural choices of Compendium. Format: [MADR](https://adr.github.io/madr/).

| # | Title | Status | Date |
|---|---|---|---|
| 0001 | [Result pattern over exceptions](0001-result-pattern.md) | Accepted | 2026-Q2 |
| 0002 | [Hexagonal architecture (strict)](0002-hexagonal-architecture.md) | Accepted | 2026-Q2 |
| 0003 | [Zero-dependency Core](0003-zero-dep-core.md) | Accepted | 2026-Q2 |
| 0004 | [Multi-tenancy strategy](0004-multi-tenancy-strategy.md) | Accepted | 2026-Q2 |
| 0005 | [Event sourcing over state-stored](0005-event-sourcing-vs-state.md) | Accepted | 2026-Q2 |
| 0006 | [Multi-repo adapter split](0006-multi-repo-adapter-split.md) | Accepted | 2026-05-14 |
| 0007 | [Integration test split (InMemory default)](0007-integration-test-split.md) | Accepted | 2026-05-14 |
| 0008 | [One release train per package](0008-release-train-per-package.md) | Accepted | 2026-08-30 |
| 0009 | [Publication gates: provenance, no republication, one feed](0009-publication-gates.md) | Accepted | 2026-08-30 |
| 0010 | [Logical event identity](0010-logical-event-identity.md) | Accepted | 2026-08-30 |
| 0011 | [Whitelist deserialization & read-time upcasting](0011-whitelist-deserialization-upcasting.md) | Accepted | 2026-08-30 |
| 0012 | [Hand-rolled CQRS dispatch, no external mediator](0012-hand-rolled-cqrs-dispatch.md) | Accepted | 2026-08-30 |
| 0013 | [One abstraction package per concern](0013-abstraction-package-per-concern.md) | Accepted | 2026-08-30 |

ADRs 0008–0013 were recorded retroactively on 2026-08-30: the decisions are read
from the code, the git history, and the CHANGELOG — each record says so and cites
its evidence. See the [architecture overview](../architecture/overview.md) for how
they fit together.

## Process
- Propose new ADR via PR with status `Proposed`
- Discuss in PR review
- On merge → status `Accepted` (or `Rejected`)
- Superseding an ADR = new ADR + update old's status to `Superseded by ####`

## Template
See [0000-template.md](0000-template.md).
