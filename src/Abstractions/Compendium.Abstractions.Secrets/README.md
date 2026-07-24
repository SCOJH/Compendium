# Compendium.Abstractions.Secrets

Provider-agnostic secret-vault abstractions: the `ISecretVault` facade with two
concern-scoped ports (`ISecretContainerService`, `ISecretVersionService`), a
neutral connection/credential model, and declarative per-adapter capabilities.

## Design contract

- **Versions are immutable and addressed by explicit revision.** The port has
  no "latest" concept: which revision is *current* is the caller's metadata.
  This makes `(secretId, revision)` a stable value reference — history stays
  trustworthy and rollback is a re-pointing of references, never a provider-side
  mutation.
- **The provider-side `SecretId` is the identity.** Names and paths are
  organizational/diagnostic; consumers persist ids.
- **No secret ever stringifies.** `SecretMaterial` and token credentials redact
  in `ToString()`.
- **Errors are uniform** (`SecretVault.*` codes via the Result pattern — never
  exceptions for control flow), so consumers handle Scaleway, Vault, or an
  in-house store identically.
- **Capabilities are declarative.** Adapters declare `ImmutableVersions`,
  `VersionEnableDisable`, `VersionDestroy`, `PathHierarchy`, `Tags`,
  `LargePayload`, … and fail undeclared operations with
  `SecretVault.CapabilityNotSupported`; each adapter documents its matrix in
  `CAPABILITIES.md`.

## Testing

`Compendium.Testing` ships `InMemorySecretVault` (a full-fidelity fake) and
`SecretVaultContractTests`, the behavioral contract suite every adapter must
pass — inheriting it in an adapter's test suite is what makes swapping vault
backends safe.

## Adapters

- `Compendium.Adapters.Scaleway.SecretManager` — Scaleway Secret Manager
  (regional, immutable versions, tags/paths).
- `NullSecretVault` (in this package) — fail-fast stub for unconfigured hosts.
