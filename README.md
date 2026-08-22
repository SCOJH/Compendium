# Compendium

> A pragmatic .NET framework for building event-sourced, multi-tenant SaaS applications.

[![CI](https://github.com/SCOJH/Compendium/actions/workflows/ci.yml/badge.svg)](https://github.com/SCOJH/Compendium/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Compendium.Core.svg)](https://www.nuget.org/packages/Compendium.Core/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4.svg)](https://dotnet.microsoft.com/)

Compendium is the framework that powers [Nexus](https://sassy.solutions), Sassy Solutions' multi-tenant platform engineering product. It distills years of building event-sourced SaaS into a small set of focused packages: DDD primitives, CQRS handlers, an event store, multi-tenancy, and ready-to-use adapters for PostgreSQL, Redis, Zitadel, and more.

## Why Compendium?

- **Zero-dependency Core** — Pure DDD primitives (`AggregateRoot<TId>`, `ValueObject`, `Result<T>`, `Error`) with no external dependencies beyond the .NET BCL.
- **CQRS + Event Sourcing built-in** — Command/query dispatchers, event store interfaces, and a PostgreSQL adapter wired out of the box.
- **Sagas, two flavors** — `ProcessManager<TState>` for DDD-style orchestration sagas and `IHandle<TEvent>` for event-driven choreography sagas, each clearly named so you don't have to guess which pattern you're using. See [docs/sagas.md](docs/sagas.md).
- **Multi-tenancy native** — Tenant context, resolution, and scoping baked into the primitives — not bolted on.
- **Result pattern everywhere** — No control-flow exceptions. Every fallible operation returns `Result<T>` with structured `Error` values.
- **Modular adapters** — Pick only what you need: twenty-two production adapters across persistence (Postgres, Redis, pgvector, Qdrant, Pinecone, S3-compatible), identity (Zitadel), billing (Stripe, LemonSqueezy), email (Listmonk), and AI (OpenRouter, OpenAI, Anthropic, Gemini, Mistral, DeepSeek, Mercury, Hugging Face, Azure OpenAI, AWS Bedrock, LiteLLM, Ollama). See [Adapters](#adapters). Each ships its own repo, NuGet package, and release cadence per [ADR-0006](docs/adr/0006-multi-repo-adapter-split.md).
- **Battle-tested in production** — Powers Nexus, a multi-tenant platform engineering product.

## Adapters

Each public adapter lives in its own repository under `sassy-solutions/compendium-adapter-*` and is released independently per [ADR-0006](docs/adr/0006-multi-repo-adapter-split.md). The framework defines the ports (`IEventStore`, `IAIProvider`, `IVectorStore`, `IBillingProvider`, …); adapters provide concrete implementations.

### Official adapters

| Domain | Adapter | Repo | NuGet |
|---|---|---|---|
| Persistence — event store | PostgreSQL | [`compendium-adapter-postgresql`](https://github.com/sassy-solutions/compendium-adapter-postgresql) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Adapters.PostgreSQL.svg)](https://www.nuget.org/packages/Compendium.Adapters.PostgreSQL/) |
| Persistence — cache & idempotency | Redis | [`compendium-adapter-redis`](https://github.com/sassy-solutions/compendium-adapter-redis) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Adapters.Redis.svg)](https://www.nuget.org/packages/Compendium.Adapters.Redis/) |
| Persistence — vector store | pgvector | [`compendium-adapter-pgvector`](https://github.com/sassy-solutions/compendium-adapter-pgvector) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Adapters.Pgvector.svg)](https://www.nuget.org/packages/Compendium.Adapters.Pgvector/) |
| Persistence — vector store | Qdrant | [`compendium-adapter-qdrant`](https://github.com/sassy-solutions/compendium-adapter-qdrant) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Adapters.Qdrant.svg)](https://www.nuget.org/packages/Compendium.Adapters.Qdrant/) |
| Persistence — vector store | Pinecone (managed) | [`compendium-adapter-pinecone`](https://github.com/sassy-solutions/compendium-adapter-pinecone) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Adapters.Pinecone.svg)](https://www.nuget.org/packages/Compendium.Adapters.Pinecone/) |
| Persistence — object storage | S3-compatible (AWS / R2 / MinIO / B2 / Wasabi) | [`compendium-adapter-s3`](https://github.com/sassy-solutions/compendium-adapter-s3) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Adapters.S3.svg)](https://www.nuget.org/packages/Compendium.Adapters.S3/) |
| Identity | Zitadel | [`compendium-adapter-zitadel`](https://github.com/sassy-solutions/compendium-adapter-zitadel) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Adapters.Zitadel.svg)](https://www.nuget.org/packages/Compendium.Adapters.Zitadel/) |
| Billing | Stripe | [`compendium-adapter-stripe`](https://github.com/sassy-solutions/compendium-adapter-stripe) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Adapters.Stripe.svg)](https://www.nuget.org/packages/Compendium.Adapters.Stripe/) |
| Billing | LemonSqueezy | [`compendium-adapter-lemonsqueezy`](https://github.com/sassy-solutions/compendium-adapter-lemonsqueezy) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Adapters.LemonSqueezy.svg)](https://www.nuget.org/packages/Compendium.Adapters.LemonSqueezy/) |
| Email | Listmonk | [`compendium-adapter-listmonk`](https://github.com/sassy-solutions/compendium-adapter-listmonk) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Adapters.Listmonk.svg)](https://www.nuget.org/packages/Compendium.Adapters.Listmonk/) |
| AI — gateway | OpenRouter | [`compendium-adapter-openrouter`](https://github.com/sassy-solutions/compendium-adapter-openrouter) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Adapters.OpenRouter.svg)](https://www.nuget.org/packages/Compendium.Adapters.OpenRouter/) |
| AI — direct provider | OpenAI | [`compendium-adapter-openai`](https://github.com/sassy-solutions/compendium-adapter-openai) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Adapters.OpenAI.svg)](https://www.nuget.org/packages/Compendium.Adapters.OpenAI/) |
| AI — direct provider | Anthropic | [`compendium-adapter-anthropic`](https://github.com/sassy-solutions/compendium-adapter-anthropic) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Adapters.Anthropic.svg)](https://www.nuget.org/packages/Compendium.Adapters.Anthropic/) |
| AI — direct provider | Gemini (Google) | [`compendium-adapter-gemini`](https://github.com/sassy-solutions/compendium-adapter-gemini) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Adapters.Gemini.svg)](https://www.nuget.org/packages/Compendium.Adapters.Gemini/) |
| AI — direct provider (EU) | Mistral AI (Large / Codestral / Pixtral) | [`compendium-adapter-mistral`](https://github.com/sassy-solutions/compendium-adapter-mistral) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Adapters.Mistral.svg)](https://www.nuget.org/packages/Compendium.Adapters.Mistral/) |
| AI — direct provider | DeepSeek (V3 / R1 reasoning) | [`compendium-adapter-deepseek`](https://github.com/sassy-solutions/compendium-adapter-deepseek) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Adapters.DeepSeek.svg)](https://www.nuget.org/packages/Compendium.Adapters.DeepSeek/) |
| AI — direct provider | Mercury (Inception Labs, diffusion) | [`compendium-adapter-mercury`](https://github.com/sassy-solutions/compendium-adapter-mercury) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Adapters.Mercury.svg)](https://www.nuget.org/packages/Compendium.Adapters.Mercury/) |
| AI — open models hosting | Hugging Face Inference Endpoints | [`compendium-adapter-huggingface`](https://github.com/sassy-solutions/compendium-adapter-huggingface) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Adapters.HuggingFace.svg)](https://www.nuget.org/packages/Compendium.Adapters.HuggingFace/) |
| AI — Azure-hosted | Azure OpenAI (Entra ID) | [`compendium-adapter-azure-openai`](https://github.com/sassy-solutions/compendium-adapter-azure-openai) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Adapters.AzureOpenAI.svg)](https://www.nuget.org/packages/Compendium.Adapters.AzureOpenAI/) |
| AI — AWS gateway | Bedrock (Claude / Llama / Mistral / Nova) | [`compendium-adapter-bedrock`](https://github.com/sassy-solutions/compendium-adapter-bedrock) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Adapters.Bedrock.svg)](https://www.nuget.org/packages/Compendium.Adapters.Bedrock/) |
| AI — gateway | LiteLLM (self-hostable) | [`compendium-adapter-litellm`](https://github.com/sassy-solutions/compendium-adapter-litellm) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Adapters.LiteLLM.svg)](https://www.nuget.org/packages/Compendium.Adapters.LiteLLM/) |
| AI — local | Ollama | [`compendium-adapter-ollama`](https://github.com/sassy-solutions/compendium-adapter-ollama) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Adapters.Ollama.svg)](https://www.nuget.org/packages/Compendium.Adapters.Ollama/) |

The thin ASP.NET Core glue (`Compendium.Adapters.AspNetCore` — middleware, ProblemDetails, multi-tenancy HTTP resolution) stays in the framework monorepo since it has no external SDK and evolves lock-step with `Compendium.Application`.

### Verified third-party adapters

None yet. Maintainers of community adapters that pass a security & convention review (namespacing, tenancy, Result-pattern, test coverage) can open a PR adding a row here.

### Writing your own adapter

Use the [`template-compendium-adapter-dotnet`](https://github.com/sassy-solutions/template-compendium-adapter-dotnet) GitHub template — it ships with the test stack, CI gate, MinVer versioning, and NuGet publishing wired in. See [`docs/adapters/external.md`](docs/adapters/external.md) for the writing guide.

## Quick start

Install the packages you need:

```bash
dotnet add package Compendium.Core
dotnet add package Compendium.Application
dotnet add package Compendium.Adapters.PostgreSQL
```

Define an event-sourced aggregate:

```csharp
using Compendium.Core.Domain.Primitives;
using Compendium.Core.Results;

public sealed class OrderAggregate : AggregateRoot<OrderId>
{
    private OrderStatus _status;
    private decimal _amount;

    private OrderAggregate(OrderId id) : base(id) { }

    public static Result<OrderAggregate> Create(CustomerId customerId, decimal amount)
    {
        if (amount <= 0)
            return Result.Failure<OrderAggregate>(
                Error.Validation("Order.Amount.Invalid", "Amount must be positive"));

        var order = new OrderAggregate(OrderId.New());
        order.AddDomainEvent(new OrderCreated(order.Id, customerId, amount));
        return Result.Success(order);
    }

    public void Apply(OrderCreated @event)
    {
        _status = OrderStatus.Pending;
        _amount = @event.Amount;
    }
}
```

Wire it up in `Program.cs`:

```csharp
using Compendium.Application.CQRS;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Register Compendium CQRS dispatchers (command/query handlers are resolved via IServiceProvider).
builder.Services.AddScoped<ICommandDispatcher, CommandDispatcher>();
builder.Services.AddScoped<IQueryDispatcher, QueryDispatcher>();

// Register your command/query handlers, then wire the PostgreSQL event store adapter
// using the options published by Compendium.Adapters.PostgreSQL.

var app = builder.Build();
```

## Architecture

```
Core (zero deps) → Abstractions → Application → Infrastructure → Adapters
                                       ↓
                              Multitenancy (cross-cutting)
```

- **Core** — Domain primitives with no external dependencies.
- **Abstractions** — Ports (interfaces) for infrastructure concerns: identity, billing, email, AI.
- **Application** — CQRS orchestration: command/query handlers, dispatchers.
- **Infrastructure** — Generic infrastructure concerns: projections, outbox, caching.
- **Adapters** — Concrete integrations with external systems.
- **Multitenancy** — Tenant resolution and scoping, usable across all layers.

## Packages

These are the framework packages that ship from this repository. Adapter packages live in their own repositories — see [Adapters](#adapters) above.

| Package | Purpose | NuGet |
|---------|---------|-------|
| `Compendium.Core` | DDD primitives, Result pattern, domain events | [![NuGet](https://img.shields.io/nuget/v/Compendium.Core.svg)](https://www.nuget.org/packages/Compendium.Core/) |
| `Compendium.Abstractions` | Shared infrastructure port interfaces | [![NuGet](https://img.shields.io/nuget/v/Compendium.Abstractions.svg)](https://www.nuget.org/packages/Compendium.Abstractions/) |
| `Compendium.Abstractions.AI` | AI provider contracts (incl. `IAIProvider`, `IEmbeddingProvider`, `IReranker`, agent loop) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Abstractions.AI.svg)](https://www.nuget.org/packages/Compendium.Abstractions.AI/) |
| `Compendium.Abstractions.Billing` | Billing provider contracts | [![NuGet](https://img.shields.io/nuget/v/Compendium.Abstractions.Billing.svg)](https://www.nuget.org/packages/Compendium.Abstractions.Billing/) |
| `Compendium.Abstractions.Email` | Email provider contracts | [![NuGet](https://img.shields.io/nuget/v/Compendium.Abstractions.Email.svg)](https://www.nuget.org/packages/Compendium.Abstractions.Email/) |
| `Compendium.Abstractions.Identity` | Identity provider contracts | [![NuGet](https://img.shields.io/nuget/v/Compendium.Abstractions.Identity.svg)](https://www.nuget.org/packages/Compendium.Abstractions.Identity/) |
| `Compendium.Abstractions.VectorStore` | Vector store port (`IVectorStore`) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Abstractions.VectorStore.svg)](https://www.nuget.org/packages/Compendium.Abstractions.VectorStore/) |
| `Compendium.Abstractions.Search` | Search port (`ISearchIndex`) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Abstractions.Search.svg)](https://www.nuget.org/packages/Compendium.Abstractions.Search/) |
| `Compendium.Application` | CQRS dispatchers, handlers, pipelines | [![NuGet](https://img.shields.io/nuget/v/Compendium.Application.svg)](https://www.nuget.org/packages/Compendium.Application/) |
| `Compendium.Infrastructure` | Projections, outbox, infrastructure building blocks | [![NuGet](https://img.shields.io/nuget/v/Compendium.Infrastructure.svg)](https://www.nuget.org/packages/Compendium.Infrastructure/) |
| `Compendium.Multitenancy` | Tenant context, resolution, and scoping | [![NuGet](https://img.shields.io/nuget/v/Compendium.Multitenancy.svg)](https://www.nuget.org/packages/Compendium.Multitenancy/) |
| `Compendium.Adapters.AspNetCore` | ASP.NET Core glue (middleware, ProblemDetails, multi-tenancy HTTP resolution) | [![NuGet](https://img.shields.io/nuget/v/Compendium.Adapters.AspNetCore.svg)](https://www.nuget.org/packages/Compendium.Adapters.AspNetCore/) |
| `Compendium.Testing` | Test helpers, fakes, TestContainers fixtures | [![NuGet](https://img.shields.io/nuget/v/Compendium.Testing.svg)](https://www.nuget.org/packages/Compendium.Testing/) |

## Documentation

The full documentation site is being built at [scojh.github.io/Compendium](https://scojh.github.io/Compendium/) (DocFX-powered). In the meantime:

- [ROADMAP.md](ROADMAP.md) — themes, what's next, and what's out of scope
- [CONTRIBUTING.md](CONTRIBUTING.md) — build, test, conventions
- [docs/adr/](docs/adr/) — architecture decision records
- Source under `src/` and the Nexus consumer code for end-to-end examples

## Who's using Compendium?

- **[Nexus](https://sassy.solutions)** — Multi-tenant platform engineering by Sassy Solutions.

Using Compendium in your project? Open a PR to add yourself to this list.

## Contributing

Contributions, issues, and feedback are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on code style, commit conventions, and the development loop.

## License

MIT &copy; 2026 Sassy Solutions. See [LICENSE](LICENSE) for details.
