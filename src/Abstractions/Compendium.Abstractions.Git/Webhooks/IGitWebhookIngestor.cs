// -----------------------------------------------------------------------
// <copyright file="IGitWebhookIngestor.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Git.Repositories;

namespace Compendium.Abstractions.Git.Webhooks;

/// <summary>
/// Inbound side of webhooks: verifies a raw delivery's signature and parses it
/// into the neutral <see cref="GitWebhookEvent"/> union. Pure and synchronous —
/// it owns no HTTP endpoint; the host maps its own route and hands the raw
/// body/headers here. Requires
/// <see cref="Capabilities.GitCapability.WebhookIngestion"/>.
/// </summary>
public interface IGitWebhookIngestor
{
    /// <summary>
    /// Verifies the delivery signature against <paramref name="secret"/>
    /// (fail-closed: missing or invalid signatures return
    /// <c>Git.WebhookSignatureInvalid</c>) and parses the payload. Event types
    /// the platform does not consume parse to
    /// <see cref="GitWebhookEvent.Unsupported"/> — callers acknowledge those
    /// with a 2xx and never retry.
    /// </summary>
    Result<GitWebhookEvent> Parse(GitWebhookDelivery delivery, string secret);
}

/// <summary>
/// A raw inbound webhook delivery as received over HTTP.
/// </summary>
public sealed record GitWebhookDelivery
{
    /// <summary>
    /// Gets the raw request body exactly as received — signature verification
    /// runs over these bytes, so the body must not be re-serialized upstream.
    /// </summary>
    public required string Body { get; init; }

    /// <summary>
    /// Gets the request headers. Lookups are case-insensitive; hosts should
    /// build the dictionary with <see cref="StringComparer.OrdinalIgnoreCase"/>.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }
}

/// <summary>
/// A neutral, provider-agnostic inbound webhook event, as a closed union for
/// exhaustive pattern matching.
/// </summary>
public abstract record GitWebhookEvent
{
    /// <summary>
    /// Gets the provider-assigned delivery identifier, used for idempotent
    /// processing (the same delivery may be received more than once).
    /// </summary>
    public required string DeliveryId { get; init; }

    /// <summary>
    /// Gets the repository the event concerns, when the event is repository-scoped.
    /// </summary>
    public GitRepositoryRef? Repository { get; init; }

    /// <summary>
    /// Commits were pushed to a branch.
    /// </summary>
    /// <param name="Reference">The pushed reference (e.g. <c>"refs/heads/main"</c>).</param>
    /// <param name="HeadCommitSha">The SHA of the new head commit.</param>
    public sealed record Push(string Reference, string HeadCommitSha) : GitWebhookEvent;

    /// <summary>
    /// A tag was pushed.
    /// </summary>
    /// <param name="Tag">The tag name.</param>
    /// <param name="CommitSha">The SHA the tag points at.</param>
    public sealed record TagPushed(string Tag, string CommitSha) : GitWebhookEvent;

    /// <summary>
    /// A pull/merge request changed state.
    /// </summary>
    /// <param name="Action">The provider-native action (e.g. <c>"opened"</c>, <c>"closed"</c>).</param>
    /// <param name="Number">The pull request number.</param>
    /// <param name="SourceReference">The source branch.</param>
    /// <param name="TargetReference">The target branch.</param>
    public sealed record PullRequestChanged(
        string Action,
        int Number,
        string SourceReference,
        string TargetReference) : GitWebhookEvent;

    /// <summary>
    /// A pipeline run reached a terminal state.
    /// </summary>
    /// <param name="RunId">The run identifier.</param>
    /// <param name="Pipeline">The pipeline the run belongs to.</param>
    /// <param name="Status">The neutral terminal status.</param>
    /// <param name="Reference">The git reference the run executed on.</param>
    public sealed record PipelineRunCompleted(
        string RunId,
        string Pipeline,
        Pipelines.GitPipelineStatus Status,
        string Reference) : GitWebhookEvent;

    /// <summary>
    /// The platform app's installation on an account changed (installed,
    /// suspended, unsuspended, repositories added/removed, or uninstalled).
    /// </summary>
    /// <param name="Namespace">The account login the installation lives on.</param>
    /// <param name="AccountType">Whether that account is an organization or a user.</param>
    /// <param name="InstallationId">The provider-side installation identifier.</param>
    /// <param name="Change">The neutral change kind.</param>
    public sealed record ConnectionChanged(
        string Namespace,
        Connections.GitAccountType AccountType,
        string InstallationId,
        GitConnectionChangeKind Change) : GitWebhookEvent;

    /// <summary>
    /// An event type the platform does not consume. Acknowledge with a 2xx and
    /// do not retry.
    /// </summary>
    /// <param name="ProviderEventType">The provider-native event type name.</param>
    public sealed record Unsupported(string ProviderEventType) : GitWebhookEvent;
}

/// <summary>
/// The kind of change reported by <see cref="GitWebhookEvent.ConnectionChanged"/>.
/// </summary>
public enum GitConnectionChangeKind
{
    /// <summary>The app was installed on the account.</summary>
    Installed,

    /// <summary>The installation was suspended.</summary>
    Suspended,

    /// <summary>The installation was unsuspended.</summary>
    Unsuspended,

    /// <summary>The set of repositories the installation can access changed.</summary>
    RepositoriesChanged,

    /// <summary>The app was uninstalled from the account.</summary>
    Uninstalled,
}
