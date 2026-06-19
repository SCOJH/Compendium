// -----------------------------------------------------------------------
// <copyright file="InboundMessage.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Messaging.Models;

/// <summary>
/// A normalized inbound message extracted from a platform webhook. Platform-specific shapes
/// (Telegram update, Slack event, Discord interaction, ...) are projected onto this common type.
/// </summary>
public sealed record InboundMessage
{
    /// <summary>The platform the message arrived on (see <see cref="MessagingPlatforms"/>).</summary>
    public required string Platform { get; init; }

    /// <summary>
    /// The conversation identifier to reply to (Telegram chat id, Slack channel, Discord channel,
    /// WhatsApp phone, ...). Opaque to callers; passed back on the <see cref="OutboundMessage"/>.
    /// </summary>
    public required string ConversationId { get; init; }

    /// <summary>The message text. Empty for non-text messages that carry only attachments.</summary>
    public required string Text { get; init; }

    /// <summary>The platform user identifier of the sender, when available.</summary>
    public string? SenderId { get; init; }

    /// <summary>A human-readable sender name, when available.</summary>
    public string? SenderDisplayName { get; init; }

    /// <summary>The platform message identifier, when available (enables threaded replies).</summary>
    public string? MessageId { get; init; }

    /// <summary>The platform-reported timestamp, or the receive time when the platform omits it.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Optional attachments (images, audio, files) referenced by the message.</summary>
    public IReadOnlyList<MessageAttachment>? Attachments { get; init; }

    /// <summary>Optional raw platform fields preserved for advanced routing or audit.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
