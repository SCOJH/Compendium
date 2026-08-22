# Release trains

## Why there are trains

Until now a single tag prefix drove every package. Tagging `v1.0.5-preview.4`
packed all thirty-five projects under `src/` and pushed all thirty-five to
nuget.org. A one-line fix in `Compendium.Abstractions.Geo` shipped as a new
release of `Compendium.Core`, `Compendium.Testing` and thirty-two others, so a
version number no longer told a consumer whether anything relevant to them had
changed. Downstream this showed up as a rule that everything moves together or
nothing moves.

A **train** is a set of packages that share a tag prefix and therefore share a
version. There are twenty-six of them for thirty-five packages.

The split is read off the `ProjectReference` graph, not chosen by taste:

- a project that **another project in this repository depends on** joins the
  `core-v` train. Those are never shipped separately, and a core package
  published with a dependency on a core version that was never tagged fails at
  the consumer's `restore`, not here;
- a project **nothing here depends on** gets a train of its own.

| Train | Packages |
|---|---|
| `core-v` | `Compendium.Core`, `Compendium.Abstractions`, `Compendium.Abstractions.AI`, `Compendium.Abstractions.Caching`, `Compendium.Abstractions.CodingAgents`, `Compendium.Abstractions.Git`, `Compendium.Abstractions.Secrets`, `Compendium.Application`, `Compendium.Infrastructure`, `Compendium.Multitenancy` |
| `<name>-v` | one per remaining abstraction: `analytics-v`, `authorization-v`, `billing-v`, `crm-v`, `documents-v`, `email-v`, `featureflags-v`, `geo-v`, `identity-v`, `jobs-v`, `messaging-v`, `notifications-v`, `realtime-v`, `search-v`, `speech-v`, `translation-v`, `vectorstore-v`, `webhooks-v` |
| `adapter-<name>-v` | one per in-tree adapter: `adapter-aspnetcore-v`, `adapter-claudecode-v`, `adapter-github-v`, `adapter-kubernetes-sandbox-v`, `adapter-scaleway-secretmanager-v`, `adapter-shared-v` |
| `testing-v` | `Compendium.Testing` |

The train lives in each `.csproj`, next to its `<PackageId>`:

```xml
<PackageId>Compendium.Abstractions.Geo</PackageId>
<MinVerTagPrefix>geo-v</MinVerTagPrefix>
```

`Directory.Build.props` deliberately declares no prefix. A project that forgets
one would not fail to build — MinVer would hand it `0.0.0-alpha.0.N`, below
everything already published, and the release job would push that. That is what
`scripts/verify-package-trains.sh` exists to stop.

To see the current mapping, or the projects a given tag would release:

```bash
scripts/verify-package-trains.sh
scripts/select-train-projects.sh geo-v1.0.6          # -> 1 project
scripts/select-train-projects.sh core-v1.0.6         # -> 10 projects
```

## Releasing one train

```bash
git tag geo-v1.0.6
git push origin geo-v1.0.6
```

That packs and publishes `Compendium.Abstractions.Geo` alone. Nothing else moves
and nothing else changes version.

Adding a package means adding a `<MinVerTagPrefix>` and seeding its first tag.
Nothing else: the release job discovers trains from the `.csproj` files.

## The two gates

Two packages are public today whose nuspec records a commit that is on no tag.
Neither went through the test gate or the coverage gate, and neither can be
rebuilt from a tag. Nothing in the pipeline said no. Two scripts now do, both
between `Pack` and `Push`, so a failure means nothing is published:

- **`scripts/verify-package-provenance.sh artifacts/`** — every artifact's
  nuspec must carry a `repository` commit (an absent attribute is a failure, so
  a SourceLink regression cannot silently disarm the gate), that commit must be
  the one this run is building, it must exist in this repository, and it must be
  reachable from at least one tag.
- **`scripts/verify-package-not-published.sh artifacts/`** — replaces
  `--skip-duplicate`. That flag turned a republication into a silent no-op: a
  version could be pushed twice with different content and the run stayed green.
  The script asks the flat-container index whether the version already exists. A
  collision means a tag was moved. An unreachable feed is also a failure — not
  knowing is not permission.

Both take a directory or individual packages, read id, version and commit from
the embedded nuspec rather than from the file name, and report every offending
package before exiting.

## Seeding the trains — one-time

Splitting one train into many leaves every new train without a tag, so every
package would drop to `0.0.0-alpha.0.N`. Each train needs a starting tag on the
commit the last global tag pointed at.

```bash
scripts/bootstrap-train-tags.sh                 # dry run, prints the commands
scripts/bootstrap-train-tags.sh --create        # creates them locally
git push origin core-v1.0.5-preview.4           # one at a time — this publishes
```

The script defaults to the newest `v*` tag as the starting point; `--from <tag>`
overrides it. It creates nothing without `--create` and never pushes: pushing a
train tag triggers its release.

Do this **after** `.github/workflows/release.yml` has been switched to the
trigger below. A train tag pushed before that either triggers nothing or
triggers the old all-packages job.

