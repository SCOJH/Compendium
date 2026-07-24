# Compendium.Adapters.Scaleway.SecretManager

Scaleway Secret Manager adapter for `Compendium.Abstractions.Secrets`: a
concrete `ISecretVault` over the regional `v1beta1` REST API (raw HTTP,
`X-Auth-Token` auth — no provider SDK dependency).

```csharp
services.AddScalewaySecretVault(o =>
{
    o.DefaultRegion = "fr-par";
    o.DefaultProjectId = "<scaleway-project-id>";
});

var connection = new SecretVaultConnection
{
    Provider = "scaleway",
    Credential = new SecretVaultCredential.ApiToken(scwSecretKey),
};

var created = await vault.Secrets.CreateAsync(connection, new CreateVaultSecret
{
    Name = "db-password",
    Path = SecretScopePath.From("myapp", "prod").Value,
});
var v1 = await vault.Versions.AddAsync(connection, created.Value.SecretId,
    SecretMaterial.FromString("hunter2"));
var read = await vault.Versions.AccessAsync(connection, created.Value.SecretId, v1.Value.Revision);
```

See `CAPABILITIES.md` for the declared capability matrix and operational
notes (64 KiB payload limit, tag encoding, retry policy, billing levers).

The adapter passes the `SecretVaultContractTests` behavioral contract from
`Compendium.Testing` (run against a live tenancy via the integration suite;
CI runs the same contract against `InMemorySecretVault`).
