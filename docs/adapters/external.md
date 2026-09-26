# External adapters

Heavy adapters that ship from their own repositories per [ADR-0006](../adr/0006-multi-repo-adapter-split.md). Each is released independently from the framework on its own version cadence.

| Adapter | Repository | NuGet package | Description |
|---|---|---|---|
| Stripe | [sassy-solutions/compendium-adapter-stripe](https://github.com/sassy-solutions/compendium-adapter-stripe) | [`Compendium.Adapters.Stripe`](https://www.nuget.org/packages/Compendium.Adapters.Stripe) | Billing: subscriptions, checkouts, customer portal, HMAC-signed webhooks |
| LemonSqueezy | [sassy-solutions/compendium-adapter-lemonsqueezy](https://github.com/sassy-solutions/compendium-adapter-lemonsqueezy) | [`Compendium.Adapters.LemonSqueezy`](https://www.nuget.org/packages/Compendium.Adapters.LemonSqueezy) | Billing: subscriptions, checkouts, license keys, webhooks (JSON:API) |
| Zitadel | [sassy-solutions/compendium-adapter-zitadel](https://github.com/sassy-solutions/compendium-adapter-zitadel) | [`Compendium.Adapters.Zitadel`](https://www.nuget.org/packages/Compendium.Adapters.Zitadel) | Identity: users, tokens, organizations (multi-tenant) |
| Listmonk | [sassy-solutions/compendium-adapter-listmonk](https://github.com/sassy-solutions/compendium-adapter-listmonk) | [`Compendium.Adapters.Listmonk`](https://www.nuget.org/packages/Compendium.Adapters.Listmonk) | Email: transactional, newsletters, subscribers |
| OpenRouter | [sassy-solutions/compendium-adapter-openrouter](https://github.com/sassy-solutions/compendium-adapter-openrouter) | [`Compendium.Adapters.OpenRouter`](https://www.nuget.org/packages/Compendium.Adapters.OpenRouter) | AI: access to 100+ LLMs through one OpenAI-compatible API |
| PostgreSQL | [sassy-solutions/compendium-adapter-postgresql](https://github.com/sassy-solutions/compendium-adapter-postgresql) | [`Compendium.Adapters.PostgreSQL`](https://www.nuget.org/packages/Compendium.Adapters.PostgreSQL) | Event store, projection store, idempotency, row-level tenant isolation |
| Redis | [sassy-solutions/compendium-adapter-redis](https://github.com/sassy-solutions/compendium-adapter-redis) | [`Compendium.Adapters.Redis`](https://www.nuget.org/packages/Compendium.Adapters.Redis) | Distributed cache, idempotency store, locking |

## Writing a new adapter

Generate a new repository from the [`template-compendium-adapter-dotnet`](https://github.com/sassy-solutions/template-compendium-adapter-dotnet) GitHub template. The template ships:

- xUnit + AwesomeAssertions + NSubstitute + AutoFixture + Bogus test stack
- 90 % line-coverage CI gate
- MinVer-driven versioning from git tags
- Tag-triggered NuGet publishing (nuget.org + GitHub Packages)
- Renovate + Dependabot configs
- `compendium-test-author` skill and `/tests` / `/coverage` slash commands

See the template README for the full bootstrap procedure.

## In-framework adapters

The only adapter that remains in the framework monorepo:

- [`Compendium.Adapters.AspNetCore`](aspnetcore.md) — thin glue (middleware, ProblemDetails mappers, DI helpers). No external SDK; evolves in lock-step with `Compendium.Application`.
