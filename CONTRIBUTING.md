# Contributing to Compendium

Thanks for your interest in Compendium! This document explains how to work on the framework.

## Getting started

### Prerequisites

- .NET 9 SDK (see `global.json` for the exact version).
- Docker (optional, needed to run integration tests with TestContainers).
- Git.

### Build and test

```bash
git clone https://github.com/SCOJH/Compendium.git
cd compendium
dotnet restore Compendium.sln
dotnet build Compendium.sln --configuration Release
dotnet test Compendium.sln --configuration Release --filter "Category!=Integration&Category!=Load"
```

Integration tests require Docker for PostgreSQL and Redis containers.

## Repository layout

```
src/
  Core/                 Compendium.Core — zero-dependency DDD primitives
  Abstractions/         Ports (Identity, Email, Billing, AI, shared)
  Application/          CQRS dispatchers and handlers
  Infrastructure/       Resilience, telemetry, serialization primitives
  Multitenancy/         Tenant context and scoping
  Adapters/             Concrete implementations (PostgreSQL, Redis, Zitadel, …)
  Extensions/           Composition helpers
  Testing/              Test utilities consumed by downstream projects
tests/
  Unit/                 Fast unit tests
  Integration/          TestContainers-backed integration tests
  Architecture/         NetArchTest rules
  LoadTests/            NBomber performance scenarios
```

## Coding conventions

- **Result pattern over exceptions** for expected failures (`Result<T>` and `Error`).
- **Immutable records** for domain events and DTOs.
- **Hexagonal architecture**: ports live in `Compendium.Abstractions.*`, adapters live in `Compendium.Adapters.*`.
- **Zero-dep Core**: `Compendium.Core` must compile with only the .NET BCL — no external NuGet references.
- File header: MIT copyright, matches `LICENSE`.
- Target: `.NET 9`, C# 13, nullable enabled.

Run `dotnet format` before submitting a PR.

## Submitting changes

1. Fork and create a feature branch from `main`.
2. Add tests that demonstrate the change.
3. Ensure `dotnet build -c Release` and `dotnet test -c Release` pass locally.
4. Open a pull request against `main` with a clear description.

PR reviewers will run the full CI suite (build + unit + integration + architecture tests).

## Versioning and releases

Compendium uses [Semantic Versioning](https://semver.org/) with [MinVer](https://github.com/adamralph/minver) for git-tag-driven versioning.

- Preview releases: `v1.0.0-preview.N` (tag prefix `v`).
- Stable releases: `v1.0.0`, `v1.1.0`, …

All 19 packages share the same version (synchronized release cadence).

Releases are automated via GitHub Actions on tag push: see `.github/workflows/release.yml`.

## Reporting bugs / feature requests

Open an issue at [SCOJH/Compendium/issues](https://github.com/SCOJH/Compendium/issues).

For security vulnerabilities, please use private disclosure via GitHub Security Advisories.

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
