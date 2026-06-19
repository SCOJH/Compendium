// -----------------------------------------------------------------------
// <copyright file="IMessagingConnector.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Messaging.Models;

namespace Compendium.Abstractions.Messaging;

/// <summary>
/// Provider-agnostic, two-way connector to a conversational messaging platform
/// (Telegram, Slack, Discord, Microsoft Teams, WhatsApp, or a generic webhook).
/// </summary>
/// <remarks>
/// <para>
/// A connector has two responsibilities: translate a raw inbound platform webhook into
/// normalized <see cref="InboundMessage"/> values (verifying authenticity along the way),
/// and deliver a normalized <see cref="OutboundMessage"/> back to the platform.
/// </para>
/// <para>
/// Credentials (bot token, signing secret, ...) are supplied <em>per call</em> via
/// <see cref="ChannelCredentials"/> rather than baked into the connector, so a single
/// registered connector can serve many tenants — the host resolves the right secret for the
/// inbound conversation and passes it in.
/// </para>
/// </remarks>
public interface IMessagingConnector
{
    /// <summary>
    /// Gets the platform identifier this connector implements (see <see cref="MessagingPlatforms"/>).
    /// </summary>
    string Platform { get; }

    /// <summary>
    /// Parses and verifies a raw inbound webhook request from the platform.
    /// </summary>
    /// <param name="request">The raw HTTP webhook (headers, body, query) as received by the host.</param>
    /// <param name="credentials">The tenant credentials used to verify the request signature.</param>
    /// <returns>
    /// A result containing an <see cref="InboundEnvelope"/> with zero or more normalized messages
    /// and an optional platform handshake response to echo back; or a failure (for example
    /// <see cref="MessagingErrors.InvalidSignature"/>) when the request cannot be trusted.
    /// </returns>
    Result<InboundEnvelope> ParseInbound(InboundRequest request, ChannelCredentials credentials);

    /// <summary>
    /// Sends a normalized message to the platform conversation.
    /// </summary>
    /// <param name="message">The normalized outbound message (target conversation + text + optional attachments).</param>
    /// <param name="credentials">The tenant credentials used to authenticate the outbound API call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A result containing the delivery <see cref="OutboundReceipt"/>, or an error.</returns>
    Task<Result<OutboundReceipt>> SendAsync(
        OutboundMessage message,
        ChannelCredentials credentials,
        CancellationToken cancellationToken = default);
}
