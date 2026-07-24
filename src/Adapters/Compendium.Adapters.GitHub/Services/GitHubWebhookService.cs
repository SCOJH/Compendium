// -----------------------------------------------------------------------
// <copyright file="GitHubWebhookService.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Globalization;
using Compendium.Adapters.GitHub.Http;
using Octokit;

namespace Compendium.Adapters.GitHub.Services;

/// <summary>
/// Outbound webhook subscriptions on a repository or organization, backed by
/// Octokit. Subscriptions are matched by delivery URL so ensuring the same URL
/// twice updates in place rather than duplicating.
/// </summary>
internal sealed class GitHubWebhookService : IGitWebhookService
{
    private const string HookName = "web";

    private readonly IGitHubClientProvider _clients;

    public GitHubWebhookService(IGitHubClientProvider clients)
    {
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
    }

    /// <inheritdoc />
    public async Task<Result<GitWebhookSubscription>> EnsureAsync(
        GitConnection connection, GitWebhookTarget target, EnsureGitWebhook request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(request);
        var guard = GitHubCapabilities.Matrix.EnsureSupported(GitCapability.WebhookManagement);
        if (guard.IsFailure)
        {
            return Result.Failure<GitWebhookSubscription>(guard.Error);
        }

        return await ExecuteAsync(connection, Context(target), client => target switch
        {
            GitWebhookTarget.Repository r => EnsureRepositoryHookAsync(client, r.Ref, request),
            GitWebhookTarget.Namespace n => EnsureOrganizationHookAsync(client, n.Name, request),
            _ => Task.FromResult(Result.Failure<GitWebhookSubscription>(UnknownTarget(target))),
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result> DeleteAsync(
        GitConnection connection, GitWebhookTarget target, string subscriptionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionId);

        if (!int.TryParse(subscriptionId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hookId))
        {
            // A non-numeric id cannot name an existing GitHub hook — nothing to delete.
            return Task.FromResult(Result.Success());
        }

        return ExecuteUnitAsync(connection, Context(target), async client =>
        {
            try
            {
                switch (target)
                {
                    case GitWebhookTarget.Repository r:
                        await client.Repository.Hooks.Delete(r.Ref.Namespace, r.Ref.Name, hookId).ConfigureAwait(false);
                        break;
                    case GitWebhookTarget.Namespace n:
                        await client.Organization.Hook.Delete(n.Name, hookId).ConfigureAwait(false);
                        break;
                }
            }
            catch (NotFoundException)
            {
                // Idempotent: deleting an absent subscription succeeds.
            }
        });
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<GitWebhookSubscription>>> ListAsync(
        GitConnection connection, GitWebhookTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        return ExecuteAsync(connection, Context(target), async client =>
        {
            IReadOnlyList<GitWebhookSubscription> subscriptions = target switch
            {
                GitWebhookTarget.Repository r =>
                    (await client.Repository.Hooks.GetAll(r.Ref.Namespace, r.Ref.Name).ConfigureAwait(false))
                        .Select(MapRepositoryHook).Where(s => s is not null).Select(s => s!).ToList(),
                GitWebhookTarget.Namespace n =>
                    (await client.Organization.Hook.GetAll(n.Name).ConfigureAwait(false))
                        .Select(MapOrganizationHook).Where(s => s is not null).Select(s => s!).ToList(),
                _ => [],
            };
            return Result.Success(subscriptions);
        });
    }

    private static async Task<Result<GitWebhookSubscription>> EnsureRepositoryHookAsync(
        IGitHubClient client, GitRepositoryRef repository, EnsureGitWebhook request)
    {
        var hooks = await client.Repository.Hooks.GetAll(repository.Namespace, repository.Name).ConfigureAwait(false);
        var existing = hooks.FirstOrDefault(h => UrlOf(h.Config) == request.Url.ToString());
        var config = BuildConfig(request);
        var events = request.Events.ToList();

        if (existing is not null)
        {
            var updated = await client.Repository.Hooks.Edit(
                repository.Namespace, repository.Name, (int)existing.Id,
                new EditRepositoryHook(config) { Events = events, Active = request.Active }).ConfigureAwait(false);
            return Result.Success(MapRepositoryHook(updated)!);
        }

        var created = await client.Repository.Hooks.Create(
            repository.Namespace, repository.Name,
            new NewRepositoryHook(HookName, config) { Events = events, Active = request.Active }).ConfigureAwait(false);
        return Result.Success(MapRepositoryHook(created)!);
    }

    private static async Task<Result<GitWebhookSubscription>> EnsureOrganizationHookAsync(
        IGitHubClient client, string @namespace, EnsureGitWebhook request)
    {
        var hooks = await client.Organization.Hook.GetAll(@namespace).ConfigureAwait(false);
        var existing = hooks.FirstOrDefault(h => UrlOf(h.Config) == request.Url.ToString());
        var config = BuildConfig(request);
        var events = request.Events.ToList();

        if (existing is not null)
        {
            var updated = await client.Organization.Hook.Edit(
                @namespace, (int)existing.Id,
                new EditOrganizationHook(config) { Events = events, Active = request.Active }).ConfigureAwait(false);
            return Result.Success(MapOrganizationHook(updated)!);
        }

        var created = await client.Organization.Hook.Create(
            @namespace, new NewOrganizationHook(HookName, config) { Events = events, Active = request.Active })
            .ConfigureAwait(false);
        return Result.Success(MapOrganizationHook(created)!);
    }

    private static Dictionary<string, string> BuildConfig(EnsureGitWebhook request) => new(StringComparer.Ordinal)
    {
        ["url"] = request.Url.ToString(),
        ["content_type"] = "json",
        ["secret"] = request.Secret,
        ["insecure_ssl"] = "0",
    };

    private static string? UrlOf(IReadOnlyDictionary<string, string>? config) =>
        config is not null && config.TryGetValue("url", out var url) ? url : null;

    private static GitWebhookSubscription? MapRepositoryHook(RepositoryHook hook)
    {
        var url = UrlOf(hook.Config);
        return url is null
            ? null
            : new GitWebhookSubscription(
                hook.Id.ToString(CultureInfo.InvariantCulture), new Uri(url), hook.Events?.ToList() ?? [], hook.Active);
    }

    private static GitWebhookSubscription? MapOrganizationHook(OrganizationHook hook)
    {
        var url = UrlOf(hook.Config);
        return url is null
            ? null
            : new GitWebhookSubscription(
                hook.Id.ToString(CultureInfo.InvariantCulture), new Uri(url), hook.Events?.ToList() ?? [], hook.Active);
    }

    private static GitRestErrorContext Context(GitWebhookTarget target) => target switch
    {
        GitWebhookTarget.Repository r => GitRestErrorContext.ForRepository(r.Ref),
        GitWebhookTarget.Namespace n => GitRestErrorContext.ForNamespace(n.Name),
        _ => GitRestErrorContext.None,
    };

    private static Error UnknownTarget(GitWebhookTarget target) =>
        Error.Failure("GitHub.UnknownWebhookTarget", $"Unknown webhook target '{target.GetType().Name}'.");

    private async Task<Result<T>> ExecuteAsync<T>(
        GitConnection connection, GitRestErrorContext context, Func<IGitHubClient, Task<Result<T>>> operation)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var clientResult = await _clients.GetClientAsync(connection, CancellationToken.None).ConfigureAwait(false);
        if (clientResult.IsFailure)
        {
            return Result.Failure<T>(clientResult.Error);
        }

        try
        {
            return await operation(clientResult.Value).ConfigureAwait(false);
        }
        catch (ApiException ex)
        {
            return Result.Failure<T>(GitHubErrorMapper.FromException(ex, context));
        }
    }

    private async Task<Result> ExecuteUnitAsync(
        GitConnection connection, GitRestErrorContext context, Func<IGitHubClient, Task> operation)
    {
        var result = await ExecuteAsync<object?>(connection, context, async client =>
        {
            await operation(client).ConfigureAwait(false);
            return Result.Success<object?>(null);
        }).ConfigureAwait(false);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }
}
