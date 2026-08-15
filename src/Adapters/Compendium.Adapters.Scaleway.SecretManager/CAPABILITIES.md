# Scaleway Secret Manager — capability matrix

Adapter: `Compendium.Adapters.Scaleway.SecretManager` · Provider id: `scaleway`
API: Secret Manager regional `v1beta1` (`fr-par`, `nl-ams`, `pl-waw`)

| Capability | Level | Notes |
|---|---|---|
| `ImmutableVersions` | **Full** | Versions are immutable; revisions are 1-based, monotonic, never reused. `(secretId, revision)` is a stable rollback anchor. |
| `VersionEnableDisable` | **Full** | Kill-switch per revision. The adapter makes enable/disable idempotent (a no-op transition reads as success). |
| `VersionDestroy` | **Full** | Permanent destruction; the revision number stays reserved. |
| `PathHierarchy` | **Full** | Secrets live in `/`-separated folders. Prefix listing is filtered adapter-side over the tenancy's secrets (paged at 100/page), keeping prefix semantics uniform across providers. |
| `Tags` | **Partial** | The provider models tags as a list of strings; the neutral key/value tags are encoded as `key:value` entries (colon-less tags decode to an empty value). Tag filters apply adapter-side. |
| `LargePayload` | **None** | A version payload is limited to 64 KiB; larger payloads fail fast with `SecretVault.PayloadTooLarge` before any network call. |
| `EphemeralSecrets` | **None** | Exists provider-side (ephemeral policies); not exposed through the v1 ports. |
| `ServerSideRotation` | **None** | Not provided by Secret Manager. |

## Operational notes

- **Authentication**: IAM API secret key sent as `X-Auth-Token`, supplied per
  call via `SecretVaultConnection.Credential` (`ApiToken`). The adapter holds
  no credential state; options carry only base URL, default region, default
  project.
- **Tenancy**: `SecretVaultConnection.Tenancy` = the Scaleway project id
  (falls back to `DefaultProjectId`).
- **Retries**: requests are sent exactly once. Version writes are not
  idempotent; retry/deduplication policy belongs to the caller (deduplicate by
  content hash before writing).
- **Throttling**: HTTP 429 maps to `SecretVault.Throttled` carrying
  `retryAfterSeconds` when the provider supplies `Retry-After`.
- **Disabled-version access**: the provider's 4xx on access is disambiguated
  by reading the version metadata, so consumers always receive the precise
  `VersionDisabled` vs `VersionNotFound` vs `SecretNotFound` code.
- **Billing**: Scaleway bills stored versions (enabled or disabled) and API
  requests. Cost levers for callers: deduplicate identical payloads before
  `AddAsync`, cache `AccessAsync` results per revision (immutable → cacheable
  indefinitely), destroy old revisions beyond a retention window — but never
  destroy a revision still referenced as a rollback target.
