# C4 — Level 2: Containers

For a framework, the "containers" are its **publishable units**: the NuGet packages
this repository ships, plus the two things that sit outside it — the host
application and the externally-hosted adapter packages. Every arrow below is an
actual `ProjectReference` (or NuGet reference) in the code; the direction is
enforced for the four main assemblies by the architecture tests in
`tests/Architecture/`.

```mermaid
flowchart BT
    subgraph repo["This repository — 35 packages, 26 release trains"]
        core["Compendium.Core<br/><i>DDD primitives, Result/Error,<br/>event contracts + serialization registry.<br/><b>Zero NuGet dependencies</b></i>"]

        abs["Compendium.Abstractions<br/><i>base ports: CQRS markers, IEventStore,<br/>IRepository, sagas</i>"]
        sat["Satellite abstraction packages<br/><i>Compendium.Abstractions.AI / .Billing /<br/>.Geo / .Git / .Secrets / … (one port per concern)</i>"]

        appl["Compendium.Application<br/><i>dispatchers, pipeline behaviors,<br/>process managers, choreography</i>"]
        mt["Compendium.Multitenancy<br/><i>tenant context, resolvers,<br/>consistency validation</i>"]
        infra["Compendium.Infrastructure<br/><i>projections, in-memory reference<br/>implementations of every port</i>"]

        aspnet["Compendium.Adapters.AspNetCore<br/><i>middleware, ProblemDetails,<br/>tenant HTTP resolution</i>"]
        intree["Incubating in-tree adapters<br/><i>GitHub · ClaudeCode · Kubernetes.Sandbox ·<br/>Scaleway.SecretManager</i>"]
        testing["Compendium.Testing<br/><i>fakes, fixtures, test helpers</i>"]

        abs --> core
        sat --> core
        sat -.->|"some via"| abs
        appl --> core
        appl --> abs
        appl -->|"agent loop"| sat
        mt --> core
        mt --> abs
        infra --> appl
        infra --> mt
        infra --> abs
        infra --> core
        aspnet --> infra
        aspnet --> abs
        aspnet --> core
        intree --> sat
        intree --> core
        testing --> infra
    end

    extadp["External adapter packages<br/><i>PostgreSQL, Redis, Zitadel, Stripe,<br/>AI providers, … — own repos,<br/>own release cadence</i>"]
    hostapp["Host application<br/><i>aggregates, handlers, projections,<br/>composition root</i>"]

    extadp -->|"implements ports<br/>(NuGet reference)"| sat
    extadp -.-> abs
    hostapp --> appl
    hostapp --> infra
    hostapp --> extadp

    classDef zerodep fill:#0b6e4f,stroke:#08523b,color:#fff
    classDef pkg fill:#1168bd,stroke:#0b4884,color:#fff
    classDef external fill:#999999,stroke:#6b6b6b,color:#fff
    class core zerodep
    class abs,sat,appl,mt,infra,aspnet,intree,testing pkg
    class extadp,hostapp external
```

## Reading notes (faithful to the code)

- **Dependencies point inward, toward Core.** The architecture tests
  (`HexagonalLayeringTests`) verify ten "must not depend on" rules across Core,
  Abstractions, Application and Infrastructure. Honest caveat: the test project
  loads only those four assemblies — Multitenancy, the satellite abstractions and
  the in-tree adapters are not covered by those tests today.
- **`Compendium.Application` → `Compendium.Abstractions.AI` is a real edge**, not a
  mistake in the diagram: the AI agent-loop contracts are consumed by Application,
  which is why `Abstractions.AI` rides the `core-v` release train with the ten
  interdependent packages.
- **`Infrastructure` depends on `Application` and `Multitenancy`** — it is the outer
  of the two middle rings, hosting orchestration-aware building blocks
  (`LiveProjectionProcessor` is a `BackgroundService`) and tenant-prefixed
  in-memory stores.
- **Satellite abstractions are deliberately tiny and independent**: 18 of them are
  leaf packages with their own release train each, so tagging `geo-v1.0.6` ships
  `Compendium.Abstractions.Geo` and nothing else (see
  [release trains](../operations/release-trains.md)).
- **The in-tree adapters are the exception, not the rule**: `AspNetCore` stays
  because it has no external SDK and evolves lock-step with the framework; the four
  others incubate here until their abstraction ships a stable tag
  ([ADR-0006](../adr/0006-multi-repo-adapter-split.md)).

Next: [Level 3 — components of the core flow](c4-level3-components.md)
