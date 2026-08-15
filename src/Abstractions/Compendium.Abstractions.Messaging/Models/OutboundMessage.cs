// -----------------------------------------------------------------------
// <copyright file="OutboundMessage.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Messaging.Models;

/// <summary>
/// A normalized message to deliver to a platform conversation. Connectors translate this into the
/// platform's native send call.
/// </summary>
public sealed record OutboundMessage
{
    /// <summary>The target conversation (mirrors <see cref="InboundMessage.ConversationId"/>).</summary>
    public required string ConversationId { get; init; }

    /// <summary>The message text to send.</summary>
    public required string Text { get; init; }

    /// <summary>Optional id of the inbound message to reply to, when the platform supports threading.</summary>
    public string? ReplyToMessageId { get; init; }

    /// <summary>Optional attachments to send alongside the text.</summary>
    public IReadOnlyList<MessageAttachment>? Attachments { get; init; }

    /// <summary>Optional platform-specific send options (for example Telegram <c>parse_mode</c>).</summary>
    public IReadOnlyDictionary<string, string>? Options { get; init; }
}

/// <summary>The outcome of an outbound delivery.</summary>
/// <param name="MessageId">The platform-assigned message id, when returned.</param>
/// <param name="SentAt">When the message was accepted by the platform.</param>
public sealed record OutboundReceipt(string? MessageId, DateTimeOffset SentAt);
