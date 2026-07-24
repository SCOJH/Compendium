// -----------------------------------------------------------------------
// <copyright file="SecretVaultErrors.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Secrets.Capabilities;

namespace Compendium.Abstractions.Secrets;

/// <summary>
/// Provides standardized error definitions for secret-vault operations.
/// Adapters map provider responses onto these errors so consumers can handle
/// failures uniformly across providers.
/// </summary>
public static class SecretVaultErrors
{
    /// <summary>
    /// Gets the error code prefix for secret-vault errors.
    /// </summary>
    public const string Prefix = "SecretVault";

    /// <summary>
    /// No secret vault is configured on this host (missing credentials,
    /// project, or region). Returned by <see cref="Stubs.NullSecretVault"/> and
    /// by adapters whose required options are absent.
    /// </summary>
    public static Error NotConfigured(string? provider = null) =>
        Error.Unavailable(
            $"{Prefix}.NotConfigured",
            provider is null
                ? "No secret vault is configured on this host."
                : $"The '{provider}' secret vault is not configured on this host.");

    /// <summary>
    /// The operation requires a capability the provider does not support (or
    /// supports only partially). Carries <c>provider</c> and <c>capability</c>
    /// metadata; see the adapter's CAPABILITIES.md for the support matrix.
    /// </summary>
    public static Error NotSupported(string provider, SecretVaultCapability capability, string? limitation = null)
    {
        var metadata = new Dictionary<string, object>
        {
            ["provider"] = provider,
            ["capability"] = capability.ToString(),
        };
        if (limitation is not null)
        {
            metadata["limitation"] = limitation;
        }

        return Error.Unavailable(
            $"{Prefix}.CapabilityNotSupported",
            limitation is null
                ? $"Provider '{provider}' does not support capability '{capability}'."
                : $"Provider '{provider}' does not support capability '{capability}': {limitation}",
            metadata);
    }

    /// <summary>
    /// The vault rejected the caller's credentials (expired key, revoked
    /// application, wrong tenancy).
    /// </summary>
    public static Error AuthenticationFailed(string provider, string? detail = null) =>
        Error.Unauthorized(
            $"{Prefix}.AuthenticationFailed",
            detail is null
                ? $"Authentication with the '{provider}' secret vault failed."
                : $"Authentication with the '{provider}' secret vault failed: {detail}");

    /// <summary>
    /// No secret exists for the given provider-side identifier (or it has been
    /// deleted).
    /// </summary>
    public static Error SecretNotFound(string secretId) =>
        Error.NotFound(
            $"{Prefix}.SecretNotFound",
            $"Secret '{secretId}' was not found in the vault.");

    /// <summary>
    /// The secret exists but has no revision with the given number (never
    /// written, or destroyed).
    /// </summary>
    public static Error VersionNotFound(string secretId, long revision) =>
        Error.NotFound(
            $"{Prefix}.VersionNotFound",
            $"Revision {revision} of secret '{secretId}' was not found (never written or destroyed).");

    /// <summary>
    /// The revision exists but is disabled (kill-switch). Access is refused
    /// until the revision is re-enabled.
    /// </summary>
    public static Error VersionDisabled(string secretId, long revision) =>
        Error.Conflict(
            $"{Prefix}.VersionDisabled",
            $"Revision {revision} of secret '{secretId}' is disabled and cannot be accessed.");

    /// <summary>
    /// A secret with the same name already exists at the given path.
    /// </summary>
    public static Error ConflictExists(string name, string path) =>
        Error.Conflict(
            $"{Prefix}.ConflictExists",
            $"A secret named '{name}' already exists at path '{path}'.");

    /// <summary>
    /// The payload exceeds the provider's per-version size limit. Carries
    /// <c>size</c> and <c>maxSize</c> metadata.
    /// </summary>
    public static Error PayloadTooLarge(int size, int maxSize) =>
        Error.Validation(
            $"{Prefix}.PayloadTooLarge",
            $"Secret payload of {size} bytes exceeds the provider limit of {maxSize} bytes.",
            new Dictionary<string, object> { ["size"] = size, ["maxSize"] = maxSize });

    /// <summary>
    /// A provider-side quota was exhausted (secrets per project, versions per
    /// secret, ...). Not retryable without operator action.
    /// </summary>
    public static Error QuotaExceeded(string provider, string detail) =>
        Error.Unavailable(
            $"{Prefix}.QuotaExceeded",
            $"Provider '{provider}' quota exceeded: {detail}");

    /// <summary>
    /// The provider throttled the request. Carries <c>retryAfterSeconds</c>
    /// metadata when the provider supplied a Retry-After hint.
    /// </summary>
    public static Error Throttled(string provider, int? retryAfterSeconds = null)
    {
        var metadata = new Dictionary<string, object> { ["provider"] = provider };
        if (retryAfterSeconds is not null)
        {
            metadata["retryAfterSeconds"] = retryAfterSeconds.Value;
        }

        return Error.TooManyRequests(
            $"{Prefix}.Throttled",
            retryAfterSeconds is null
                ? $"Provider '{provider}' throttled the request."
                : $"Provider '{provider}' throttled the request; retry after {retryAfterSeconds.Value}s.",
            metadata);
    }

    /// <summary>
    /// The provider rejected the request for a reason not covered by a more
    /// specific error. Carries <c>provider</c> and <c>statusCode</c> metadata.
    /// </summary>
    public static Error ProviderRejected(string provider, int statusCode, string? detail = null) =>
        Error.Failure(
            $"{Prefix}.ProviderRejected",
            detail is null
                ? $"Provider '{provider}' rejected the request with status {statusCode}."
                : $"Provider '{provider}' rejected the request with status {statusCode}: {detail}",
            new Dictionary<string, object> { ["provider"] = provider, ["statusCode"] = statusCode });
}
