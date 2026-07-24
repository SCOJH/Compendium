# Compendium.Adapters.GitHub

A GitHub adapter for the Compendium Framework: a concrete `IGitServer` implementing
the [`Compendium.Abstractions.Git`](../../Abstractions/Compendium.Abstractions.Git) ports
against **github.com** and **GitHub Enterprise Server**.

## What it does

- **GitHub App auth** — signs RS256 App JWTs and mints short-lived installation
  access tokens, caching them per installation and refreshing before expiry.
  Also passes through caller-supplied service-account, personal-access, and OAuth
  tokens.
- **Repositories** — create from a template, read metadata/contents/commits/branches,
  create tags, publish releases.
- **CI configuration** — repository, organization, and deployment-environment
  Actions **secrets** (libsodium sealed-box encrypted) and **variables**.
- **Pipelines** — trigger workflows (`workflow_dispatch`) and read run status.
- **Environments** — create/update/list deployment environments.
- **Branch policies** — GitHub **repository rulesets** (not legacy branch protection).
- **Access control** — teams, team membership, team/user repository roles.
- **Webhooks** — manage outbound subscriptions; verify and parse inbound deliveries
  (fail-closed HMAC-SHA256) into neutral events.

See [CAPABILITIES.md](CAPABILITIES.md) for the full support matrix and its limitations.

## Registration

```csharp
services.AddGitHubGitServer(options =>
{
    options.DefaultApp.AppId = configuration["GitHub:AppId"]!;
    options.DefaultApp.AppSlug = configuration["GitHub:AppSlug"]!;
    options.DefaultApp.PrivateKeyPem = secretStore["GitHub:PrivateKey"];
    options.DefaultApp.WebhookSecret = secretStore["GitHub:WebhookSecret"];
});
```

Consumers resolve `IEnumerable<IGitServer>` and dispatch on `Provider == "github"`,
or resolve a single concern-scoped port (`IGitRepositoryService`, `IGitCredentialBroker`, …).
Every operation takes a `GitConnection` explicitly — the adapter is a stateless
singleton and one instance serves any number of tenants.

## Connections

| Credential | Auth |
|---|---|
| `GitCredential.AppInstallation` | Mints an installation token from the configured App (`AppKey` selects a registration). |
| `GitCredential.ServiceAccountToken` / `PersonalAccessToken` | Passed through as a bearer token. |
| `GitCredential.OAuthAccessToken` | Passed through (8-hour reported lifetime). |

Set `GitConnection.ServerUrl` to a GitHub Enterprise Server API base
(`https://ghes.example.com/api/v3`) to target GHES; leave it `null` for github.com.

## License

MIT. See [LICENSE](../../../LICENSE).
