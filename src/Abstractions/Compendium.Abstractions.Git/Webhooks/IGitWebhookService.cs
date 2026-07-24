// -----------------------------------------------------------------------
// <copyright file="IGitWebhookService.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Git.Connections;
using Compendium.Abstractions.Git.Repositories;

namespace Compendium.Abstractions.Git.Webhooks;

/// <summary>
/// Outgoing webhook subscription management on a repository or namespace.
/// Requires <see cref="Capabilities.GitCapability.WebhookManagement"/>.
/// </summary>
public interface IGitWebhookService
{
    /// <summary>
    /// Creates the subscription when absent (matched by URL), updates it
    /// otherwise (idempotent).
    /// </summary>
    Task<Result<GitWebhookSubscription>> EnsureAsync(
        GitConnection connection,
        GitWebhookTarget target,
        EnsureGitWebhook request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a subscription by identifier. Deleting an absent subscription
    /// succeeds (idempotent).
    /// </summary>
    Task<Result> DeleteAsync(
        GitConnection connection,
        GitWebhookTarget target,
        string subscriptionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the target's webhook subscriptions.
    /// </summary>
    Task<Result<IReadOnlyList<GitWebhookSubscription>>> ListAsync(
        GitConnection connection,
        GitWebhookTarget target,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Where a webhook subscription lives, as a closed union.
/// </summary>
public abstract record GitWebhookTarget
{
    private GitWebhookTarget()
    {
    }

    /// <summary>
    /// A repository-level webhook.
    /// </summary>
    /// <param name="Ref">The repository.</param>
    public sealed record Repository(GitRepositoryRef Ref) : GitWebhookTarget;

    /// <summary>
    /// A namespace-level (organization/group) webhook.
    /// </summary>
    /// <param name="Name">The namespace login.</param>
    public sealed record Namespace(string Name) : GitWebhookTarget;
}

/// <summary>
/// Request to create or update a webhook subscription.
/// </summary>
public sealed record EnsureGitWebhook
{
    /// <summary>Gets the delivery URL.</summary>
    public required Uri Url { get; init; }

    /// <summary>
    /// Gets the shared secret used to sign deliveries. Redacted in <c>ToString()</c>.
    /// </summary>
    public required string Secret { get; init; }

    /// <summary>
    /// Gets the provider-native event names to subscribe to (e.g. GitHub
    /// <c>"push"</c>, <c>"workflow_run"</c>). Each adapter documents its
    /// accepted names.
    /// </summary>
    public required IReadOnlyList<string> Events { get; init; }

    /// <summary>Gets whether the subscription is active. Defaults to true.</summary>
    public bool Active { get; init; } = true;

    /// <inheritdoc />
    public override string ToString() => $"EnsureGitWebhook(Url={Url}, Secret=***, Events=[{string.Join(", ", Events)}])";
}

/// <summary>
/// A webhook subscription as reported by the provider.
/// </summary>
/// <param name="Id">The provider-side subscription identifier.</param>
/// <param name="Url">The delivery URL.</param>
/// <param name="Events">The provider-native event names subscribed to.</param>
/// <param name="Active">Whether the subscription is active.</param>
public sealed record GitWebhookSubscription(
    string Id,
    Uri Url,
    IReadOnlyList<string> Events,
    bool Active);
