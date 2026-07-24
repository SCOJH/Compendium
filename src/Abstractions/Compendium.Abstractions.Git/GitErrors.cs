// -----------------------------------------------------------------------
// <copyright file="GitErrors.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Git.Capabilities;

namespace Compendium.Abstractions.Git;

/// <summary>
/// Provides standardized error definitions for git-server operations. Adapters
/// map provider responses onto these errors so consumers can handle failures
/// uniformly across providers.
/// </summary>
public static class GitErrors
{
    /// <summary>
    /// Gets the error code prefix for git-server errors.
    /// </summary>
    public const string Prefix = "Git";

    /// <summary>
    /// The git server is not configured on this host (missing app registration,
    /// credentials, or base URL). Returned by <see cref="Stubs.NullGitServer"/>
    /// and by adapters whose required options are absent.
    /// </summary>
    public static Error NotConfigured(string? provider = null) =>
        Error.Unavailable(
            $"{Prefix}.NotConfigured",
            provider is null
                ? "No git server is configured on this host."
                : $"The '{provider}' git server is not configured on this host.");

    /// <summary>
    /// The operation requires a capability the provider does not support (or
    /// supports only partially). Carries <c>provider</c> and <c>capability</c>
    /// metadata; see the adapter's CAPABILITIES.md for the support matrix.
    /// </summary>
    public static Error NotSupported(string provider, GitCapability capability, string? limitation = null)
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
    /// The provider rejected the supplied credential (expired token, revoked
    /// installation, bad signature on an app JWT, …).
    /// </summary>
    public static Error AuthenticationFailed(string provider, string? detail = null) =>
        Error.Unauthorized(
            $"{Prefix}.AuthenticationFailed",
            detail is null
                ? $"Authentication against '{provider}' failed."
                : $"Authentication against '{provider}' failed: {detail}");

    /// <summary>
    /// The platform app is not installed on the target namespace. The
    /// <c>installUrl</c> metadata entry, when present, is the URL a user should
    /// visit to install the app.
    /// </summary>
    public static Error AppNotInstalled(string @namespace, string? installUrl = null)
    {
        var metadata = new Dictionary<string, object> { ["namespace"] = @namespace };
        if (installUrl is not null)
        {
            metadata["installUrl"] = installUrl;
        }

        return Error.Failure(
            $"{Prefix}.AppNotInstalled",
            $"The platform app is not installed on '{@namespace}'.",
            metadata);
    }

    /// <summary>
    /// The requested repository does not exist or is not visible to the credential.
    /// </summary>
    public static Error RepositoryNotFound(string repository) =>
        Error.NotFound($"{Prefix}.RepositoryNotFound", $"Repository '{repository}' was not found.");

    /// <summary>
    /// The requested namespace (organization / group / user account) does not
    /// exist or is not visible to the credential.
    /// </summary>
    public static Error NamespaceNotFound(string @namespace) =>
        Error.NotFound($"{Prefix}.NamespaceNotFound", $"Namespace '{@namespace}' was not found.");

    /// <summary>
    /// A resource with the same identifier already exists (repository name,
    /// namespace slug, environment name, …).
    /// </summary>
    public static Error Conflict(string resource) =>
        Error.Conflict($"{Prefix}.Conflict", $"'{resource}' already exists.");

    /// <summary>
    /// The provider rejected the request because of rate limiting. Adapters
    /// surface the provider's retry hint when available; callers must not retry
    /// before it elapses.
    /// </summary>
    public static Error Throttled(TimeSpan? retryAfter = null) =>
        Error.TooManyRequests(
            $"{Prefix}.Throttled",
            retryAfter.HasValue
                ? $"Git provider throttled the request. Retry after {retryAfter.Value.TotalSeconds:F0} seconds."
                : "Git provider throttled the request. Please try again later.",
            retryAfter.HasValue
                ? new Dictionary<string, object> { ["retryAfterSeconds"] = retryAfter.Value.TotalSeconds }
                : null);

    /// <summary>
    /// An inbound webhook delivery failed signature verification. The delivery
    /// must be rejected without processing.
    /// </summary>
    public static Error WebhookSignatureInvalid() =>
        Error.Unauthorized(
            $"{Prefix}.WebhookSignatureInvalid",
            "The webhook delivery signature is missing or invalid.");

    /// <summary>
    /// The provider rejected the request for a reason not covered by a more
    /// specific error. Carries <c>provider</c> and <c>statusCode</c> metadata.
    /// </summary>
    public static Error ProviderRejected(string provider, int statusCode, string detail) =>
        Error.Failure(
            $"{Prefix}.ProviderRejected",
            $"Provider '{provider}' rejected the request ({statusCode}): {detail}",
            new Dictionary<string, object> { ["provider"] = provider, ["statusCode"] = statusCode });
}
