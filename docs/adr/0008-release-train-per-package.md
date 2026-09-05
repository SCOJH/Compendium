# 0008. One release train per package, partitioned by the dependency graph

* Status: Accepted
* Date: 2026-08-30 (recorded retroactively — the decision shipped with commit `7523771` and is documented operationally in [release trains](../operations/release-trains.md))
* Deciders: @sassy-solutions/maintainers

## Context

Until mid-2026 a single MinVer tag prefix (`v*`) drove every package in this
repository. Tagging `v1.0.5-preview.4` packed all thirty-five projects under
`src/` and pushed all thirty-five to nuget.org. Three consequences accumulated:

1. **Version numbers stopped carrying information.** A one-line fix in
   `Compendium.Abstractions.Geo` shipped as a new release of `Compendium.Core`,
   `Compendium.Testing`, and thirty-two other packages. A consumer looking at a
   version bump could not tell whether anything relevant to them had changed.
2. **Everything moved together or nothing moved.** Downstream (Nexus,
   external adapter repositories) this hardened into a de-facto rule that the whole
   framework upgrades as one unit — exactly the coupling [ADR-0006](0006-multi-repo-adapter-split.md)
   was created to remove between the framework and its adapters, reproduced
   *inside* the framework.
3. **Re-pushing an old tag republished thirty-five packages** — an operational
   hazard rather than a versioning scheme.

At the same time, some packages genuinely *must* move together: a
`Compendium.Application` release that depends on an untagged `Compendium.Core`
version fails at the consumer's `restore`.

## Decision

Each project under `src/` declares its own `<MinVerTagPrefix>` in its `.csproj`,
next to its `<PackageId>`. **Twenty-six trains cover thirty-five packages.**
Release tags are `<train>-v<version>` (e.g. `geo-v1.0.6`); pushing a tag packs and
publishes exactly the projects of that train and nothing else. The old `v*` tags no
longer match the release trigger, so replaying them publishes nothing.

The partition is **read off the `ProjectReference` graph, not chosen by taste**:

- A project that **another project in this repository depends on** joins the shared
  `core-v` train (ten packages: `Core`, `Abstractions`, `Abstractions.AI`,
  `.Caching`, `.CodingAgents`, `.Git`, `.Secrets`, `Application`, `Infrastructure`,
  `Multitenancy`). Interdependent packages are never shipped separately, so a
  published package can never reference a sibling version that was never tagged.
- A project **nothing here depends on** gets a train of its own — eighteen satellite
  abstractions, six in-tree adapters, and `Compendium.Testing`.

Two mechanical safeguards make the rule enforceable rather than aspirational:

- `Directory.Build.props` deliberately declares **no** default prefix. A project
  without a train would not fail to build — MinVer would silently hand it
  `0.0.0-alpha.0.N` — so `scripts/verify-package-trains.sh` fails CI when any
  packable project lacks a `<MinVerTagPrefix>` (and fails on an empty scan: *"a
  gate that passes on an empty scan is not a gate"*).
- `scripts/select-train-projects.sh` maps a tag to its projects and exits non-zero
  on an unknown train — a typo in a tag is an error, not an empty release.

## Consequences

### Positive

- A version bump means something again: `geo-v1.0.6` says *Geo changed*, full stop.
- Leaf packages (satellite abstractions, incubating adapters) evolve at their own
  pace without dragging the core along — the same decoupling ADR-0006 bought
  between repositories, now applied within the repository.
- The partition rule is an algorithm, not a judgment call: adding a package means
  adding one MSBuild property and seeding one tag. Disagreements about "which train"
  are settled by the dependency graph.
- Old global tags are inert by construction; the republish-everything hazard is gone.

### Negative / Trade-offs

- **The `core-v` train is still a ten-package monolith.** A fix in
  `Compendium.Multitenancy` ships a new `Compendium.Core` version. This is the
  price of the "interdependent packages move together" invariant; shrinking the
  train means cutting `ProjectReference` edges, which is a design change, not a
  versioning change.
- **Twenty-six trains means twenty-six tag sequences** to reason about, and a
  one-time bootstrap (`scripts/bootstrap-train-tags.sh`) was needed to seed them.
- **`CHANGELOG.md` has not caught up**: it still tracks a single version line
  (`1.0.0-preview.N`) and links to the retired global tags. With per-train
  versions, a single changelog version no longer designates any package. Known
  debt, not yet resolved.
- Release provenance and duplicate protection become *more* important, not less,
  with many small releases — addressed separately in [ADR-0009](0009-publication-gates.md).

## Alternatives considered

- **Keep the single global version.** Rejected — the three frictions above are
  structural and compound with every package added.
- **Fully independent version per package (35 trains).** Rejected — publishing
  `Compendium.Application` independently of the `Compendium.Core` it references
  creates version matrices that no one tests; the dependency graph says these ten
  move together, so they share a train.
- **Hand-curated train membership.** Rejected — any partition that is a matter of
  taste will drift; reading it off `ProjectReference` makes the rule checkable by a
  script and the script is in CI.
- **Splitting the repository further** (one repo per train). Rejected for now —
  ADR-0006 already extracted what had an independent release *driver* (vendor SDK
  churn); the remaining packages share CI, conventions, and reviewers, and the
  train mechanism buys the release independence without the repo overhead.