## The release workflow

`.github/workflows/release.yml` has to change with this, and that change is not
in the same commit as the rest — it is outside the write perimeter of the agent
that prepared these scripts. Below is the file to apply, in full. The behaviour
change is in four places: the trigger, the train guard, the pack step, and the
two gates before two pushes that no longer carry `--skip-duplicate`.

```yaml
name: Release

on:
  push:
    tags: ["*-v*"]

permissions:
  contents: write
  packages: write

jobs:
  pack-publish:
    runs-on: ubuntu-latest
    timeout-minutes: 30
    steps:
      - uses: actions/checkout@v6
        with:
          fetch-depth: 0 # MinVer needs full history; the provenance gate needs the tags

      - uses: actions/setup-dotnet@v5
        with:
          dotnet-version: "9.0.x"

      - name: Restore
        run: dotnet restore Compendium.sln

      - name: Build
        run: dotnet build Compendium.sln -c Release --no-restore

      - name: Test (unit only)
        run: |
          dotnet test Compendium.sln -c Release --no-build \
            --filter "FullyQualifiedName!~IntegrationTests&FullyQualifiedName!~LoadTests"

      - name: Every packable project declares a train
        run: bash scripts/verify-package-trains.sh

      - name: Pack the train this tag releases
        run: |
          mkdir -p artifacts
          projects=$(bash scripts/select-train-projects.sh "$GITHUB_REF_NAME")
          mapfile -t project_list <<< "$projects"
          for csproj in "${project_list[@]}"; do
            echo "Packing $(basename "$csproj" .csproj)"
            dotnet pack "$csproj" -c Release --no-build -o artifacts/
          done
          echo "Packed $(ls artifacts/*.nupkg 2>/dev/null | wc -l) package(s) for $GITHUB_REF_NAME"

      - name: Verify every package comes from a tagged commit
        run: bash scripts/verify-package-provenance.sh artifacts/

      - name: Verify no version is being republished
        run: bash scripts/verify-package-not-published.sh artifacts/

      - name: Push to nuget.org
        run: |
          dotnet nuget push "artifacts/*.nupkg" \
            --api-key ${{ secrets.NUGET_API_KEY }} \
            --source https://api.nuget.org/v3/index.json

      - name: Push to GitHub Packages (backup feed)
        continue-on-error: true
        run: |
          dotnet nuget push "artifacts/*.nupkg" \
            --api-key ${{ secrets.GITHUB_TOKEN }} \
            --source https://nuget.pkg.github.com/sassy-solutions/index.json

      - name: Create GitHub Release
        uses: softprops/action-gh-release@b4309332981a82ec1c5618f44dd2e27cc8bfbfda  # v3.0.0
        with:
          files: artifacts/*.nupkg
          generate_release_notes: true
          prerelease: ${{ contains(github.ref, '-preview') || contains(github.ref, '-rc') || contains(github.ref, '-alpha') || contains(github.ref, '-beta') }}
```

The old `tags: ["v*"]` trigger does not match a train tag, and `"*-v*"` does not
match the old `v1.0.5-preview.4`. Old tags therefore stop triggering anything,
which is what we want: re-pushing one of them used to republish thirty-five
packages.

`scripts/verify-package-trains.sh` is also worth a step in
`.github/workflows/ci.yml`, next to the coverage gate, so a project added
without a train fails on the pull request rather than at release time.

### Checking the change before it publishes anything

On a throwaway branch, with the workflow applied and the trains seeded:

```bash
git tag geo-v0.0.1-test && git push origin geo-v0.0.1-test
```

The run should pack exactly one package and stop at the provenance gate or
publish a single test version, depending on how far you want to take it. Delete
the tag afterwards. `core-v0.0.1-test` should pack exactly ten.

## Still open

Three things this repository cannot close on its own. They are tracked on the
ticket that produced this document.

- **`Compendium.Abstractions.Geo 1.0.4` and `Compendium.Abstractions.Messaging
  1.0.4`** are published from commits `ce2b763` and `5b8d420`. Both are
  reachable from `main` today but neither is on any tag, and the `v1.0.4` tag
  contains neither project. They need republishing from a train tag, and the
  `1.0.4` versions deprecating on nuget.org towards the replacement. The gate
  above makes a repeat impossible; it does not fix what is already out.
- **`Compendium.Abstractions.Storage 1.0.x`** stays listed and restorable on
  nuget.org — an existing consumer must not break — but should be marked
  deprecated there, with the `SCOJH/storage` package as the alternative. This
  repository no longer builds or publishes that name.
- **Which organisation owns this repository.** `git remote` and
  `Directory.Build.props` say `SCOJH/Compendium`; the published `1.0.3` nuspec
  and the adapter split ADR say `sassy-solutions/compendium`. The gates do not
  care, but a wrong `RepositoryUrl` in a published nuspec is a traceability
  defect of exactly the kind this work is closing, and the deprecation steps
  need the right account.
