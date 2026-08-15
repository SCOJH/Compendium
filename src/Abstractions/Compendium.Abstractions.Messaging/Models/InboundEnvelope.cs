// -----------------------------------------------------------------------
// <copyright file="InboundEnvelope.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Messaging.Models;

/// <summary>
/// The result of parsing an inbound webhook: the normalized messages it carried (possibly none)
/// plus an optional platform handshake response the host must echo back.
/// </summary>
/// <param name="Messages">Zero or more normalized messages. A delivery receipt or non-message
/// event yields an empty list.</param>
/// <param name="Acknowledgement">An optional response the host must return verbatim to complete a
/// platform verification handshake (for example a Slack <c>url_verification</c> challenge or a
/// WhatsApp <c>hub.challenge</c>). When <see langword="null"/>, the host returns <c>200 OK</c>.</param>
public sealed record InboundEnvelope(
    IReadOnlyList<InboundMessage> Messages,
    ChannelAck? Acknowledgement = null)
{
    /// <summary>An empty envelope (no messages, no handshake) — a plain acknowledged webhook.</summary>
    public static InboundEnvelope Empty { get; } = new(Array.Empty<InboundMessage>());
}

/// <summary>
/// A verbatim HTTP response a connector asks the host to return, used for platform verification
/// handshakes that require echoing a token before message delivery is enabled.
/// </summary>
/// <param name="StatusCode">The HTTP status code to return.</param>
/// <param name="Body">The response body, when any.</param>
/// <param name="ContentType">The response content type, when a specific one is required.</param>
public sealed record ChannelAck(int StatusCode, string? Body = null, string? ContentType = null);
