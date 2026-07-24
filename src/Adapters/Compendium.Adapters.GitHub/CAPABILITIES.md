# Compendium.Adapters.GitHub — capability matrix

Provider discriminator: **`github`**. Targets github.com and GitHub Enterprise
Server (set `GitConnection.ServerUrl` to the GHES API base).

Levels: **Full** — supported without caveats · **Partial** — supported with the
noted limitation · **None** — not reachable by this adapter (with the reason).

| Capability | Level | GitHub mechanism | Limitations |
|---|---|---|---|
| `RepositoryFromTemplate` | Full | `POST /repos/{template_owner}/{template_repo}/generate` | — |
| `RepositoryManagement` | Full | `GET /repos/*`, contents, commits, branches | — |
| `TagsAndReleases` | Full | `POST /repos/{o}/{r}/git/refs` (tags), `POST .../releases` | Tag creation makes a lightweight git ref; a release is a separate `CreateReleaseAsync` call. |
| `CiSecrets` | Full | Actions repo secrets, libsodium sealed box | Write-only (values never read back). |
| `CiVariables` | Full | Actions repo variables | — |
| `NamespaceSecrets` | Full | Actions org secrets (`visibility: all`), sealed box | Org variables/secrets default to `all` visibility. |
| `PipelineTrigger` | **Partial** | `POST /repos/{o}/{r}/actions/workflows/{wf}/dispatches` | `workflow_dispatch` returns **204 with no run id** — `GitPipelineRunHandle.RunId` is `null`; correlate the created run via `ListRunsAsync`. |
| `PipelineStatus` | Full | `GET .../actions/runs/{id}`, `.../runs` | Status/conclusion mapped onto `GitPipelineStatus` (unmapped states → `Unknown`). |
| `DeploymentEnvironments` | Full | `PUT/GET/DELETE /repos/{o}/{r}/environments/{name}` | `PUT` is create-or-update (idempotent). |
| `EnvironmentSecrets` | Full | Environment secrets keyed by numeric repository id, sealed box | The repository id is resolved with an extra `GET /repos/{o}/{r}`. |
| `BranchPolicies` | Full | Repository **rulesets** (`/repos/{o}/{r}/rulesets`) | Neutral policy → one ruleset named `compendium:{pattern}`; not classic branch protection. Admin bypass added when `EnforceForAdmins` is false. |
| `TeamsAndPermissions` | Full | Teams, team membership, `AddOrUpdateTeamRepositoryPermissions`, collaborators | Organization namespaces only; team ops on a user account fail as a mapped provider error. Roles map to `pull/triage/push/maintain/admin`. |
| `WebhookManagement` | Full | Repository and organization hooks | Matched by delivery URL for idempotent ensure. |
| `WebhookIngestion` | Full | `X-Hub-Signature-256` HMAC-SHA256 over the raw body | Fail-closed: missing/invalid signature → `Git.WebhookSignatureInvalid`. Consumed events: `push` (incl. tag pushes), `pull_request`, `workflow_run` (completed), `installation`, `installation_repositories`; everything else → `Unsupported`. |
| `NamespaceProvisioning` | **None** | — | github.com organization creation requires an **enterprise-owner user token**; unreachable with App credentials. `CreateNamespaceAsync` always returns `Git.CapabilityNotSupported`. |
| `AppInstallationAuth` | Full | App JWT (RS256) → installation access token | Tokens cached per (app, installation), refreshed 60 s before expiry; a stale-JWT 401 on mint is retried once. |
| `ServiceAccountAuth` | Full | Bearer pass-through of a durable token | Reported with a far-future expiry. |
| `OAuthUserAuth` | Full | Bearer pass-through of an OAuth user token | Reported with an 8-hour expiry. |
| `ScopedTokenMinting` | Full | `repositories` + `permissions` on the create-installation-token request | Scoped tokens bypass the token cache and are minted fresh each call. |

## Token scoping note

The neutral `GitAccessTokenScope` carries repository **references** (namespace/name).
GitHub's create-installation-access-token endpoint accepts either numeric
`repository_ids` or a `repositories` array of names; the adapter sends the names,
avoiding an id lookup. At most 500 repositories may be listed.
