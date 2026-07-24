# Compendium.Abstractions.Git

Provider-agnostic git-server ports for the Compendium Framework.

A platform that drives a git server (create repositories from templates, configure CI
secrets/variables, trigger pipelines, manage deployment environments, branch policies,
teams, and webhooks) programs against the `IGitServer` facade and its concern-scoped
sub-ports. Concrete providers (GitHub, GitLab, Gitea/Forgejo, Azure DevOps, …) are
supplied by `Compendium.Adapters.*` packages.

## Design

- **`IGitServer`** — facade carrying the `Provider` discriminator, the declarative
  `GitServerCapabilities`, and the sub-ports. Consumers resolve `IEnumerable<IGitServer>`
  and dispatch on `Provider`.
- **`GitConnection` / `GitCredential`** — every call takes an explicit connection.
  Credentials are a closed union: platform app installation (GitHub App), durable
  service-account token (GitLab group token, Gitea bot PAT), one-shot personal access
  token, or OAuth user token. Token-bearing records redact in `ToString()`.
- **`GitServerCapabilities`** — adapters declare what they support (`None`/`Partial`/`Full`
  per `GitCapability`). Unsupported operations fail with `Git.CapabilityNotSupported`
  (`Result` failure, never an exception). Each adapter ships a `CAPABILITIES.md` matrix.
- **`GitWebhookEvent`** — neutral inbound-event union produced by `IGitWebhookIngestor`
  from a raw, signature-verified delivery.
- All operations return `Result`/`Result<T>` from `Compendium.Core.Results`.

## Testing

`Compendium.Testing` provides `InMemoryGitServer` (full-fidelity fake) and
`GitServerContractTests<TFixture>` (the behavioral contract every adapter must pass).
This package also ships `NullGitServer`, a fail-fast stub for unconfigured hosts.
