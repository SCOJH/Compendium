// -----------------------------------------------------------------------
// <copyright file="MessagingErrors.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Messaging;

/// <summary>
/// Standardized error definitions for conversational messaging operations.
/// </summary>
public static class MessagingErrors
{
    /// <summary>Gets the error code prefix for messaging errors.</summary>
    public const string Prefix = "Messaging";

    /// <summary>The inbound webhook signature could not be verified against the supplied credentials.</summary>
    public static Error InvalidSignature(string platform) =>
        Error.Forbidden($"{Prefix}.InvalidSignature", $"Inbound '{platform}' webhook signature verification failed.");

    /// <summary>A required credential (bot token, signing secret, ...) was missing.</summary>
    public static Error MissingCredential(string key) =>
        Error.Validation($"{Prefix}.MissingCredential", $"Required messaging credential '{key}' is missing.");

    /// <summary>The inbound webhook payload was malformed or could not be parsed.</summary>
    public static Error MalformedPayload(string platform) =>
        Error.Validation($"{Prefix}.MalformedPayload", $"Inbound '{platform}' webhook payload was malformed.");

    /// <summary>No connector is registered for the requested platform.</summary>
    public static Error UnsupportedPlatform(string platform) =>
        Error.NotFound($"{Prefix}.UnsupportedPlatform", $"No messaging connector is registered for platform '{platform}'.");

    /// <summary>The platform rejected the outbound delivery.</summary>
    public static Error DeliveryFailed(string platform, string detail) =>
        Error.Failure($"{Prefix}.DeliveryFailed", $"Delivery to '{platform}' failed: {detail}");

    /// <summary>The platform throttled the request.</summary>
    public static Error Throttled(string platform, TimeSpan? retryAfter = null) =>
        Error.TooManyRequests(
            $"{Prefix}.Throttled",
            retryAfter.HasValue
                ? $"'{platform}' throttled the request. Retry after {retryAfter.Value.TotalSeconds} seconds."
                : $"'{platform}' throttled the request. Please try again later.");
}
