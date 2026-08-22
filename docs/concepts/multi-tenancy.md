# Multi-tenancy

Compendium treats multi-tenancy as a first-class concern, not a bolt-on. Every operation flows through a `TenantContext` that is set at the request boundary, validated for consistency, and propagated to every adapter that needs it (event store, projections, billing, identity).

The goal: cross-tenant data leaks are *impossible by construction*, not merely "policed by code reviews."

For the design rationale, see [ADR 0004 — Multi-tenancy strategy](../adr/0004-multi-tenancy-strategy.md).

## Tenant identity comes from multiple sources

In a real SaaS, the tenant identifier can arrive via several paths in the same request:

- An explicit `X-Tenant-ID` header (machine-to-machine APIs)
- A subdomain (`acme.example.com` → tenant `acme`)
- A claim in the JWT (`tenant_id`, `org_id`, or in our case `urn:zitadel:iam:org:id`)

When more than one source is present, Compendium **requires them to agree**. A request with `X-Tenant-ID: acme` and a JWT for tenant `globex` is rejected outright — that combination usually means a misconfigured proxy or a confused-deputy attack.

The middleware that enforces this lives in `Compendium.Adapters.AspNetCore`. From [`TenantValidationMiddleware.cs`](https://github.com/SCOJH/Compendium/blob/ca25347/src/Adapters/Compendium.Adapters.AspNetCore/Security/TenantValidationMiddleware.cs):

```csharp
// Extract tenant identifiers from all sources
var sources = ExtractTenantSources(context);

// Validate consistency across sources
var validationResult = validator.Validate(sources);

if (validationResult.IsFailure)
{
    _logger.LogWarning(
        "Tenant validation failed: {Error}. Path: {Path}",
        validationResult.Error.Message,
        SanitizeForLog(context.Request.Path));
    await WriteErrorResponse(context, validationResult.Error.Message,
        StatusCodes.Status403Forbidden);
    return;
}
```

The configurable bits (which header, which JWT claims, which paths to skip) live on [`TenantValidationMiddlewareOptions`](https://github.com/SCOJH/Compendium/blob/ca25347/src/Adapters/Compendium.Adapters.AspNetCore/Security/TenantValidationMiddleware.cs#L226).

## TenantContext is per-request, scoped DI

Once validated, the resolved tenant lives on a scoped `TenantContext`:

```
HTTP Request
    │
    ▼
[TenantValidationMiddleware]
    │  extracts header / subdomain / JWT
    │  validates consistency
    │  loads Tenant from ITenantStore
    │  sets tenantContext.SetTenant(tenant)
    │
    ▼
[Endpoint / Command Handler]
    │  receives TenantContext via DI
    │  passes Tenant.Id to adapters
    │
    ▼
[Adapters: PostgreSQL, Stripe, Listmonk, ...]
       └─ scope queries / API calls by tenant
```

Adapters that touch persistence read the tenant from `TenantContext` and scope every query accordingly. The contract is: **if you forgot to scope, the operation should fail loudly**, not silently return data from another tenant.

## Isolation strategies

Compendium does not force you into one isolation model. Three are common:

1. **Schema-per-tenant** in a shared database. Cheap, easy to operate, decent isolation. Default for `Compendium.Adapters.PostgreSQL` setups.
2. **Database-per-tenant**. Strongest isolation, more operational overhead. Compendium supports it by switching the connection string per `TenantContext`.
3. **Row-level security (RLS)**. All tenants share tables; Postgres RLS enforces isolation. Cheapest at scale but harder to debug.

The choice is made at infrastructure setup time, not in the domain. The domain code is identical across the three.

## Excluded paths

Some paths legitimately need to run without a tenant: health checks, OpenAPI specs, login endpoints. The middleware accepts an explicit allow-list:

```csharp
public string[] ExcludedPaths { get; set; } = new[]
{
    "/health", "/healthz", "/ready", "/live",
    "/metrics",
    "/.well-known",
    "/swagger", "/api-docs"
};
```

Anything else without a resolvable tenant is rejected.

## Pitfalls to avoid

- **Trusting only one source**. If you read just the header and ignore the JWT, an attacker with a valid token for tenant A can pass `X-Tenant-ID: B` and you have a leak. Compendium rejects the mismatch.
- **Logging the tenant ID and user email together** without thinking about retention. See the GDPR-driven `PiiMasking` helper in `Compendium.Adapters.Shared` and the related work in POM-178 / [ADR 0004](../adr/0004-multi-tenancy-strategy.md).
- **Forgetting to scope a new query**. Make it a code-review checklist item: every new repository method must take a `TenantId` (or read it from `TenantContext`). Compendium's existing adapters set the precedent — follow it.

## Where to go next

- [Hexagonal Architecture](hexagonal-architecture.md) — `TenantContext` is itself a port, with concrete implementations in adapters
- [Event Sourcing](event-sourcing.md) — events carry `AggregateId`; tenancy is enforced by the store, not by the event
- [ADR 0004](../adr/0004-multi-tenancy-strategy.md) — the decision and trade-offs
- [`samples/02-MultiTenant-WithPostgres`](https://github.com/SCOJH/Compendium/tree/main/samples/02-MultiTenant-WithPostgres) — runnable two-tenant example
