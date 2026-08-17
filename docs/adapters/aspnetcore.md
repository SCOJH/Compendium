# Compendium.Adapters.AspNetCore

ASP.NET Core integration: tenant validation middleware, security headers, problem-details mapping, and authentication helpers.

## Install

```bash
dotnet add package Compendium.Adapters.AspNetCore
```

## Configuration

The adapter exposes several middleware and helper registrations. Most projects wire the tenant middleware first.

```csharp
using Compendium.Adapters.AspNetCore.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<TenantValidationMiddlewareOptions>(
    builder.Configuration.GetSection("Compendium:Tenant"));

var app = builder.Build();

app.UseMiddleware<TenantValidationMiddleware>();
// ...
app.Run();
```

`TenantValidationMiddlewareOptions` (see [`TenantValidationMiddleware.cs`](https://github.com/SCOJH/Compendium/blob/main/src/Adapters/Compendium.Adapters.AspNetCore/Security/TenantValidationMiddleware.cs)):

| Option | Default | Description |
|---|---|---|
| `TenantHeaderName` | `X-Tenant-ID` | HTTP header to read the tenant from |
| `EnableSubdomainResolution` | `true` | Whether to fall back to the host's subdomain |
| `IgnoredSubdomains` | `www, api, admin, app, dashboard, console, portal, staging, dev, test` | Subdomains *not* treated as tenant identifiers |
| `TenantClaimTypes` | `tenant_id, tid, org_id, organization_id, urn:zitadel:iam:org:id` | JWT claim names checked, in order |
| `ExcludedPaths` | `/health, /healthz, /ready, /live, /metrics, /.well-known, /swagger, /api-docs` | Paths skipped by tenant validation |

## Usage

The middleware extracts the tenant from header / subdomain / JWT, validates that all sources agree, looks up the tenant via `ITenantStore`, and sets `TenantContext` for the rest of the pipeline. See [the multi-tenancy concept page](../concepts/multi-tenancy.md) for the full data flow.

Downstream handlers receive the tenant via DI:

```csharp
public sealed class GetOrdersHandler(TenantContext tenant, IOrderRepository repo)
    : IQueryHandler<GetOrdersQuery, IReadOnlyList<Order>>
{
    public Task<IReadOnlyList<Order>> Handle(GetOrdersQuery q, CancellationToken ct)
        => repo.ListAsync(tenant.Current.Id, ct);
}
```

## Gotchas

- **Order matters.** Place the tenant middleware *after* authentication (so the JWT claim is available) but *before* authorization (so policies can read `TenantContext`).
- **`ExcludedPaths` are prefix matches.** `/health` matches `/health` and `/healthz` and `/health-check`. Pick paths intentionally.
- **CRLF in `Path`.** The middleware sanitizes user-controlled paths before logging (POM-175). If you mirror the pattern in your own middleware, do the same.
- **Subdomain resolution requires at least 3 host parts.** `acme.example.com` works; `localhost`, IPs, and bare-domain (`example.com`) don't.

## See also

- [API Reference: Compendium.Adapters.AspNetCore.Security](../api/Compendium.Adapters.AspNetCore.Security.html)
- [Multi-tenancy concept](../concepts/multi-tenancy.md)
- [ADR 0004 — Multi-tenancy strategy](../adr/0004-multi-tenancy-strategy.md)
