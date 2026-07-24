// -----------------------------------------------------------------------
// <copyright file="GitHubWebhookIngestor.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Compendium.Adapters.GitHub.Services;

namespace Compendium.Adapters.GitHub.Webhooks;

/// <summary>
/// Verifies and parses inbound GitHub webhook deliveries into the neutral
/// <see cref="GitWebhookEvent"/> union. Fail-closed: a missing or invalid
/// <c>X-Hub-Signature-256</c> (HMAC-SHA256 over the raw body, constant-time
/// compared) is rejected before any parsing. Event types the platform does not
/// consume parse to <see cref="GitWebhookEvent.Unsupported"/>.
/// </summary>
internal sealed class GitHubWebhookIngestor : IGitWebhookIngestor
{
    private const string SignatureHeader = "X-Hub-Signature-256";
    private const string EventHeader = "X-GitHub-Event";
    private const string DeliveryHeader = "X-GitHub-Delivery";
    private const string SignaturePrefix = "sha256=";

    /// <inheritdoc />
    public Result<GitWebhookEvent> Parse(GitWebhookDelivery delivery, string secret)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        if (!VerifySignature(delivery, secret))
        {
            return Result.Failure<GitWebhookEvent>(GitErrors.WebhookSignatureInvalid());
        }

        if (!TryGetHeader(delivery, DeliveryHeader, out var deliveryId) || string.IsNullOrWhiteSpace(deliveryId))
        {
            return Result.Failure<GitWebhookEvent>(Error.Validation(
                $"{GitErrors.Prefix}.MalformedDelivery", $"The delivery is missing the {DeliveryHeader} header."));
        }

        if (!TryGetHeader(delivery, EventHeader, out var eventType) || string.IsNullOrWhiteSpace(eventType))
        {
            return Result.Failure<GitWebhookEvent>(Error.Validation(
                $"{GitErrors.Prefix}.MalformedDelivery", $"The delivery is missing the {EventHeader} header."));
        }

        JsonElement root;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(delivery.Body);
            root = document.RootElement;
        }
        catch (JsonException ex)
        {
            return Result.Failure<GitWebhookEvent>(Error.Validation(
                $"{GitErrors.Prefix}.MalformedDelivery", $"The delivery body is not valid JSON: {ex.Message}"));
        }

        using (document)
        {
            var repository = ParseRepository(root);
            var parsed = Translate(eventType, root, deliveryId, repository);
            return Result.Success(parsed);
        }
    }

    private static GitWebhookEvent Translate(
        string eventType, JsonElement root, string deliveryId, GitRepositoryRef? repository) => eventType switch
    {
        "push" => TranslatePush(root, deliveryId, repository),
        "pull_request" => TranslatePullRequest(root, deliveryId, repository),
        "workflow_run" => TranslateWorkflowRun(root, deliveryId, repository),
        "installation" or "installation_repositories" => TranslateInstallation(eventType, root, deliveryId),
        _ => new GitWebhookEvent.Unsupported(eventType) { DeliveryId = deliveryId, Repository = repository },
    };

    private static GitWebhookEvent TranslatePush(JsonElement root, string deliveryId, GitRepositoryRef? repository)
    {
        var reference = GetString(root, "ref") ?? string.Empty;
        var sha = GetString(root, "after") ?? GetString(GetProperty(root, "head_commit"), "id") ?? string.Empty;

        if (reference.StartsWith("refs/tags/", StringComparison.Ordinal))
        {
            return new GitWebhookEvent.TagPushed(reference["refs/tags/".Length..], sha)
            {
                DeliveryId = deliveryId,
                Repository = repository,
            };
        }

        return new GitWebhookEvent.Push(reference, sha) { DeliveryId = deliveryId, Repository = repository };
    }

    private static GitWebhookEvent TranslatePullRequest(JsonElement root, string deliveryId, GitRepositoryRef? repository)
    {
        var pr = GetProperty(root, "pull_request");
        var action = GetString(root, "action") ?? string.Empty;
        var number = GetInt(root, "number") ?? GetInt(pr, "number") ?? 0;
        var source = GetString(GetProperty(pr, "head"), "ref") ?? string.Empty;
        var targetRef = GetString(GetProperty(pr, "base"), "ref") ?? string.Empty;

        return new GitWebhookEvent.PullRequestChanged(action, number, source, targetRef)
        {
            DeliveryId = deliveryId,
            Repository = repository,
        };
    }

    private static GitWebhookEvent TranslateWorkflowRun(JsonElement root, string deliveryId, GitRepositoryRef? repository)
    {
        var action = GetString(root, "action");
        var run = GetProperty(root, "workflow_run");

        if (!string.Equals(action, "completed", StringComparison.Ordinal))
        {
            return new GitWebhookEvent.Unsupported("workflow_run") { DeliveryId = deliveryId, Repository = repository };
        }

        var runId = GetLong(run, "id")?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        var pipeline = GetString(run, "name") ?? string.Empty;
        var status = GitHubPipelineStatusMapper.Map(GetString(run, "status"), GetString(run, "conclusion"));
        var reference = GetString(run, "head_branch") ?? string.Empty;

        return new GitWebhookEvent.PipelineRunCompleted(runId, pipeline, status, reference)
        {
            DeliveryId = deliveryId,
            Repository = repository,
        };
    }

    private static GitWebhookEvent TranslateInstallation(string eventType, JsonElement root, string deliveryId)
    {
        var installation = GetProperty(root, "installation");
        var account = GetProperty(installation, "account");
        var installationId = GetLong(installation, "id")?.ToString(System.Globalization.CultureInfo.InvariantCulture)
            ?? string.Empty;
        var login = GetString(account, "login") ?? string.Empty;
        var accountType = string.Equals(GetString(account, "type"), "User", StringComparison.OrdinalIgnoreCase)
            ? GitAccountType.User
            : GitAccountType.Organization;
        var action = GetString(root, "action");

        var change = eventType == "installation_repositories"
            ? GitConnectionChangeKind.RepositoriesChanged
            : action switch
            {
                "created" => GitConnectionChangeKind.Installed,
                "deleted" => GitConnectionChangeKind.Uninstalled,
                "suspend" => GitConnectionChangeKind.Suspended,
                "unsuspend" => GitConnectionChangeKind.Unsuspended,
                _ => GitConnectionChangeKind.RepositoriesChanged,
            };

        return new GitWebhookEvent.ConnectionChanged(login, accountType, installationId, change)
        {
            DeliveryId = deliveryId,
        };
    }

    private static GitRepositoryRef? ParseRepository(JsonElement root)
    {
        var fullName = GetString(GetProperty(root, "repository"), "full_name");
        return fullName?.Split('/') is [var ns, var name] ? new GitRepositoryRef(ns, name) : null;
    }

    private static bool VerifySignature(GitWebhookDelivery delivery, string secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return false;
        }

        if (!TryGetHeader(delivery, SignatureHeader, out var header)
            || string.IsNullOrWhiteSpace(header)
            || !header.StartsWith(SignaturePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(header[SignaturePrefix.Length..]);
        }
        catch (FormatException)
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(delivery.Body));
        return provided.Length == expected.Length && CryptographicOperations.FixedTimeEquals(provided, expected);
    }

    private static bool TryGetHeader(GitWebhookDelivery delivery, string name, out string value)
    {
        if (delivery.Headers.TryGetValue(name, out var direct))
        {
            value = direct;
            return true;
        }

        foreach (var (key, headerValue) in delivery.Headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = headerValue;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static JsonElement GetProperty(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value
            : default;

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : null;

    private static long? GetLong(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var number)
            ? number
            : null;
}
