// -----------------------------------------------------------------------
// <copyright file="MessagingPlatforms.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Messaging;

/// <summary>
/// Well-known platform identifiers returned by <see cref="IMessagingConnector.Platform"/>.
/// Open by design — a new connector may use any stable lowercase identifier.
/// </summary>
public static class MessagingPlatforms
{
    /// <summary>Telegram Bot API.</summary>
    public const string Telegram = "telegram";

    /// <summary>Slack (Events API + Web API).</summary>
    public const string Slack = "slack";

    /// <summary>Discord (Interactions / Gateway + REST).</summary>
    public const string Discord = "discord";

    /// <summary>Microsoft Teams (Bot Framework).</summary>
    public const string Teams = "teams";

    /// <summary>WhatsApp Cloud API.</summary>
    public const string WhatsApp = "whatsapp";

    /// <summary>Generic signed HTTP webhook (no specific platform).</summary>
    public const string Webhook = "webhook";
}
