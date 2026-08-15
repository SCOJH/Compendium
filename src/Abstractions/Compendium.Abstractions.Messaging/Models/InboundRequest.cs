// -----------------------------------------------------------------------
// <copyright file="InboundRequest.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Messaging.Models;

/// <summary>
/// A raw inbound webhook request as received by the host, decoupled from any web framework so
/// connectors can verify signatures and parse payloads without an ASP.NET dependency.
/// </summary>
/// <param name="Headers">The request headers (case-insensitive keys recommended).</param>
/// <param name="Body">The raw request body as a UTF-8 string (needed verbatim for HMAC verification).</param>
/// <param name="QueryString">The raw query string, if any (used by some verification handshakes).</param>
public sealed record InboundRequest(
    IReadOnlyDictionary<string, string> Headers,
    string Body,
    string? QueryString = null);
