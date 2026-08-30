# 0009. Publication gates: provenance required, republication forbidden, one feed

* Status: Accepted
* Date: 2026-08-30 (recorded retroactively — the gates shipped with the release-train work and are documented operationally in [release trains](../operations/release-trains.md))
* Deciders: @sassy-solutions/maintainers

## Context

An audit during the release-train work ([ADR-0008](0008-release-train-per-package.md))
found that the publishing pipeline would say yes to things it should refuse:

1. **Two public packages exist whose nuspec records a commit on no tag.**
   `Compendium.Abstractions.Geo 1.0.4` and `Compendium.Abstractions.Messaging 1.0.4`
   were published from commits reachable from `main` but never tagged. They went
   through neither the test gate nor the coverage gate, and cannot be rebuilt from
   a tag. Nothing in the pipeline objected.
2. **`dotnet nuget push --skip-duplicate` turned republication into a silent
   no-op.** A version could be pushed twice — potentially with different content
   after a moved tag — and the run stayed green either way.
3. **A backup push to GitHub Packages ran with `continue-on-error: true`**, which
   is a failure mode nobody watches, and gave the same `PackageId`s a second
   (later, with the [SCOJH domain repos](0006-multi-repo-adapter-split.md), a third)
   publication source.

For a framework whose packages fan out to every consumer, "where did this binary
come from and can we rebuild it" is not a nice-to-have — it is the supply-chain
property the zero-dependency Core ([ADR-0003](0003-zero-dep-core.md)) exists to
protect from the other side.

## Decision

Two verification scripts run **between `pack` and `push`** in
`.github/workflows/release.yml`, so a failure means nothing is published:

- **`scripts/verify-package-provenance.sh`** — for every artifact, the embedded
  nuspec must carry a `repository/@commit` attribute (an *absent* attribute is a
  failure, so a SourceLink regression cannot silently disarm the gate), the commit
  must be the one this run is building, it must exist in this repository, and it
  must be **reachable from at least one tag**. Shallow clones are refused because
  they cannot answer the question.
- **`scripts/verify-package-not-published.sh`** — replaces `--skip-duplicate`. It
  asks the nuget.org flat-container index whether the version already exists: a
  collision means a tag was moved and is a red run. An unreachable feed is *also* a
  failure — *"not knowing is not permission"*.

Additionally, packages are pushed to **one feed only** (nuget.org). The GitHub
Packages backup push was removed: a second feed under `continue-on-error` is a
silent failure mode and an extra provenance for the same package identity.

Both scripts read id, version, and commit from the embedded nuspec rather than the
file name, and report every offending package before exiting.

## Consequences

### Positive

- Every package published from now on is rebuildable from a tag and passed the tag's
  test and coverage gates — by construction, not by discipline.
- A moved tag, a re-run of an old job, or a copy-paste release cannot silently
  overwrite a published version.
- One feed means one provenance story; consumers and security scanners have a
  single source of truth for `Compendium.*` binaries.
- The gates are plain bash against the nuspec and the git history — no service
  dependency, auditable in five minutes.

### Negative / Trade-offs

- **The gates do not clean up the past.** The two orphaned `1.0.4` packages remain
  on nuget.org, still needing republication from a train tag and deprecation of the
  orphaned versions. Tracked as open debt in
  [release trains — "Still open"](../operations/release-trains.md).
- **The provenance check leans on the .NET SDK's implicit SourceLink** to stamp
  `repository/@commit`. The repository declares no explicit SourceLink package, no
  `ContinuousIntegrationBuild`, and publishes no symbol packages — the gate detects
  a missing stamp, but the stamping itself is an implicit behaviour we do not pin.
- **No fallback feed.** If nuget.org is down, releases wait. Accepted: an
  unavailable feed delaying a release is cheaper than a second feed nobody audits.
- Failed runs require a human to distinguish "moved tag" (bad) from "genuine retry
  after a network flake mid-push" (annoying but safe) — the script cannot tell them
  apart and fails closed.

## Alternatives considered

- **Keep `--skip-duplicate`.** Rejected — it converts the most dangerous event a
  package feed can see (same version, possibly different content) into a green run.
- **Sign packages / adopt NuGet author signing** instead of provenance-by-commit.
  Not adopted (yet) — signing proves *who* published, not *from which tested
  commit*; the gates answer the question we actually had. Signing remains
  compatible with this decision and may come later.
- **Publish to GitHub Packages as a mirror.** Rejected — a mirror that can fail
  silently is worse than no mirror, and a second feed doubles the deprecation and
  cleanup surface (the `Compendium.Abstractions.Storage` dual-feed incident in the
  [CHANGELOG](../../CHANGELOG.md) is exactly this failure).
- **Trust the release workflow's trigger** (tags only) as sufficient provenance.
  Rejected — the two orphaned `1.0.4` packages prove that path-of-least-resistance
  publishing happens; the gate makes the invariant hold even when humans improvise.
